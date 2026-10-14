using System.Collections.Concurrent;
using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Services.Tray;

public sealed record TrayItem(string Service, string Path, string Title, string Status, string IconName,
    string IconThemePath, VariantValue IconPixmap, string? MenuPath, bool ItemIsMenu, string? Id = null);
public sealed record TrayMenuEntry(int Id, string Label, bool Enabled, bool Separator, bool Visible, IReadOnlyList<TrayMenuEntry> Children);

/// <summary>Owns the desktop's StatusNotifier watcher and reads real application tray entries.</summary>
public sealed class StatusNotifierService : IAsyncDisposable
{
    private readonly string? _busAddress;
    public StatusNotifierService(string? busAddress = null) => _busAddress = busAddress;

    private const string Watcher = "org.kde.StatusNotifierWatcher";
    private const string WatcherPath = "/StatusNotifierWatcher";
    private const string ItemInterface = "org.kde.StatusNotifierItem";
    private readonly ConcurrentDictionary<string, (string Service, string Path)> _registered = new();
    private readonly CancellationTokenSource _stop = new();
    private DBusConnection? _connection;
    private Task? _poll;
    private bool _ownsWatcher;
    private IReadOnlyList<TrayItem> _items = [];
    public IReadOnlyList<TrayItem> Items => Volatile.Read(ref _items);
    public event EventHandler? ItemsChanged;
    public bool IsAvailable { get; private set; }

    public async Task StartAsync()
    {
        if (!OperatingSystem.IsLinux() || _connection is not null) return;
        try
        {
            _connection = new DBusConnection(_busAddress ?? DBusAddress.Session ?? throw new IOException("No session bus."));
            await _connection.ConnectAsync();
            _connection.AddMethodHandler(new WatcherHandler(this));
            _ownsWatcher = await _connection.TryRequestNameAsync(Watcher);
            var host = $"org.kde.StatusNotifierHost.DaisyOS{Environment.ProcessId}_{Guid.NewGuid():N}";
            await _connection.RequestNameAsync(host);
            if (_ownsWatcher)
            {
                Emit("StatusNotifierHostRegistered");
                // Existing items usually re-register when the watcher appears. Also
                // reconcile well-known item names after a shell-only restart.
                var names = await ReadNamesAsync("ListNames", null);
                foreach (var name in names.Where(name => name.StartsWith("org.kde.StatusNotifierItem", StringComparison.Ordinal)
                    || name.StartsWith("org.freedesktop.StatusNotifierItem", StringComparison.Ordinal))) Register(name, name);
            }
            else await CallAsync(Watcher, WatcherPath, Watcher, "RegisterStatusNotifierHost", host);
            IsAvailable = true;
            _poll = PollAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[tray] Could not start application tray: {ex.Message}");
            _connection?.Dispose();
            _connection = null;
        }
    }

    private void Register(string service, string sender)
    {
        var path = service.StartsWith('/') ? service : "/StatusNotifierItem";
        var name = service.StartsWith('/') ? sender : service;
        if (string.IsNullOrWhiteSpace(name) || !path.StartsWith('/')) return;
        var key = name + path;
        if (_registered.TryAdd(key, (name, path))) Emit("StatusNotifierItemRegistered", key);
    }

    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            do { await RefreshAsync(); }
            while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Console.Error.WriteLine($"[tray] Tray disconnected: {ex.Message}"); }
    }

    internal async Task RefreshAsync()
    {
        if (_connection is null) return;
        if (!_ownsWatcher)
        {
            try
            {
                var registrations = await GetPropertyAsync(Watcher, WatcherPath, Watcher, "RegisteredStatusNotifierItems");
                var current = new HashSet<string>();
                for (var i = 0; i < registrations.Count; i++)
                {
                    var key = registrations.GetItem(i).GetString();
                    var slash = key.IndexOf('/');
                    if (slash <= 0) continue;
                    current.Add(key);
                    _registered[key] = (key[..slash], key[slash..]);
                }
                foreach (var key in _registered.Keys.Where(key => !current.Contains(key))) _registered.TryRemove(key, out _);
            }
            catch (Exception ex) { Console.Error.WriteLine($"[tray] Could not read tray watcher: {ex.Message}"); }
        }
        var items = new List<TrayItem>();
        foreach (var (key, endpoint) in _registered.ToArray())
        {
            try
            {
                var properties = await GetPropertiesAsync(endpoint.Service, endpoint.Path, ItemInterface);
                var status = String(properties, "Status");
                if (status == "Passive") continue;
                items.Add(new TrayItem(endpoint.Service, endpoint.Path,
                    First(String(properties, "Title"), String(properties, "Id"), endpoint.Service), status,
                    status == "NeedsAttention" ? First(String(properties, "AttentionIconName"), String(properties, "IconName")) : String(properties, "IconName"),
                    String(properties, "IconThemePath"),
                    properties.GetValueOrDefault(status == "NeedsAttention" && properties.ContainsKey("AttentionIconPixmap") ? "AttentionIconPixmap" : "IconPixmap"),
                    properties.TryGetValue("Menu", out var menu) && menu.Type == VariantValueType.ObjectPath ? menu.GetObjectPath().ToString() : null,
                    properties.TryGetValue("ItemIsMenu", out var isMenu) && isMenu.Type == VariantValueType.Bool && isMenu.GetBool(), String(properties, "Id")));
            }
            catch
            {
                try
                {
                    if (!(await HasOwnerAsync(endpoint.Service)))
                    {
                        if (_registered.TryRemove(key, out _)) Emit("StatusNotifierItemUnregistered", key);
                    }
                    else if (Items.FirstOrDefault(item => item.Service == endpoint.Service && item.Path == endpoint.Path) is { } retained) items.Add(retained);
                }
                catch { }
            }
        }
        Volatile.Write(ref _items, items.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase).ToArray());
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task ActivateAsync(TrayItem item, int x, int y) => CallCoordinatesAsync(item, "Activate", x, y);
    public Task ContextMenuAsync(TrayItem item, int x, int y) => CallCoordinatesAsync(item, "ContextMenu", x, y);
    private async Task CallCoordinatesAsync(TrayItem item, string member, int x, int y)
    {
        if (_connection is null) return;
        MessageBuffer message;
        using (var writer = _connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: item.Service, path: item.Path, @interface: ItemInterface, member: member, signature: "ii");
            writer.WriteInt32(x); writer.WriteInt32(y); message = writer.CreateMessage();
        }
        await _connection.CallMethodAsync(message).WaitAsync(TimeSpan.FromSeconds(3));
    }

    public async Task<IReadOnlyList<TrayMenuEntry>> GetMenuAsync(TrayItem item)
    {
        if (_connection is null || item.MenuPath is null || item.MenuPath == "/") return [];
        // Some apps populate their menu only when the host announces it is opening.
        MessageBuffer opening;
        using (var writer = _connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: item.Service, path: item.MenuPath,
                @interface: "com.canonical.dbusmenu", member: "AboutToShow", signature: "i");
            writer.WriteInt32(0); opening = writer.CreateMessage();
        }
        try { await _connection.CallMethodAsync(opening).WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (DBusErrorReplyException) { } // Optional on older dbusmenu implementations.
        MessageBuffer message;
        using (var writer = _connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: item.Service, path: item.MenuPath, @interface: "com.canonical.dbusmenu", member: "GetLayout", signature: "iias");
            writer.WriteInt32(0); writer.WriteInt32(-1); writer.WriteArray(Array.Empty<string>()); message = writer.CreateMessage();
        }
        return await _connection.CallMethodAsync(message, (reply, _) =>
        {
            var reader = reply.GetBodyReader(); reader.ReadUInt32();
            return ReadLayout(ref reader).Children;
        }).WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static TrayMenuEntry ReadLayout(ref Reader reader)
    {
        reader.AlignStruct();
        var id = reader.ReadInt32();
        var props = reader.ReadDictionaryOfStringToVariantValue();
        var children = reader.ReadArrayOfVariantValue();
        return new TrayMenuEntry(id, String(props, "label").Replace("_", ""),
            !props.TryGetValue("enabled", out var enabled) || enabled.GetBool(), String(props, "type") == "separator",
            !props.TryGetValue("visible", out var visible) || visible.GetBool(), children.Select(ParseLayout).ToArray());
    }

    private static TrayMenuEntry ParseLayout(VariantValue layout)
    {
        var props = layout.GetItem(1);
        var map = new Dictionary<string, VariantValue>();
        for (var i = 0; i < props.Count; i++) { var entry = props.GetDictionaryEntry(i); map[entry.Key.GetString()] = entry.Value; }
        var children = layout.GetItem(2);
        return new TrayMenuEntry(layout.GetItem(0).GetInt32(), String(map, "label").Replace("_", ""),
            !map.TryGetValue("enabled", out var enabled) || enabled.GetBool(), String(map, "type") == "separator",
            !map.TryGetValue("visible", out var visible) || visible.GetBool(), Enumerable.Range(0, children.Count).Select(i => ParseLayout(children.GetItem(i))).ToArray());
    }

    public async Task InvokeMenuAsync(TrayItem item, int id)
    {
        if (_connection is null || item.MenuPath is null) return;
        MessageBuffer message;
        using (var writer = _connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: item.Service, path: item.MenuPath, @interface: "com.canonical.dbusmenu", member: "Event", signature: "isvu");
            writer.WriteInt32(id); writer.WriteString("clicked"); writer.WriteVariantInt32(0);
            writer.WriteUInt32(unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())); message = writer.CreateMessage();
        }
        await _connection.CallMethodAsync(message).WaitAsync(TimeSpan.FromSeconds(3));
    }

    private async Task<Dictionary<string, VariantValue>> GetPropertiesAsync(string service, string path, string iface)
    {
        var message = BuildCall(service, path, "org.freedesktop.DBus.Properties", "GetAll", iface);
        return await _connection!.CallMethodAsync(message, (reply, _) => reply.GetBodyReader().ReadDictionaryOfStringToVariantValue()).WaitAsync(TimeSpan.FromSeconds(3));
    }
    private async Task<VariantValue> GetPropertyAsync(string service, string path, string iface, string property)
    {
        MessageBuffer message;
        using (var writer = _connection!.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: service, path: path, @interface: "org.freedesktop.DBus.Properties", member: "Get", signature: "ss");
            writer.WriteString(iface); writer.WriteString(property); message = writer.CreateMessage();
        }
        return await _connection.CallMethodAsync(message, (reply, _) => reply.GetBodyReader().ReadVariantValue()).WaitAsync(TimeSpan.FromSeconds(3));
    }
    private async Task<bool> HasOwnerAsync(string name) => await _connection!.CallMethodAsync(
        BuildCall("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", name),
        (reply, _) => reply.GetBodyReader().ReadBool()).WaitAsync(TimeSpan.FromSeconds(3));
    private async Task<string[]> ReadNamesAsync(string method, string? argument) => await _connection!.CallMethodAsync(
        BuildCall("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", method, argument),
        (reply, _) => reply.GetBodyReader().ReadArrayOfString()).WaitAsync(TimeSpan.FromSeconds(3));
    private Task CallAsync(string service, string path, string iface, string member, string argument) =>
        _connection!.CallMethodAsync(BuildCall(service, path, iface, member, argument));
    private MessageBuffer BuildCall(string service, string path, string iface, string member, string? argument)
    {
        using var writer = _connection!.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: service, path: path, @interface: iface, member: member, signature: argument is null ? "" : "s");
        if (argument is not null) writer.WriteString(argument);
        return writer.CreateMessage();
    }
    private void Emit(string member, string? item = null)
    {
        if (!_ownsWatcher || _connection is null) return;
        using var writer = _connection.GetMessageWriter();
        writer.WriteSignalHeader(path: WatcherPath, @interface: Watcher, member: member, signature: item is null ? "" : "s");
        if (item is not null) writer.WriteString(item);
        _connection.TrySendMessage(writer.CreateMessage());
    }
    private static string String(IReadOnlyDictionary<string, VariantValue> properties, string name) =>
        properties.TryGetValue(name, out var value) && value.Type == VariantValueType.String ? value.GetString() : "";
    private static string First(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_poll is not null) await _poll;
        _connection?.Dispose(); _connection = null;
        _stop.Dispose(); IsAvailable = false;
    }

    private sealed class WatcherHandler(StatusNotifierService owner) : IPathMethodHandler
    {
        public string Path => WatcherPath;
        public bool HandlesChildPaths => false;
        private static readonly ReadOnlyMemory<byte> Xml = """
            <interface name="org.kde.StatusNotifierWatcher">
              <method name="RegisterStatusNotifierItem"><arg type="s" direction="in"/></method>
              <method name="RegisterStatusNotifierHost"><arg type="s" direction="in"/></method>
              <property name="RegisteredStatusNotifierItems" type="as" access="read"/>
              <property name="IsStatusNotifierHostRegistered" type="b" access="read"/>
              <property name="ProtocolVersion" type="i" access="read"/>
              <signal name="StatusNotifierItemRegistered"><arg type="s"/></signal>
              <signal name="StatusNotifierItemUnregistered"><arg type="s"/></signal>
              <signal name="StatusNotifierHostRegistered"/>
              <signal name="StatusNotifierHostUnregistered"/>
            </interface>
            """u8.ToArray();
        private static readonly ReadOnlyMemory<byte> PropertiesXml = """
            <interface name="org.freedesktop.DBus.Properties">
              <method name="Get"><arg type="s" direction="in"/><arg type="s" direction="in"/><arg type="v" direction="out"/></method>
              <method name="GetAll"><arg type="s" direction="in"/><arg type="a{sv}" direction="out"/></method>
            </interface>
            """u8.ToArray();

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.IsDBusIntrospectRequest) { context.ReplyIntrospectXml([Xml, PropertiesXml]); return default; }
            var request = context.Request;
            if (request.InterfaceAsString == Watcher)
            {
                if (request.MemberAsString == "RegisterStatusNotifierItem")
                    owner.Register(request.GetBodyReader().ReadString(), request.SenderAsString!);
                else if (request.MemberAsString != "RegisterStatusNotifierHost") return default;
                using var writer = context.CreateReplyWriter(""); context.Reply(writer.CreateMessage());
                return default;
            }
            if (request.InterfaceAsString == "org.freedesktop.DBus.Properties")
            {
                var reader = request.GetBodyReader();
                if (reader.ReadString() != Watcher) return default;
                if (request.MemberAsString == "Get")
                {
                    var name = reader.ReadString();
                    using var writer = context.CreateReplyWriter("v");
                    writer.WriteVariant(Property(name)); context.Reply(writer.CreateMessage());
                }
                else if (request.MemberAsString == "GetAll")
                {
                    using var writer = context.CreateReplyWriter("a{sv}");
                    var start = writer.WriteDictionaryStart();
                    foreach (var name in new[] { "RegisteredStatusNotifierItems", "IsStatusNotifierHostRegistered", "ProtocolVersion" })
                    { writer.WriteDictionaryEntryStart(); writer.WriteString(name); writer.WriteVariant(Property(name)); }
                    writer.WriteDictionaryEnd(start); context.Reply(writer.CreateMessage());
                }
            }
            return default;
        }
        private VariantValue Property(string name) => name switch
        {
            "RegisteredStatusNotifierItems" => VariantValue.Array(owner._registered.Keys.ToArray()),
            "IsStatusNotifierHostRegistered" => true,
            "ProtocolVersion" => 0,
            _ => throw new ArgumentException("Unknown tray watcher property.", nameof(name))
        };
    }
}
