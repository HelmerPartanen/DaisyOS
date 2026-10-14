using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using DaisyOS.Shell.Views.Components.Notifications;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Services.Notifications;

/// <summary>
/// Implements the org.freedesktop.Notifications D-Bus daemon specification.
/// Allows desktop applications (Chrome, Discord, Spotify, Steam, etc.) to
/// post notifications directly into DaisyOS's native notification system.
/// </summary>
public sealed class FreedesktopNotificationDaemonService : IAsyncDisposable
{
    public const string BusName = "org.freedesktop.Notifications";
    public const string ObjectPath = "/org/freedesktop/Notifications";
    public const string InterfaceName = "org.freedesktop.Notifications";

    private readonly INotificationService _notificationService;
    private DBusConnection? _connection;
    private NotificationHandler? _handler;
    private uint _notificationIdCounter;
    private readonly ConcurrentDictionary<uint, byte> _externalIds = new();

    public bool IsAvailable { get; private set; }

    public FreedesktopNotificationDaemonService(INotificationService notificationService)
    {
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux() || _connection is not null)
        {
            return;
        }

        try
        {
            var connection = new DBusConnection(DBusAddress.Session
                ?? throw new InvalidOperationException("No session D-Bus is available."));
            await connection.ConnectAsync();

            var handler = new NotificationHandler(OnNotify, OnClose);
            connection.AddMethodHandler(handler);

            if (!await connection.TryRequestNameAsync(BusName, RequestNameOptions.ReplaceExisting))
            {
                handler.Dispose();
                connection.Dispose();
                Console.Error.WriteLine("[notifications] Another notification daemon already owns org.freedesktop.Notifications.");
                return;
            }

            _connection = connection;
            _handler = handler;
            _notificationService.NotificationsChanged += OnNotificationsChanged;
            _notificationService.NotificationActionTriggered += OnActionTriggered;
            IsAvailable = true;
            Console.Error.WriteLine("[notifications] Registered org.freedesktop.Notifications service.");
        }
        catch (Exception ex)
        {
            _handler?.Dispose();
            _handler = null;
            _connection?.Dispose();
            _connection = null;
            IsAvailable = false;
            Console.Error.WriteLine($"[notifications] Could not register notification daemon: {ex.Message}");
        }
    }

    private uint OnNotify(
        string appName,
        uint replacesId,
        string appIcon,
        string summary,
        string body,
        IReadOnlyList<string> actions,
        IReadOnlyDictionary<string, VariantValue> hints,
        int expireTimeout)
    {
        var id = replacesId > 0 ? replacesId : Interlocked.Increment(ref _notificationIdCounter);
        var desktopEntry = GetStringHint(hints, "desktop-entry");
        var hintedIcon = GetStringHint(hints, "image-path") ?? GetStringHint(hints, "image_path");
        var category = ParseCategory(GetStringHint(hints, "category"));
        var urgency = ParseUrgency(hints);
        var actionPairs = new List<(string Id, string Label)>();
        for (var index = 0; index + 1 < actions.Count; index += 2)
            if (!string.IsNullOrWhiteSpace(actions[index]))
                actionPairs.Add((actions[index], string.IsNullOrWhiteSpace(actions[index + 1]) ? "Open" : actions[index + 1]));
        var primary = actionPairs.FirstOrDefault();
        var secondary = actionPairs.Skip(1).FirstOrDefault();

        Dispatcher.UIThread.Post(() =>
        {
            var notification = new NotificationItem(
                Title: string.IsNullOrWhiteSpace(summary) ? appName : summary,
                Body: body,
                CreatedAt: DateTimeOffset.UtcNow,
                Id: id.ToString(),
                SourceAppId: desktopEntry ?? appName,
                SourceAppName: appName,
                Icon: FirstNonEmpty(appIcon, desktopEntry),
                PreviewImage: hintedIcon,
                ActionId: primary.Id, ActionLabel: primary.Label,
                SecondaryActionId: secondary.Id, SecondaryActionLabel: secondary.Label,
                Category: category,
                Urgency: urgency,
                TimeoutSeconds: (int)NotificationToastHost.DisplayDuration.TotalSeconds
            );

            _externalIds[id] = 0;
            _notificationService.Add(notification);
        });

        return id;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? GetStringHint(IReadOnlyDictionary<string, VariantValue> hints, string key)
    {
        if (!hints.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.Type == VariantValueType.Variant)
        {
            value = value.GetVariantValue();
        }

        return value.Type == VariantValueType.String ? value.GetString() : null;
    }

    private static NotificationUrgency ParseUrgency(IReadOnlyDictionary<string, VariantValue> hints)
    {
        if (!hints.TryGetValue("urgency", out var value))
        {
            return NotificationUrgency.Normal;
        }

        if (value.Type == VariantValueType.Variant)
        {
            value = value.GetVariantValue();
        }

        if (value.Type != VariantValueType.Byte)
        {
            return NotificationUrgency.Normal;
        }

        return value.GetByte() switch
        {
            0 => NotificationUrgency.Low,
            2 => NotificationUrgency.Critical,
            _ => NotificationUrgency.Normal
        };
    }

    private static NotificationCategory ParseCategory(string? category) => category?.Split('.')[0] switch
    {
        "device" => NotificationCategory.System,
        "network" => NotificationCategory.Network,
        "email" or "im" or "x-daisyos.application" => NotificationCategory.Application,
        _ => NotificationCategory.Application
    };

    private void OnClose(uint id)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_externalIds.TryRemove(id, out _)) EmitClosed(id, 3);
            _notificationService.DismissById(id.ToString());
        });
    }

    private void OnNotificationsChanged(object? sender, EventArgs e)
    {
        var retained = _notificationService.GetNotifications().Select(item => item.Id).ToHashSet();
        foreach (var id in _externalIds.Keys)
            if (!retained.Contains(id.ToString()) && _externalIds.TryRemove(id, out _)) EmitClosed(id, 2);
    }

    private void OnActionTriggered(object? sender, NotificationItem item)
    {
        if (_connection is null || !uint.TryParse(item.Id, out var id) || !_externalIds.ContainsKey(id) || item.ActionId is null) return;
        using var writer = _connection.GetMessageWriter();
        writer.WriteSignalHeader(path: ObjectPath, @interface: InterfaceName, member: "ActionInvoked", signature: "us");
        writer.WriteUInt32(id);
        writer.WriteString(item.ActionId);
        _connection.TrySendMessage(writer.CreateMessage());
    }

    private void EmitClosed(uint id, uint reason)
    {
        if (_connection is null) return;
        using var writer = _connection.GetMessageWriter();
        writer.WriteSignalHeader(path: ObjectPath, @interface: InterfaceName, member: "NotificationClosed", signature: "uu");
        writer.WriteUInt32(id);
        writer.WriteUInt32(reason);
        _connection.TrySendMessage(writer.CreateMessage());
    }

    public ValueTask DisposeAsync()
    {
        _notificationService.NotificationsChanged -= OnNotificationsChanged;
        _notificationService.NotificationActionTriggered -= OnActionTriggered;
        _externalIds.Clear();
        _handler?.Dispose();
        _handler = null;
        _connection?.Dispose();
        _connection = null;
        IsAvailable = false;
        return ValueTask.CompletedTask;
    }

    private sealed class NotificationHandler : IPathMethodHandler, IDisposable
    {
        private readonly Func<string, uint, string, string, string, IReadOnlyList<string>, IReadOnlyDictionary<string, VariantValue>, int, uint> _notify;
        private readonly Action<uint> _close;

        private static readonly ReadOnlyMemory<byte> IntrospectionXml = """
            <interface name="org.freedesktop.Notifications">
              <method name="GetCapabilities">
                <arg direction="out" type="as" name="capabilities"/>
              </method>
              <method name="Notify">
                <arg direction="in" type="s" name="app_name"/>
                <arg direction="in" type="u" name="replaces_id"/>
                <arg direction="in" type="s" name="app_icon"/>
                <arg direction="in" type="s" name="summary"/>
                <arg direction="in" type="s" name="body"/>
                <arg direction="in" type="as" name="actions"/>
                <arg direction="in" type="a{sv}" name="hints"/>
                <arg direction="in" type="i" name="expire_timeout"/>
                <arg direction="out" type="u" name="id"/>
              </method>
              <method name="CloseNotification">
                <arg direction="in" type="u" name="id"/>
              </method>
              <method name="GetServerInformation">
                <arg direction="out" type="s" name="name"/>
                <arg direction="out" type="s" name="vendor"/>
                <arg direction="out" type="s" name="version"/>
                <arg direction="out" type="s" name="spec_version"/>
              </method>
              <signal name="NotificationClosed">
                <arg type="u" name="id"/>
                <arg type="u" name="reason"/>
              </signal>
              <signal name="ActionInvoked">
                <arg type="u" name="id"/>
                <arg type="s" name="action_key"/>
              </signal>
            </interface>
            """u8.ToArray();

        private static readonly string[] Capabilities = ["actions", "body", "icon-static", "persistence"];

        public string Path => ObjectPath;
        public bool HandlesChildPaths => false;

        public NotificationHandler(
            Func<string, uint, string, string, string, IReadOnlyList<string>, IReadOnlyDictionary<string, VariantValue>, int, uint> notify,
            Action<uint> close)
        {
            _notify = notify;
            _close = close;
        }

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.IsDBusIntrospectRequest)
            {
                context.ReplyIntrospectXml([IntrospectionXml]);
                return default;
            }

            var request = context.Request;
            var member = request.MemberAsString;

            if (request.InterfaceAsString == InterfaceName)
            {
                switch (member)
                {
                    case "GetCapabilities":
                    {
                        using var writer = context.CreateReplyWriter("as");
                        writer.WriteArray(Capabilities);
                        context.Reply(writer.CreateMessage());
                        return default;
                    }
                    case "GetServerInformation":
                    {
                        using var writer = context.CreateReplyWriter("ssss");
                        writer.WriteString("DaisyOS Shell");
                        writer.WriteString("DaisyOS");
                        writer.WriteString("1.0");
                        writer.WriteString("1.2");
                        context.Reply(writer.CreateMessage());
                        return default;
                    }
                    case "Notify":
                    {
                        try
                        {
                            var reader = request.GetBodyReader();
                            var appName = reader.ReadString();
                            var replacesId = reader.ReadUInt32();
                            var appIcon = reader.ReadString();
                            var summary = reader.ReadString();
                            var body = reader.ReadString();
                            var actions = reader.ReadArrayOfString();
                            var hints = reader.ReadDictionaryOfStringToVariantValue();
                            var expireTimeout = reader.ReadInt32();
                            var id = _notify(appName, replacesId, appIcon, summary, body, actions, hints, expireTimeout);

                            using var writer = context.CreateReplyWriter("u");
                            writer.WriteUInt32(id);
                            context.Reply(writer.CreateMessage());
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[notifications] Error handling Notify: {ex.Message}");
                        }
                        return default;
                    }
                    case "CloseNotification":
                    {
                        try
                        {
                            var reader = request.GetBodyReader();
                            var id = reader.ReadUInt32();
                            _close(id);

                            using var writer = context.CreateReplyWriter("");
                            context.Reply(writer.CreateMessage());
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[notifications] Error handling CloseNotification: {ex.Message}");
                        }
                        return default;
                    }
                }
            }

            return default;
        }

        public void Dispose()
        {
        }
    }
}
