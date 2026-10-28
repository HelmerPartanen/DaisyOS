using global::System.Net;
using global::System.Net.NetworkInformation;
using global::System.Net.Sockets;
using Tmds.DBus.Protocol;

namespace DaisyOS.Continuity;

// Avahi owns multicast. Publishing uses its system D-Bus EntryGroup API, scoped
// to private-address interfaces; browsing uses the Avahi D-Bus CLI adapter.
public sealed class AvahiDiscovery : IAsyncDisposable
{
    private readonly DBusConnection _bus = new(DBusAddress.System ?? "unix:path=/run/dbus/system_bus_socket");
    private readonly List<string> _groups = [];
    private readonly Dictionary<string, (string Id, string Name, string Address)> _resolved = new();
    private readonly SemaphoreSlim _publish = new(1, 1);
    private global::System.Diagnostics.Process? _browser;
    private IDisposable? _ownerWatch;
    private sealed record Advertisement(string Id, string Name, int Port, bool Visible);
    private Advertisement _advertisement = new("", "", 0, false);
    private readonly Action<string, string, string, bool> _found;
    public string Diagnostic { get; private set; } = "Discovery not started.";
    public AvahiDiscovery(Action<string, string, string, bool> found) => _found = found;
    public static bool IsLan(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        var b = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork
            ? b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 169 && b[1] == 254
            : address.IsIPv6LinkLocal || (b[0] & 0xfe) == 0xfc;
    }
    public async Task StartAsync(CancellationToken token)
    {
        await _bus.ConnectAsync();
        _ownerWatch = await _bus.AddMatchAsync<string>(new MatchRule { Type = MessageType.Signal,
            Sender = "org.freedesktop.DBus", Interface = "org.freedesktop.DBus", Member = "NameOwnerChanged", Arg0 = "org.freedesktop.Avahi" },
            (message, _) => { var reader = message.GetBodyReader(); reader.ReadString(); reader.ReadString(); return reader.ReadString(); },
            update =>
            {
                if (!update.HasValue) return;
                Diagnostic = update.Value.Length == 0 ? "Avahi is unavailable; manual connection remains available." : "Discovery active.";
                if (update.Value.Length > 0) _ = RepublishAsync();
            }, emitOnCapturedContext: false);
        var start = new global::System.Diagnostics.ProcessStartInfo("avahi-browse") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-prkf", "_daisycont._tcp" }) start.ArgumentList.Add(arg);
        _browser = global::System.Diagnostics.Process.Start(start) ?? throw new IOException("Avahi unavailable.");
        _ = _browser.StandardError.ReadToEndAsync(token);
        Diagnostic = "Discovery active.";
        _ = ReadAsync(_browser, token);
    }
    private async Task ReadAsync(global::System.Diagnostics.Process browser, CancellationToken token)
    {
        try
        {
            while (await browser.StandardOutput.ReadLineAsync(token) is { } line)
            {
                if (line.Length > 8192) continue;
                var parts = line.Split(';');
                if (parts.Length < 6 || parts[0] is not ("=" or "-")) continue;
                var key = parts[1] + ":" + parts[2] + ":" + parts[3];
                if (parts[0] == "-")
                {
                    if (_resolved.Remove(key, out var removed)) Report(removed.Id, removed.Name);
                    continue;
                }
                if (parts.Length < 10 || !IPAddress.TryParse(parts[7], out var ip) || !IsLan(ip)) continue;
                var txt = parts[9];
                var id = global::System.Text.RegularExpressions.Regex.Match(txt, "(?:^|[\\\" ])id=([a-f0-9]{64})(?:[\\\" ]|$)").Groups[1].Value;
                if (id.Length != 64 || !global::System.Text.RegularExpressions.Regex.IsMatch(txt, "(?:^|[\\\" ])pv=1(?:[\\\" ]|$)") || !int.TryParse(parts[8], out var port) || port is < 1 or > 65535) continue;
                if (ip.IsIPv6LinkLocal)
                {
                    var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name == parts[1]);
                    if (nic is null) continue;
                    ip.ScopeId = nic.GetIPProperties().GetIPv6Properties().Index;
                }
                var address = ip.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{ip}]:{port}" : $"{ip}:{port}";
                if (_resolved.Count >= 256 && !_resolved.ContainsKey(key)) continue;
                _resolved[key] = (id, DecodeName(parts[3]), address);
                Report(id, DecodeName(parts[3]));
            }
            Diagnostic = "Avahi stopped. Connect by address is still available.";
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
    private static string DecodeName(string text)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 3 < text.Length && byte.TryParse(text.AsSpan(i + 1, 3), out var value)) { bytes.Add(value); i += 3; }
            else bytes.AddRange(global::System.Text.Encoding.UTF8.GetBytes(text[i].ToString()));
        }
        return SecureState.SafeName(global::System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
    }
    private void Report(string id, string name)
    {
        var addresses = _resolved.Values.Where(p => p.Id == id).Select(p => p.Address).OrderBy(a => a.StartsWith('[')).ThenBy(a => a, StringComparer.Ordinal).ToArray();
        _found(id, name, addresses.FirstOrDefault() ?? "", addresses.Length > 0);
    }
    public Task PublishAsync(string id, string name, int port, bool visible)
    {
        _advertisement = new(id, name, port, visible);
        return PublishCurrentAsync();
    }
    private async Task RepublishAsync()
    {
        try { await PublishCurrentAsync(); }
        catch { Diagnostic = "Could not advertise on Avahi; manual connection remains available."; }
    }
    private async Task PublishCurrentAsync()
    {
        await _publish.WaitAsync();
        try
        {
            var current = _advertisement;
            await PublishCoreAsync(current.Id, current.Name, current.Port, current.Visible);
        }
        finally { _publish.Release(); }
    }
    private async Task PublishCoreAsync(string id, string name, int port, bool visible)
    {
        foreach (var group in _groups.ToArray())
        {
            try { await CallAsync(group, "org.freedesktop.Avahi.EntryGroup", "Free", "", (ref MessageWriter writer) => { }); } catch { }
        }
        _groups.Clear();
        if (!visible) return;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.SupportsMulticast && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            var properties = nic.GetIPProperties();
            if (!properties.UnicastAddresses.Any(a => IsLan(a.Address) && !IPAddress.IsLoopback(a.Address))) continue;
            var index = properties.GetIPv4Properties()?.Index ?? properties.GetIPv6Properties().Index;
            var group = await _bus.CallMethodAsync(CreateCall("/", "org.freedesktop.Avahi.Server", "EntryGroupNew", "", (ref MessageWriter writer) => { }),
                (message, _) => message.GetBodyReader().ReadObjectPath().ToString());
            _groups.Add(group);
            await CallAsync(group, "org.freedesktop.Avahi.EntryGroup", "AddService", "iiussssqaay", (ref MessageWriter writer) =>
            {
                writer.WriteInt32(index); writer.WriteInt32(-1); writer.WriteUInt32(0);
                writer.WriteString(name); writer.WriteString("_daisycont._tcp"); writer.WriteString(""); writer.WriteString(""); writer.WriteUInt16((ushort)port);
                // aay encoded explicitly to avoid reflection-based D-Bus serialization.
                var array = writer.WriteArrayStart(DBusType.Array);
                foreach (var txt in new[] { "pv=1", "id=" + id, "cap=clipboard,files" }) writer.WriteArray(global::System.Text.Encoding.UTF8.GetBytes(txt));
                writer.WriteArrayEnd(array);
            });
            await CallAsync(group, "org.freedesktop.Avahi.EntryGroup", "Commit", "", (ref MessageWriter writer) => { });
        }
    }
    private delegate void WriteValue(ref MessageWriter writer);
    private MessageBuffer CreateCall(string path, string iface, string method, string signature, WriteValue write)
    {
        var writer = _bus.GetMessageWriter();
        try
        {
        writer.WriteMethodCallHeader(destination: "org.freedesktop.Avahi", path: path, @interface: iface, member: method, signature: signature);
        write(ref writer); return writer.CreateMessage();
        } finally { writer.Dispose(); }
    }
    private Task CallAsync(string path, string iface, string method, string signature, WriteValue write) =>
        _bus.CallMethodAsync(CreateCall(path, iface, method, signature, write));
    public async ValueTask DisposeAsync()
    {
        _ownerWatch?.Dispose();
        try { await PublishAsync("", "", 0, false); } catch { }
        if (_browser is { HasExited: false }) _browser.Kill(true);
        _browser?.Dispose(); _bus.Dispose();
    }
}
