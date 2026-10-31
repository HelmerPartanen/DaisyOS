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
    private readonly Dictionary<string, (string Id, string Name, string Address)> _resolved = new();
    private readonly SemaphoreSlim _publish = new(1, 1);
    private global::System.Diagnostics.Process? _browser;
    private global::System.Diagnostics.Process? _publisher;
    private UdpClient? _udp4;
    private IDisposable? _ownerWatch;
    private CancellationTokenSource? _beaconStop;
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
        // LAN discovery must work even when Avahi or its CLI is unavailable.
        StartUdpDiscovery(token);
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
    private void StartUdpDiscovery(CancellationToken token)
    {
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.EnableBroadcast = true;
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 47621));
            _udp4 = udp;
            _beaconStop = CancellationTokenSource.CreateLinkedTokenSource(token);
            _ = ReadUdpAsync(udp, _beaconStop.Token);
            _ = BroadcastLoopAsync(_beaconStop.Token);
        }
        catch { }
    }
    private async Task ReadUdpAsync(UdpClient udp, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var packet = await udp.ReceiveAsync(token);
                if (!IsLan(packet.RemoteEndPoint.Address) || packet.Buffer.Length > 512) continue;
                var text = global::System.Text.Encoding.UTF8.GetString(packet.Buffer);
                if (text == "DAISYCONT1|DISCOVER")
                {
                    var current = _advertisement;
                    if (current.Visible && current.Port > 0 && current.Id.Length == 64)
                        await SendBeaconToAsync(udp, new IPEndPoint(packet.RemoteEndPoint.Address, 47621), current);
                    continue;
                }
                var parts = text.Split('|', 4);
                if (parts.Length == 4 && parts[0] == "DAISYCONT1" && parts[1].Length == 64 && parts[1].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') &&
                    int.TryParse(parts[2], out var port) && port is >= 1 and <= 65535)
                {
                    if (parts[1] == _advertisement.Id) continue;
                    var cleanName = SecureState.SafeName(parts[3]);
                    var address = $"{packet.RemoteEndPoint.Address}:{port}";
                    lock (_resolved)
                    {
                        _resolved["udp:" + parts[1]] = (parts[1], cleanName, address);
                        Report(parts[1], cleanName);
                    }
                }
            }
        }
        catch { }
    }
    private async Task BroadcastLoopAsync(CancellationToken token)
    {
        try
        {
            await BroadcastProbeAsync(sweepSubnet: true);
            var tick = 0;
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            while (await timer.WaitForNextTickAsync(token))
            {
                tick++;
                var current = _advertisement;
                if (current.Visible && current.Port > 0 && current.Id.Length == 64)
                    await BroadcastAdvertisementAsync(current, sweepSubnet: tick % 4 == 1);
            }
        }
        catch { }
    }
    private async Task BroadcastProbeAsync(bool sweepSubnet = false)
    {
        if (_udp4 is not { } udp) return;
        var payload = global::System.Text.Encoding.UTF8.GetBytes("DAISYCONT1|DISCOVER");
        foreach (var target in GetDiscoveryTargetAddresses(sweepSubnet))
        {
            try { await udp.SendAsync(payload, new IPEndPoint(target, 47621)); } catch { }
        }
    }
    private async Task BroadcastAdvertisementAsync(Advertisement current, bool sweepSubnet = false)
    {
        if (_udp4 is not { } udp || !current.Visible || current.Port <= 0 || current.Id.Length != 64) return;
        foreach (var target in GetDiscoveryTargetAddresses(sweepSubnet))
        {
            await SendBeaconToAsync(udp, new IPEndPoint(target, 47621), current);
        }
    }
    private static async Task SendBeaconToAsync(UdpClient udp, IPEndPoint endpoint, Advertisement current)
    {
        try
        {
            var displayName = current.Name.Length > 9 && current.Name[^9] == '-' && current.Name[^8..] == current.Id[..8]
                ? current.Name[..^9]
                : current.Name;
            var bytes = global::System.Text.Encoding.UTF8.GetBytes($"DAISYCONT1|{current.Id}|{current.Port}|{displayName}");
            await udp.SendAsync(bytes, endpoint);
        }
        catch { }
    }
    private static IEnumerable<IPAddress> GetDiscoveryTargetAddresses(bool sweepSubnet)
    {
        var list = new List<IPAddress> { IPAddress.Broadcast };
        var seen = new HashSet<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                foreach (var ua in nic.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && IsLan(a.Address) && !IPAddress.IsLoopback(a.Address)))
                {
                    var ip = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask.GetAddressBytes();
                    if (ip.Length == 4 && mask.Length == 4)
                    {
                        var bcast = new byte[4];
                        for (var i = 0; i < 4; i++) bcast[i] = (byte)(ip[i] | ~mask[i]);
                        var bcastIp = new IPAddress(bcast);
                        if (seen.Add(bcastIp)) list.Add(bcastIp);
                        if (sweepSubnet && mask[0] == 255 && mask[1] == 255 && mask[2] == 255)
                        {
                            for (var host = 1; host < 255; host++)
                            {
                                if (host == ip[3]) continue;
                                var candidate = new IPAddress(new byte[] { ip[0], ip[1], ip[2], (byte)host });
                                if (seen.Add(candidate)) list.Add(candidate);
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return list;
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
                    lock (_resolved)
                    {
                        if (_resolved.Remove(key, out var removed)) Report(removed.Id, removed.Name);
                    }
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
                var decodedName = DecodeName(parts[3]);
                if (decodedName.Length > 9 && decodedName[^9] == '-' && decodedName[^8..] == id[..8])
                    decodedName = decodedName[..^9];
                lock (_resolved)
                {
                    if (_resolved.Count >= 256 && !_resolved.ContainsKey(key)) continue;
                    _resolved[key] = (id, decodedName, address);
                    Report(id, decodedName);
                }
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
        if (visible && port > 0 && id.Length == 64)
        {
            _ = BroadcastAdvertisementAsync(_advertisement, sweepSubnet: true);
            _ = BroadcastProbeAsync(sweepSubnet: true);
        }
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
    private Task PublishCoreAsync(string id, string name, int port, bool visible)
    {
        if (_publisher is { } old)
        {
            try { if (!old.HasExited) old.Kill(true); } catch { }
            old.Dispose();
            _publisher = null;
        }
        if (!visible || port <= 0 || id.Length != 64) return Task.CompletedTask;
        var start = new global::System.Diagnostics.ProcessStartInfo("avahi-publish-service")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { "--no-fail", name, "_daisycont._tcp", port.ToString(), "pv=1", "id=" + id, "cap=clipboard,files,input,handoff,screen,audio" })
            start.ArgumentList.Add(arg);
        _publisher = global::System.Diagnostics.Process.Start(start);
        return Task.CompletedTask;
    }
    public async ValueTask DisposeAsync()
    {
        _ownerWatch?.Dispose();
        _beaconStop?.Cancel();
        _udp4?.Dispose();
        try { await PublishAsync("", "", 0, false); } catch { }
        if (_publisher is { HasExited: false }) try { _publisher.Kill(true); } catch { }
        _publisher?.Dispose();
        if (_browser is { HasExited: false }) _browser.Kill(true);
        _browser?.Dispose(); _bus.Dispose();
    }
}
