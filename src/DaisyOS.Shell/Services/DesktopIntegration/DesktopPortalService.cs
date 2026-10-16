using System.Text;
using Microsoft.Win32.SafeHandles;
using IOPath = global::System.IO.Path;
using Avalonia.Controls;
using Avalonia.Threading;
using DaisyOS.Shell.Apps.Files.Picker;
using Tmds.DBus.Protocol;
using System.Collections.Concurrent;

namespace DaisyOS.Shell.Services.DesktopIntegration;

/// <summary>The portal frontend authenticates application identity; this backend owns the consent UI.</summary>
internal sealed class DesktopPortalService : IPathMethodHandler, IDisposable
{
    internal const string BusName = "org.freedesktop.impl.portal.desktop.daisyos";
    private const string Prefix = "org.freedesktop.impl.portal.";
    private readonly DBusConnection _bus = new(DBusAddress.Session ?? throw new IOException("No session bus."));
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _requests = new();
    private ScreenCastPortal? _capture;
    private GlobalShortcutsPortal? _shortcuts;
    private InputCapturePortal? _inputCapture;
    private IDisposable? _frontendWatch;
    private PortalSettings? _settings;
    private NotificationPortal? _notifications;
    private readonly PrintPortal _printing = new();
    public void UpdateAppearance() => _settings?.Update();
    private static readonly Dictionary<string, Dictionary<string, string>> Methods = new()
    {
        ["InputCapture"] = new() { ["CreateSession"] = "oossa{sv}", ["GetZones"] = "oosa{sv}", ["SetPointerBarriers"] = "oosa{sv}aa{sv}u", ["Enable"] = "osa{sv}", ["Disable"] = "osa{sv}", ["Release"] = "osa{sv}" },
        ["GlobalShortcuts"] = new() { ["CreateSession"] = "oosa{sv}", ["BindShortcuts"] = "ooa(sa{sv})sa{sv}", ["ListShortcuts"] = "oo" },
        ["Background"] = new() { ["NotifyBackground"] = "oss" },
        ["Wallpaper"] = new() { ["SetWallpaperURI"] = "osssa{sv}" },
        ["DynamicLauncher"] = new() { ["PrepareInstall"] = "osssva{sv}" },
        ["Usb"] = new() { ["AcquireDevices"] = "ossa(sa{sv}a{sv})a{sv}" },
        ["Print"] = new() { ["PreparePrint"] = "osssa{sv}a{sv}a{sv}", ["Print"] = "osssha{sv}" },
        ["Notification"] = new(),
        ["Lockdown"] = new(),
        ["Settings"] = new(),
        ["RemoteDesktop"] = new() { ["CreateSession"] = "oosa{sv}", ["SelectDevices"] = "oosa{sv}", ["Start"] = "oossa{sv}" },
        ["ScreenCast"] = new() { ["CreateSession"] = "oosa{sv}", ["SelectSources"] = "oosa{sv}", ["Start"] = "oossa{sv}" },
        ["Screenshot"] = new() { ["Screenshot"] = "ossa{sv}", ["PickColor"] = "ossa{sv}" },
        ["Account"] = new() { ["GetUserInformation"] = "ossa{sv}" },
        ["AppChooser"] = new() { ["ChooseApplication"] = "ossasa{sv}" },
        ["Access"] = new() { ["AccessDialog"] = "osssssa{sv}" },
        ["FileChooser"] = new() { ["OpenFile"] = "osssa{sv}", ["SaveFile"] = "osssa{sv}", ["SaveFiles"] = "osssa{sv}" },
    };
    public string Path => "/org/freedesktop/portal/desktop";
    public bool HandlesChildPaths => true;

    public async Task StartAsync()
    {
        await _bus.ConnectAsync();
        _capture = new ScreenCastPortal(_bus);
        _shortcuts = new GlobalShortcutsPortal(_bus);
        _inputCapture = new InputCapturePortal(_bus);
        _settings = new PortalSettings(_bus);
        _notifications = new NotificationPortal(_bus);
        await _notifications.StartAsync();
        _frontendWatch = await _bus.AddMatchAsync<int>(new MatchRule
        {
            Type = MessageType.Signal, Sender = "org.freedesktop.DBus", Interface = "org.freedesktop.DBus",
            Member = "NameOwnerChanged", Arg0 = "org.freedesktop.portal.Desktop"
        }, (message, _) => 0, notification =>
        {
            if (notification.HasValue) Dispatcher.UIThread.Post(() =>
            {
                foreach (var request in _requests.Values)
                    try { request.Cancel(); } catch (ObjectDisposedException) { }
                _capture?.Dispose(); _shortcuts?.Dispose(); _inputCapture?.Dispose();
            });
        }, emitOnCapturedContext: false);
        _bus.AddMethodHandler(this);
        if (!await _bus.TryRequestNameAsync(BusName)) throw new IOException("A DaisyOS portal is already running.");
        Console.Error.WriteLine("[desktop-services] DaisyOS portal ready.");
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml(Methods.Select(pair => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(
                $"<interface name='{Prefix}{pair.Key}'>" + string.Concat(pair.Value.Select(method =>
                    $"<method name='{method.Key}'>" + string.Concat(ParseSignature(method.Value).Select(type => $"<arg direction='in' type='{type}'/>")) +
                    "<arg direction='out' type='u'/>" + (pair.Key == "Wallpaper" ? "" : "<arg direction='out' type='a{sv}'/>") + "</method>")) +
                ExtraIntrospection(pair.Key) + string.Concat(Properties(pair.Key).Select(property =>
                    $"<property name='{property.Key}' type='{(property.Key == "SupportedOptions" ? "a{sv}" : pair.Key == "Lockdown" ? "b" : "u")}' access='read'/>")) + "</interface>")).ToArray());
            return default;
        }
        SafeFileHandle? printFile = null;
        try
        {
            var message = context.Request;
            if (message.InterfaceAsString == Prefix + "DynamicLauncher" && message.MemberAsString == "RequestInstallToken")
                return new ValueTask(TrustedAsync(context, () => { using var writer = context.CreateReplyWriter("u"); writer.WriteUInt32(2); context.Reply(writer.CreateMessage()); }));
            if (message.InterfaceAsString == Prefix + "Background" && message.MemberAsString == "GetAppState")
                return new ValueTask(BackgroundStateAsync(context));
            if (message.InterfaceAsString == Prefix + "Background" && message.MemberAsString == "EnableAutostart")
                return new ValueTask(TrustedAsync(context, () => { using var writer = context.CreateReplyWriter("b"); writer.WriteBool(false); context.Reply(writer.CreateMessage()); }));
            if (message.InterfaceAsString == Prefix + "Notification")
            {
                var reader = message.GetBodyReader(); var app = reader.ReadString(); var id = reader.ReadString();
                var data = message.MemberAsString == "AddNotification" ? reader.ReadDictionaryOfStringToVariantValue() : new Dictionary<string, VariantValue>();
                return new ValueTask(NotificationAsync(context, app, id, data));
            }
            if (message.InterfaceAsString == PortalSettings.Interface)
                return new ValueTask(ReadSettingsAsync(context));
            if (message.InterfaceAsString == "org.freedesktop.DBus.Properties")
            {
                var reader = message.GetBodyReader();
                var iface = reader.ReadString();
                if (!Methods.ContainsKey(iface.Replace(Prefix, "", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Unknown portal interface.");
                var properties = Properties(iface[Prefix.Length..]);
                if (message.MemberAsString == "GetAll")
                {
                    using var writer = context.CreateReplyWriter("a{sv}");
                    writer.WriteDictionary(properties);
                    context.Reply(writer.CreateMessage());
                }
                else if (message.MemberAsString == "Get" && properties.TryGetValue(reader.ReadString(), out var property))
                {
                    using var writer = context.CreateReplyWriter("v"); writer.WriteVariant(property); context.Reply(writer.CreateMessage());
                }
                else context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", "Unknown property.");
                return default;
            }
            if ((message.InterfaceAsString == Prefix + "Request" || message.InterfaceAsString == Prefix + "Session") && message.MemberAsString == "Close")
                return new ValueTask(CancelAsync(context, message.PathAsString!, message.InterfaceAsString == Prefix + "Session"));
            if ((message.InterfaceAsString == Prefix + "RemoteDesktop" || message.InterfaceAsString == Prefix + "InputCapture") && message.MemberAsString == "ConnectToEIS" && message.SignatureAsString == "osa{sv}")
            {
                var input = message.GetBodyReader(); var session = input.ReadObjectPath().ToString(); var app = input.ReadString();
                return new ValueTask(ConnectToEisAsync(context, session, app, message.InterfaceAsString == Prefix + "InputCapture"));
            }
            var name = message.InterfaceAsString?.Replace(Prefix, "", StringComparison.Ordinal) ?? "";
            if (!Methods.TryGetValue(name, out var methods) || !methods.TryGetValue(message.MemberAsString!, out var signature))
            {
                context.ReplyError("org.freedesktop.DBus.Error.UnknownMethod", "Unsupported portal method.");
                return default;
            }
            if (message.SignatureAsString != signature) throw new InvalidDataException("Incorrect request signature.");
            var body = message.GetBodyReader();
            string[]? choices = null;
            var dictionaries = new List<Dictionary<string, VariantValue>>();
            var extra = new Dictionary<string, VariantValue>();
            var args = new List<string>();
            foreach (var type in ParseSignature(signature))
            {
                if (type == "a{sv}") { dictionaries.Add(body.ReadDictionaryOfStringToVariantValue()); continue; }
                if (type == "u") { extra["daisy_zone_set"] = body.ReadUInt32(); continue; }
                if (type == "aa{sv}")
                {
                    var barriers = new Tmds.DBus.Protocol.Array<Dict<string, VariantValue>>(); var end = body.ReadArrayStart(DBusType.Array);
                    while (body.HasNext(end)) barriers.Add(new Dict<string, VariantValue>(body.ReadDictionaryOfStringToVariantValue()));
                    extra["daisy_barriers"] = barriers; continue;
                }
                if (type == "a(sa{sv})")
                {
                    var shortcuts = new Tmds.DBus.Protocol.Array<Struct<string, Dict<string, VariantValue>>>();
                    var end = body.ReadArrayStart(DBusType.Struct);
                    while (body.HasNext(end)) { body.AlignStruct(); var id = body.ReadString(); shortcuts.Add(new Struct<string, Dict<string, VariantValue>>(id, new Dict<string, VariantValue>(body.ReadDictionaryOfStringToVariantValue()))); }
                    extra["daisy_shortcuts"] = shortcuts; continue;
                }
                if (type == "v") { extra["daisy_icon"] = body.ReadVariantValue(); continue; }
                if (type == "a(sa{sv}a{sv})")
                {
                    var devices = new Tmds.DBus.Protocol.Array<Struct<string, Dict<string, VariantValue>, Dict<string, VariantValue>>>();
                    var end = body.ReadArrayStart(DBusType.Struct);
                    while (body.HasNext(end))
                    {
                        body.AlignStruct(); var id = body.ReadString();
                        var info = new Dict<string, VariantValue>(body.ReadDictionaryOfStringToVariantValue());
                        var access = new Dict<string, VariantValue>(body.ReadDictionaryOfStringToVariantValue());
                        devices.Add(new Struct<string, Dict<string, VariantValue>, Dict<string, VariantValue>>(id, info, access));
                    }
                    extra["daisy_devices"] = devices; continue;
                }
                if (type == "h") { printFile = body.ReadHandle<SafeFileHandle>(); continue; }
                if (type == "as") { choices = body.ReadArrayOfString(); continue; }
                args.Add(type == "o" ? body.ReadObjectPath().ToString() : body.ReadString());
            }
            var options = dictionaries.Count > 0 ? dictionaries[^1] : new Dictionary<string, VariantValue>();
            foreach (var pair in extra) options[pair.Key] = pair.Value;
            if (choices is not null) options["daisy_choices"] = VariantValue.Array(choices);
            return new ValueTask(DispatchAsync(context, name, message.MemberAsString!, args, options, printFile));
        }
        catch (Exception ex) { printFile?.Dispose(); context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", ex.Message); return default; }
    }

    private static Dictionary<string, VariantValue> Properties(string name)
    {
        var properties = new Dictionary<string, VariantValue> { ["version"] = name switch { "FileChooser" or "ScreenCast" => 3u, "Screenshot" or "RemoteDesktop" => 2u, _ => 1u } };
        if (name == "ScreenCast") { properties["AvailableSourceTypes"] = 3u; properties["AvailableCursorModes"] = 7u; }
        if (name == "RemoteDesktop") properties["AvailableDeviceTypes"] = 7u;
        if (name == "Lockdown")
        {
            properties.Clear();
            foreach (var key in new[] { "disable-printing", "disable-save-to-disk", "disable-application-handlers", "disable-location", "disable-camera", "disable-microphone", "disable-sound-output" }) properties[key] = false;
        }
        if (name == "InputCapture") properties["SupportedCapabilities"] = 7u;
        if (name == "DynamicLauncher") properties["SupportedLauncherTypes"] = 3u;
        if (name == "Notification") properties["SupportedOptions"] = new Dict<string, VariantValue>();
        return properties;
    }
    private static string ExtraIntrospection(string name) => name switch
    {
        "Settings" => "<method name='Read'><arg direction='in' type='s'/><arg direction='in' type='s'/><arg direction='out' type='v'/></method><method name='ReadAll'><arg direction='in' type='as'/><arg direction='out' type='a{sa{sv}}'/></method><signal name='SettingChanged'><arg type='s'/><arg type='s'/><arg type='v'/></signal>",
        "Notification" => "<method name='AddNotification'><arg direction='in' type='s'/><arg direction='in' type='s'/><arg direction='in' type='a{sv}'/></method><method name='RemoveNotification'><arg direction='in' type='s'/><arg direction='in' type='s'/></method><signal name='ActionInvoked'><arg type='s'/><arg type='s'/><arg type='s'/><arg type='av'/></signal>",
        "RemoteDesktop" or "InputCapture" => "<method name='ConnectToEIS'><arg direction='in' type='o'/><arg direction='in' type='s'/><arg direction='in' type='a{sv}'/><arg direction='out' type='h'/></method>",
        "Background" => "<method name='GetAppState'><arg direction='out' type='a{sv}'/></method><method name='EnableAutostart'><arg direction='in' type='s'/><arg direction='in' type='b'/><arg direction='in' type='as'/><arg direction='in' type='u'/><arg direction='out' type='b'/></method>",
        "DynamicLauncher" => "<method name='RequestInstallToken'><arg direction='in' type='s'/><arg direction='in' type='a{sv}'/><arg direction='out' type='u'/></method>",
        _ => ""
    };

    private async Task BackgroundStateAsync(MethodContext context)
    {
        try
        {
            if (!await IsFrontendAsync(context.Request.SenderAsString!)) throw new UnauthorizedAccessException();
            var state = new Dictionary<string, VariantValue>();
            try
            {
                MessageBuffer call;
                using (var writer = _bus.GetMessageWriter())
                {
                    writer.WriteMethodCallHeader(destination: Compositor.KWinWindowBridge.BusName, path: Compositor.KWinWindowBridge.ObjectPath, @interface: Compositor.KWinWindowBridge.InterfaceName, member: "GetCaptureWindows"); call = writer.CreateMessage();
                }
                var json = await _bus.CallMethodAsync(call, (reply, _) => reply.GetBodyReader().ReadString(), (object?)null).WaitAsync(TimeSpan.FromSeconds(2));
                if (Compositor.KWinWindowSnapshotParser.TryParse(json, out var windows))
                    foreach (var group in windows.GroupBy(window => window.AppId)) state[group.Key] = group.Any(window => window.IsActive) ? 2u : 1u;
            }
            catch (Exception ex) { Console.Error.WriteLine($"[portal] Application state unavailable: {ex.Message}"); }
            using var response = context.CreateReplyWriter("a{sv}"); response.WriteDictionary(state); context.Reply(response.CreateMessage());
        }
        catch (Exception) { context.ReplyError("org.freedesktop.DBus.Error.AccessDenied", "Requests must come from the desktop portal."); }
    }

    private async Task NotificationAsync(MethodContext context, string app, string id, Dictionary<string, VariantValue> data)
    {
        try
        {
            if (!await IsFrontendAsync(context.Request.SenderAsString!)) throw new UnauthorizedAccessException();
            if (context.Request.MemberAsString == "AddNotification") await _notifications!.AddAsync(app, id, data);
            else if (context.Request.MemberAsString == "RemoveNotification") await _notifications!.RemoveAsync(app, id);
            else throw new NotSupportedException();
            using var writer = context.CreateReplyWriter(""); context.Reply(writer.CreateMessage());
        }
        catch (Exception ex) { context.ReplyError("org.freedesktop.DBus.Error.Failed", ex.Message); }
    }

    private static async Task ReadSettingsAsync(MethodContext context)
    {
        // Appearance is public information. The frontend reads it while starting,
        // before it owns its well-known name. UI properties must be read on the UI thread.
        try { await Dispatcher.UIThread.InvokeAsync(() => PortalSettings.ReplyFor(context)()); }
        catch (Exception ex) { context.ReplyError("org.freedesktop.DBus.Error.Failed", ex.Message); }
    }

    private async Task TrustedAsync(MethodContext context, Action action)
    {
        try
        {
            if (!await IsFrontendAsync(context.Request.SenderAsString!))
            { context.ReplyError("org.freedesktop.DBus.Error.AccessDenied", "Requests must come from the desktop portal."); return; }
            action();
        }
        catch (Exception ex) { context.ReplyError("org.freedesktop.DBus.Error.Failed", ex.Message); }
    }

    private async Task ConnectToEisAsync(MethodContext context, string session, string app, bool captureInput)
    {
        try
        {
            if (!await IsFrontendAsync(context.Request.SenderAsString!)) throw new UnauthorizedAccessException();
            using var handle = await Dispatcher.UIThread.InvokeAsync(() => captureInput ? _inputCapture!.ConnectAsync(session, app) : _capture!.ConnectToEisAsync(session, app));
            using var writer = context.CreateReplyWriter("h"); writer.WriteHandle(handle); context.Reply(writer.CreateMessage());
        }
        catch (Exception) { context.ReplyError("org.freedesktop.DBus.Error.AccessDenied", "Remote input is not authorized or is unavailable."); }
    }

    private async Task<bool> IsFrontendAsync(string sender)
    {
        MessageBuffer call;
        using (var writer = _bus.GetMessageWriter())
        {
        writer.WriteMethodCallHeader(destination: "org.freedesktop.DBus", path: "/org/freedesktop/DBus", @interface: "org.freedesktop.DBus", member: "GetNameOwner", signature: "s");
        writer.WriteString("org.freedesktop.portal.Desktop");
        call = writer.CreateMessage();
        }
        var owner = await _bus.CallMethodAsync(call, (reply, _) => reply.GetBodyReader().ReadString(), (object?)null);
        return sender == owner;
    }

    private async Task CancelAsync(MethodContext context, string path, bool session)
    {
        if (!await IsFrontendAsync(context.Request.SenderAsString!))
        { context.ReplyError("org.freedesktop.DBus.Error.AccessDenied", "Only the portal frontend may cancel requests."); return; }
        if (session) await Dispatcher.UIThread.InvokeAsync(() => { _capture?.Close(path); _shortcuts?.Close(path); _inputCapture?.Close(path); });
        if (_requests.TryGetValue(path, out var stop)) stop.Cancel();
        using var writer = context.CreateReplyWriter(""); context.Reply(writer.CreateMessage());
    }

    private async Task DispatchAsync(MethodContext context, string name, string method, List<string> args, Dictionary<string, VariantValue> options, SafeFileHandle? printFile = null)
    {
        using var ownedFile = printFile;
        try
        {
            if (!await IsFrontendAsync(context.Request.SenderAsString!))
            { context.ReplyError("org.freedesktop.DBus.Error.AccessDenied", "Requests must come from the desktop portal."); return; }
            using var stop = new CancellationTokenSource();
            if (!_requests.TryAdd(args[0], stop)) throw new InvalidOperationException("Duplicate request.");
            try
            {
                var response = await Dispatcher.UIThread.InvokeAsync(() => ShowAsync(name, method, args, options, stop.Token, printFile));
                Reply(context, response.Code, response.Results);
            }
            finally { _requests.TryRemove(args[0], out _); }
        }
        catch (OperationCanceledException) { Reply(context, 1, []); }
        catch (Exception ex) { Console.Error.WriteLine($"[portal] {name}.{method}: {ex.Message}"); Reply(context, 2, []); }
    }

    private async Task<(uint Code, Dictionary<string, VariantValue> Results)> ShowAsync(string name, string method, List<string> args, Dictionary<string, VariantValue> options, CancellationToken stop, SafeFileHandle? printFile = null)
    {
        stop.ThrowIfCancellationRequested();
        if (name == "InputCapture") return await _inputCapture!.HandleAsync(method, args, options, stop);
        if (name == "GlobalShortcuts") return await _shortcuts!.HandleAsync(method, args, options, stop);
        if (name == "Print") return method == "PreparePrint" ? await _printing.PrepareAsync(args, options, stop) : await _printing.PrintAsync(args, options, printFile ?? throw new InvalidDataException("Missing print document."), stop);
        if (name is "ScreenCast" or "RemoteDesktop") return await _capture!.HandleAsync(name, method, args, options, stop);
        if (name is "Screenshot" or "Account" or "AppChooser" or "Wallpaper" or "DynamicLauncher" or "Usb" or "Background") return await AdditionalPortals.ShowAsync(name, method, args, options, stop);
        if (name == "Access")
        {
            var dialog = PortalDialog.Create(() => new PortalDialog(args[3], args[4] + "\n\n" + args[5], Text(options, "grant_label", "Allow"), Text(options, "deny_label", "Deny")));
            var choices = AddChoices(dialog.Fields, options);
            using var registration = stop.Register(() => Dispatcher.UIThread.Post(dialog.Close));
            var accepted = await dialog.Present();
            return (accepted && !stop.IsCancellationRequested ? 0u : 1u, ChoiceResults(choices));
        }
        var directory = Flag(options, "directory") || method == "SaveFiles";
        var picker = PortalDialog.Create(() => new DaisyFilePickerWindow(new DaisyFilePickerOptions(
            args[3], Flag(options, "multiple"), false, Extensions(options), InitialFolder: Folder(options),
            SelectDirectory: directory, SaveFile: method == "SaveFile", SuggestedName: Text(options, "current_name", "Untitled"))));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        picker.Closed += (_, _) => closed.TrySetResult();
        using var cancel = stop.Register(() => Dispatcher.UIThread.Post(picker.Close));
        picker.Show();
        await closed.Task;
        if (!picker.WasConfirmed || stop.IsCancellationRequested) return (1, []);
        var paths = picker.SelectedPaths;
        if (method == "SaveFile" && paths.Any(File.Exists))
        {
            var replace = PortalDialog.Create(() => new PortalDialog("Replace file?", $"{IOPath.GetFileName(paths[0])} already exists. Replace it?", "Replace"));
            using var replaceCancel = stop.Register(() => Dispatcher.UIThread.Post(replace.Close));
            if (!await replace.Present() || stop.IsCancellationRequested) return (1, []);
        }
        if (method == "SaveFiles" && options.TryGetValue("files", out var files))
        {
            var directoryPath = paths[0];
            var destinations = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < files.Count; i++)
            {
                var fileName = IOPath.GetFileName(Encoding.UTF8.GetString(files.GetItem(i).GetArray<byte>()).TrimEnd('\0'));
                if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".." || fileName.IndexOfAny(IOPath.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidDataException("Invalid suggested file name.");
                var candidate = IOPath.Combine(directoryPath, fileName);
                var suffix = 1;
                while (File.Exists(candidate) || Directory.Exists(candidate) || destinations.Contains(candidate))
                    candidate = IOPath.Combine(directoryPath, $"{IOPath.GetFileNameWithoutExtension(fileName)} ({suffix++}){IOPath.GetExtension(fileName)}");
                destinations.Add(candidate);
            }
            paths = destinations.ToArray();
        }
        return (0, new() { ["uris"] = VariantValue.Array(paths.Select(path => new Uri(path).AbsoluteUri).ToArray()), ["writable"] = true });
    }

    private static List<(string Id, Func<string> Value)> AddChoices(StackPanel fields, Dictionary<string, VariantValue> options)
    {
        var result = new List<(string, Func<string>)>();
        if (!options.TryGetValue("choices", out var choices)) return result;
        for (var i = 0; i < choices.Count; i++)
        {
            var choice = choices.GetItem(i);
            var id = choice.GetItem(0).GetString();
            var label = choice.GetItem(1).GetString();
            var items = choice.GetItem(2);
            var initial = choice.GetItem(3).GetString();
            if (items.Count == 0)
            {
                var check = new CheckBox { Content = label, IsChecked = initial == "true" };
                fields.Children.Add(check); result.Add((id, () => check.IsChecked == true ? "true" : "false"));
            }
            else
            {
                var ids = Enumerable.Range(0, items.Count).Select(n => items.GetItem(n).GetItem(0).GetString()).ToArray();
                var combo = new ComboBox { ItemsSource = Enumerable.Range(0, items.Count).Select(n => items.GetItem(n).GetItem(1).GetString()).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(ids, initial)) };
                fields.Children.Add(new TextBlock { Text = label }); fields.Children.Add(combo); result.Add((id, () => ids[combo.SelectedIndex]));
            }
        }
        return result;
    }

    private static Dictionary<string, VariantValue> ChoiceResults(List<(string Id, Func<string> Value)> choices)
    {
        var array = new Tmds.DBus.Protocol.Array<Struct<string, string>>();
        foreach (var (id, value) in choices) array.Add(new Struct<string, string>(id, value()));
        return new() { ["choices"] = array };
    }
    private static string Text(Dictionary<string, VariantValue> options, string key, string fallback) => options.TryGetValue(key, out var v) ? v.GetString() : fallback;
    private static bool Flag(Dictionary<string, VariantValue> options, string key) => options.TryGetValue(key, out var v) && v.GetBool();
    private static string? Folder(Dictionary<string, VariantValue> options)
    {
        if (!options.TryGetValue("current_folder", out var v)) return null;
        return Encoding.UTF8.GetString(v.GetArray<byte>()).TrimEnd('\0');
    }
    private static IReadOnlyCollection<string> Extensions(Dictionary<string, VariantValue> options)
    {
        // Keep arbitrary glob and MIME filters permissive rather than hiding valid selections.
        if (!options.TryGetValue("current_filter", out var filter)) return [];
        var rules = filter.GetItem(1);
        var result = new List<string>();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules.GetItem(i);
            var pattern = rule.GetItem(1).GetString();
            if (rule.GetItem(0).GetUInt32() != 0 || !pattern.StartsWith("*.") || pattern[2..].IndexOfAny(['*', '?', '[']) >= 0) return [];
            result.Add(pattern[1..]);
        }
        return result;
    }
    private static IEnumerable<string> ParseSignature(string signature)
    {
        for (var i = 0; i < signature.Length; i++)
        {
            if (signature[i] != 'a') { yield return signature[i].ToString(); continue; }
            if (signature.AsSpan(i).StartsWith("aa{sv}")) { yield return "aa{sv}"; i += 5; continue; }
            if (signature[i + 1] == 's') { yield return "as"; i++; continue; }
            if (signature.AsSpan(i).StartsWith("a(sa{sv})")) { yield return "a(sa{sv})"; i += 8; continue; }
            if (signature.AsSpan(i).StartsWith("a(sa{sv}a{sv})")) { yield return "a(sa{sv}a{sv})"; i += 13; continue; }
            if (signature.AsSpan(i).StartsWith("a{sv}")) { yield return "a{sv}"; i += 4; continue; }
            throw new InvalidDataException("Unsupported argument type.");
        }
    }
    private static void Reply(MethodContext context, uint code, Dictionary<string, VariantValue> results)
    {
        var bare = context.Request.MemberAsString == "SetWallpaperURI";
        using var writer = context.CreateReplyWriter(bare ? "u" : "ua{sv}"); writer.WriteUInt32(code);
        if (!bare) writer.WriteDictionary(results); context.Reply(writer.CreateMessage());
    }
    public void Dispose()
    {
        foreach (var request in _requests.Values) request.Cancel();
        _frontendWatch?.Dispose();
        _notifications?.Dispose();
        _capture?.Dispose(); _shortcuts?.Dispose(); _inputCapture?.Dispose();
        _bus.Dispose();
    }
}
