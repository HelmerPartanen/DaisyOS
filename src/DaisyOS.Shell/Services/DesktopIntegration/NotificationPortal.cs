using System.Collections.Concurrent;
using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Services.DesktopIntegration;

/// <summary>Routes sandbox notifications to DaisyOS's existing notification center.</summary>
internal sealed class NotificationPortal(DBusConnection bus) : IDisposable
{
    private sealed record Item(string App, string Id, Dictionary<string, (string Action, VariantValue? Target)> Actions);
    private readonly ConcurrentDictionary<uint, Item> _items = new();
    private IDisposable? _actions;
    public async Task StartAsync()
    {
        _actions = await bus.AddMatchAsync<(uint Id, string Action)>(new MatchRule
        {
            Type = MessageType.Signal, Sender = "org.freedesktop.Notifications", Path = "/org/freedesktop/Notifications", Interface = "org.freedesktop.Notifications", Member = "ActionInvoked"
        }, (message, _) => { var reader = message.GetBodyReader(); return (reader.ReadUInt32(), reader.ReadString()); }, notification =>
        {
            if (!notification.HasValue || !_items.TryGetValue(notification.Value.Id, out var item) || !item.Actions.TryGetValue(notification.Value.Action, out var action)) return;
            using var writer = bus.GetMessageWriter();
            writer.WriteSignalHeader(path: "/org/freedesktop/portal/desktop", @interface: "org.freedesktop.impl.portal.Notification", member: "ActionInvoked", signature: "sssav");
            writer.WriteString(item.App); writer.WriteString(item.Id); writer.WriteString(action.Action);
            writer.WriteArray(action.Target.HasValue ? new[] { action.Target.Value } : []); bus.TrySendMessage(writer.CreateMessage());
        }, emitOnCapturedContext: false);
    }
    public async Task AddAsync(string app, string id, Dictionary<string, VariantValue> data)
    {
        var previous = _items.FirstOrDefault(pair => pair.Value.App == app && pair.Value.Id == id).Key;
        var actions = new List<string>();
        var map = new Dictionary<string, (string, VariantValue?)>();
        if (data.TryGetValue("default-action", out var defaultAction))
        {
            actions.AddRange(["default", "Open"]);
            map["default"] = (defaultAction.GetString(), data.TryGetValue("default-action-target", out var target) ? (VariantValue?)target : null);
        }
        if (data.TryGetValue("buttons", out var buttons))
            for (var i = 0; i < buttons.Count; i++)
            {
                var button = buttons.GetItem(i).GetDictionary<string, VariantValue>();
                if (!button.TryGetValue("action", out var action) || !button.TryGetValue("label", out var label)) continue;
                var key = "button-" + i; actions.AddRange([key, label.GetString()]);
                map[key] = (action.GetString(), button.TryGetValue("target", out var target) ? (VariantValue?)target : null);
            }
        MessageBuffer call;
        using (var writer = bus.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: "org.freedesktop.Notifications", path: "/org/freedesktop/Notifications", @interface: "org.freedesktop.Notifications", member: "Notify", signature: "susssasa{sv}i");
            writer.WriteString(app); writer.WriteUInt32(previous); writer.WriteString("");
            writer.WriteString(data.TryGetValue("title", out var title) ? title.GetString() : app);
            writer.WriteString(data.TryGetValue("body", out var body) ? body.GetString() : "");
            writer.WriteArray(actions.ToArray()); writer.WriteDictionary(new Dictionary<string, VariantValue> { ["desktop-entry"] = app }); writer.WriteInt32(-1);
            call = writer.CreateMessage();
        }
        var result = await bus.CallMethodAsync(call, (reply, _) => reply.GetBodyReader().ReadUInt32(), (object?)null);
        if (previous != 0) _items.TryRemove(previous, out _);
        _items[result] = new Item(app, id, map);
    }
    public async Task RemoveAsync(string app, string id)
    {
        var previous = _items.FirstOrDefault(pair => pair.Value.App == app && pair.Value.Id == id).Key;
        if (previous == 0) return;
        _items.TryRemove(previous, out _);
        MessageBuffer call;
        using (var writer = bus.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: "org.freedesktop.Notifications", path: "/org/freedesktop/Notifications", @interface: "org.freedesktop.Notifications", member: "CloseNotification", signature: "u"); writer.WriteUInt32(previous); call = writer.CreateMessage();
        }
        await bus.CallMethodAsync(call);
    }
    public void Dispose() => _actions?.Dispose();
}
