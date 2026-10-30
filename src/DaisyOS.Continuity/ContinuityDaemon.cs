using global::System.Collections.Concurrent;
using global::System.Net;
using global::System.Net.Sockets;
using global::System.Security.Authentication;
using global::System.Security.Cryptography;
using global::System.Text.Json;
using global::System.Text;
using DaisyOS.Core.Continuity;
using DaisyOS.Core.Serialization;
using DaisyOS.Continuity.Protocol.V1;
using DaisyOS.System.Continuity;
using Google.Protobuf;
using Tmds.DBus.Protocol;

namespace DaisyOS.Continuity;

public sealed partial class ContinuityDaemon : IAsyncDisposable, IPathMethodHandler
{
    private readonly SecureState _state;
    private readonly string? _mediaExecutableDirectory;
    private readonly int _port;
    private int _boundPort;
    private readonly object _gate = new();
    private readonly DBusConnection _bus;
    private readonly Func<bool> _sessionIsActive;
    private readonly string _downloadsDirectory;
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource _sharing = new();
    private readonly SemaphoreSlim _networkSlots = new(8, 8);
    private readonly SemaphoreSlim _outgoingSlots = new(2, 2);
    private readonly ConcurrentDictionary<string, Task> _tasks = new();
    private readonly Dictionary<string, ContinuityDevice> _nearby = new();
    private readonly Dictionary<string, (ContinuityPrompt Prompt, TaskCompletionSource<bool> Decision)> _prompts = new();
    private readonly Dictionary<string, ContinuityTransfer> _transfers = new();
    private readonly Dictionary<string, List<CancellationTokenSource>> _peerOperations = new();
    private readonly Dictionary<string, DateTimeOffset> _pairAttempts = new();
    private DateTimeOffset _lastPairAttempt;
    private readonly Dictionary<string, TaskCompletionSource<string>> _pairInputs = new();
    private readonly Dictionary<string, long> _trustEpoch = new();
    private readonly HashSet<string> _revoked = new();
    private readonly AvahiDiscovery _discovery;
    private readonly ClipboardBridge _clipboard;
    private TcpListener? _listener;
    private IDisposable? _ownerWatch;
    private IDisposable? _notificationWatch;
    private readonly HashSet<uint> _fileNotifications = new();
    private bool _disposing;
    private bool _sessionReady;
    private string _diagnostic = "";
    private string? _shellOwner;
    public string Path => ContinuityClient.ObjectPath;
    public bool HandlesChildPaths => false;
    private bool Ready => !_stop.IsCancellationRequested && _state.Settings.Enabled && _sessionIsActive() && !_state.Settings.Paused;

    public ContinuityDaemon(SecureState state, int port) : this(state, port, null, null, null) { }

    // Composition seam for isolated policy/transfer tests. Program always uses
    // the public constructor with the real logind + shell session authority.
    internal ContinuityDaemon(SecureState state, int port, Func<bool>? sessionIsActive,
        string? downloadsDirectory, string? sessionBusAddress, string? mediaExecutableDirectory = null)
    {
        _state = state; _mediaExecutableDirectory = mediaExecutableDirectory; _port = port; _boundPort = port;
        _sessionIsActive = sessionIsActive ?? (() => _sessionReady);
        _downloadsDirectory = downloadsDirectory ?? DaisyOS.Core.Storage.UserDirectoryPaths.Load()["Downloads"];
        _bus = new(sessionBusAddress ?? DBusAddress.Session ?? throw new IOException("No session bus."));
        _clipboard = new ClipboardBridge();
        _discovery = new AvahiDiscovery((id, name, address, found) =>
        {
            lock (_gate)
            {
                if (id == _state.Id) return;
                if (!found) { _nearby.Remove(id); return; }
                if (_nearby.Count >= 128 && !_nearby.ContainsKey(id)) return;
                var peer = _state.Settings.Peers.GetValueOrDefault(id);
                if (peer is not null) _diagnostic = "";
                _nearby[id] = new(id, SecureState.SafeName(name), address, peer is null ? "Discovered" : "Trusted · discovered", peer is not null, peer?.Clipboard ?? false, peer?.Files ?? false);
            }
        });
    }
    public async Task RunAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        _listener = new TcpListener(Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, _port);
        if (Socket.OSSupportsIPv6) _listener.Server.DualMode = true;
        _listener.Start(16);
        _boundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        try { await RecoverAudioRouteAsync(); } catch (IOException) { Console.Error.WriteLine("[continuity] audio_route_cleanup_pending"); }
        await _bus.ConnectAsync();
        _ = FailClosedOnDisconnectAsync(_bus);
        await StartSessionGuardAsync();
        _notificationWatch = await _bus.AddMatchAsync<(uint Id, string Action)>(new MatchRule {
            Type = MessageType.Signal, Sender = "org.freedesktop.Notifications", Interface = "org.freedesktop.Notifications", Member = "ActionInvoked" },
            (message, _) => { var reader = message.GetBodyReader(); return (reader.ReadUInt32(), reader.ReadString()); },
            result =>
            {
                if (!result.HasValue) return;
                lock (_gate)
                {
                    if (!Ready) return;
                    if (result.Value.Action != "open-folder" || !_fileNotifications.Remove(result.Value.Id)) return;
                }
                new DaisyOS.System.Processes.SafeProcessLauncher().Launch("xdg-open", [global::System.IO.Path.Combine(_downloadsDirectory, "Continuity")]);
            }, emitOnCapturedContext: false);
        _bus.AddMethodHandler(this);
        _ownerWatch = await _bus.AddMatchAsync<int>(new MatchRule { Type = MessageType.Signal,
            Sender = "org.freedesktop.DBus", Interface = "org.freedesktop.DBus", Member = "NameOwnerChanged", Arg0 = "org.daisyos.ContinuitySession1" },
            (message, _) => 0, notification =>
            {
                if (notification.HasValue) lock (_gate) { _sessionRevision++; _shellOwner = null; _sessionGranted = false; _sessionReady = false; StopSharing(); }
            }, emitOnCapturedContext: false);
        if (!await _bus.TryRequestNameAsync(ContinuityClient.BusName)) throw new IOException("Continuity already running.");
        try { await _discovery.StartAsync(linked.Token); }
        catch { _diagnostic = "Avahi is unavailable. Connect by address instead."; }
        _ = RefreshVisibilityAsync();
        Track(MaintainPeersAsync);
        Console.Error.WriteLine("[continuity] service_ready");
        try
        {
            while (!linked.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(linked.Token);
                if (client.Client.RemoteEndPoint is not IPEndPoint endpoint || !AvahiDiscovery.IsLan(endpoint.Address) || !_networkSlots.Wait(0))
                { client.Dispose(); continue; }
                Track(async () =>
                {
                    try { await ReceiveConnectionAsync(client, endpoint, linked.Token); }
                    finally { client.Dispose(); _networkSlots.Release(); }
                });
            }
        }
        finally { lock (_gate) { _sessionReady = false; StopSharing(); } _listener.Stop(); }
    }
    private void Track(Func<Task> action)
    {
        var id = Guid.NewGuid().ToString("N");
        var task = Task.Run(async () =>
        {
            try { await action(); }
            catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException or SocketException or InvalidProtocolBufferException or UnauthorizedAccessException or TimeoutException or DecoderFallbackException)
            {
                // Forget, pause, lock, and user cancellation are normal lifecycle events.
                if (ex is OperationCanceledException) return;
                var code = ex switch
                {
                    AuthenticationException => "peer_authentication_failed",
                    EndOfStreamException => "peer_closed_or_locked",
                    InvalidDataException or InvalidProtocolBufferException or DecoderFallbackException => "invalid_or_incompatible_protocol",
                    SocketException => "peer_unreachable",
                    TimeoutException => "operation_timeout",
                    OperationCanceledException => "operation_cancelled",
                    UnauthorizedAccessException => "permission_or_session_denied",
                    _ => "io_or_storage_failed"
                };
                lock (_gate) _diagnostic = code switch
                {
                    "peer_authentication_failed" => "Peer identity authentication failed. Verify identities on both devices before pairing again.",
                    "peer_closed_or_locked" => "Peer closed the connection. Ensure the other device is unlocked and Continuity is enabled.",
                    "invalid_or_incompatible_protocol" => "Peer protocol is invalid or incompatible. Check both devices run the same revision.",
                    "peer_unreachable" => "Device unreachable. Check LAN reachability, the TCP firewall, hostname and guest Wi-Fi isolation.",
                    "operation_timeout" => "Continuity timed out. Check the peer is awake and reachable.",
                    "permission_or_session_denied" => "The device permission or active session is unavailable.",
                    "io_or_storage_failed" => "Continuity IO failed. Check connection, free disk space and private state permissions.",
                    _ => "The operation was cancelled, declined, or sharing became unavailable."
                };
                Console.Error.WriteLine("[continuity] " + code);
            }
        });
        _tasks[id] = task;
        _ = task.ContinueWith(completed => _tasks.TryRemove(id, out _), TaskScheduler.Default);
    }
    private CancellationTokenSource CreateSharingLifetime(CancellationToken parent)
    {
        lock (_gate)
        {
            if (!Ready) throw new UnauthorizedAccessException();
            return CancellationTokenSource.CreateLinkedTokenSource(parent, _sharing.Token);
        }
    }
    private CancellationTokenSource PeerLifetime(string id, CancellationToken parent, bool pairing = false)
    {
        lock (_gate)
        {
            if (!Ready || (!pairing && _revoked.Contains(id))) throw new UnauthorizedAccessException();
            var source = CancellationTokenSource.CreateLinkedTokenSource(parent, _sharing.Token);
            if (!_peerOperations.TryGetValue(id, out var list)) _peerOperations[id] = list = [];
            list.Add(source);
            return source;
        }
    }
    private void EndPeer(string id, CancellationTokenSource source)
    {
        lock (_gate) if (_peerOperations.TryGetValue(id, out var list)) { list.Remove(source); if (list.Count == 0) _peerOperations.Remove(id); }
        source.Dispose();
    }
    private bool CandidateAllowed(string id, bool pairing)
    {
        lock (_gate)
        {
            if (!Ready) return false;
            if (!pairing) return true; // TLS may report that a previously paired device has forgotten us.
            if (!_state.Settings.Visible || DateTimeOffset.UtcNow - _lastPairAttempt < TimeSpan.FromSeconds(10)) return false;
            _lastPairAttempt = DateTimeOffset.UtcNow;
            if (_pairAttempts.TryGetValue(id, out var last) && DateTimeOffset.UtcNow - last < TimeSpan.FromSeconds(10)) return false;
            foreach (var expired in _pairAttempts.Where(p => DateTimeOffset.UtcNow - p.Value > TimeSpan.FromMinutes(5)).Select(p => p.Key).ToArray()) _pairAttempts.Remove(expired);
            if (_pairAttempts.Count >= 256) return false;
            _pairAttempts[id] = DateTimeOffset.UtcNow;
            foreach (var expired in _pairAttempts.Where(p => DateTimeOffset.UtcNow - p.Value > TimeSpan.FromMinutes(5)).Select(p => p.Key).ToArray()) _pairAttempts.Remove(expired);
            return true;
        }
    }
    private async Task ReceiveConnectionAsync(TcpClient client, IPEndPoint endpoint, CancellationToken token)
    {
        lock (_gate)
        {
            if (!Ready) return;

        }
        client.NoDelay = true;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        await using var wire = await PeerWire.OpenAsync(client, _state, true, false, null, CandidateAllowed, deadline.Token, _boundPort);
        if (!wire.Remote.Pairing && !CheckMutualTrust(wire)) return;
        using var lifetime = PeerLifetime(wire.PeerId, deadline.Token, wire.Remote.Pairing);
        try
        {
            if (wire.Remote.Pairing) { await PairAsync(wire, (endpoint.Address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{endpoint.Address}]:{wire.Remote.Port}" : $"{endpoint.Address}:{wire.Remote.Port}"), lifetime.Token); return; }
            Require(wire.PeerId, null);
            var envelope = await wire.ReceiveAsync(lifetime.Token).WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
            if (envelope.Ping is { } ping)
            {
                await wire.SendAsync(new Envelope { Pong = new Pong { Sequence = ping.Sequence } }, lifetime.Token);
                if (ping.Sequence == ulong.MaxValue) ForgetPeer(wire.PeerId);
            }
            else if (envelope.ClipboardOffer is { } offer) await ReceiveClipboardAsync(wire, offer, lifetime.Token);
            else if (envelope.FileOffer is { } file) await ReceiveFileAsync(wire, file, lifetime.Token);
            else if (envelope.LiveStart is { } live) await ReceiveLiveAsync(wire, live, lifetime.Token);
            else if (envelope.ActivityOffer is { } activity) await ReceiveActivityAsync(wire, activity, lifetime.Token);
            else throw new InvalidDataException("Unsupported operation.");
        }
        finally { EndPeer(wire.PeerId, lifetime); }
    }
    private async Task<PeerWire> DialAsync(string address, string? expected, bool pairing, CancellationToken token)
    {
        if (!address.StartsWith('[') && IPAddress.TryParse(address, out var literal)) address = literal.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{literal}]:47621" : $"{literal}:47621";
        if (!Uri.TryCreate("tcp://" + address, UriKind.Absolute, out var uri) || uri.Port is 0 or > 65535 || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/")
            throw new InvalidDataException("Use hostname:port or [IPv6]:port.");
        var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, token);
        var target = addresses.FirstOrDefault(AvahiDiscovery.IsLan) ?? throw new IOException("A reachable local-network address is required.");
        if (target.IsIPv4MappedToIPv6) target = target.MapToIPv4();
        var client = new TcpClient(target.AddressFamily) { NoDelay = true };
        try
        {
            await client.ConnectAsync(target, uri.Port < 0 ? 47621 : uri.Port, token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token);
            var wire = await PeerWire.OpenAsync(client, _state, false, pairing, expected,
                (id, _) => { lock (_gate) return Ready && (pairing || !_revoked.Contains(id) && _state.Settings.Peers.ContainsKey(id)); }, token, _boundPort);
            if (!pairing && !CheckMutualTrust(wire)) { await wire.DisposeAsync(); throw new OperationCanceledException("Device was forgotten."); }
            return wire;
        }
        catch { client.Dispose(); throw; }
    }
    private void Require(string id, string? feature)
    {
        lock (_gate)
        {
            if (_revoked.Contains(id)) throw new OperationCanceledException("Device was forgotten.");
            if (!Ready || !_state.Settings.Peers.TryGetValue(id, out var peer) ||
                feature == "clipboard" && !peer.Clipboard || feature == "files" && !peer.Files ||
                feature == "input" && !peer.Input || feature == "handoff" && !peer.Handoff ||
                feature == "screen" && !peer.Screen || feature == "audio" && !peer.Audio)
                throw new UnauthorizedAccessException();
        }
    }
    internal async Task PairAsync(PeerWire wire, string address, CancellationToken token)
    {
        lock (_gate)
        {
            var trusted = _state.Settings.Peers.GetValueOrDefault(wire.PeerId);
            if (_nearby.Count < 128 || _nearby.ContainsKey(wire.PeerId))
                _nearby[wire.PeerId] = new(wire.PeerId, SecureState.SafeName(wire.Remote.Name), address,
                    "Pairing", trusted is not null, trusted?.Clipboard ?? false, trusted?.Files ?? false);
        }
        try { await PairCoreAsync(wire, address, token); }
        finally
        {
            lock (_gate) if (_nearby.TryGetValue(wire.PeerId, out var device))
                _nearby[wire.PeerId] = device with { Status = _state.Settings.Peers.ContainsKey(wire.PeerId) ? "Trusted · discovered" : "Discovered" };
        }
    }
    private async Task PairCoreAsync(PeerWire wire, string address, CancellationToken token)
    {
        long epoch;
        lock (_gate) epoch = _trustEpoch.GetValueOrDefault(wire.PeerId);
        // SRP proves knowledge of the receiver's code without transmitting it.
        // Both TLS identities are part of the SRP identity, preventing relay to
        // a different certificate. One proof attempt is allowed per pairing.
        var identity = Encoding.UTF8.GetBytes(wire.IsServer ? wire.PeerId + _state.Id : _state.Id + wire.PeerId);
        var code = wire.IsServer ? RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", global::System.Globalization.CultureInfo.InvariantCulture) : "";
        var input = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = Guid.NewGuid().ToString("N");
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (!Ready || _prompts.Count >= 16 || _prompts.Values.Any(p => p.Prompt.Kind.StartsWith("pair-"))) return;
            _prompts.Add(request, (new(request, wire.IsServer ? "pair-display" : "pair-code", wire.PeerId,
                SecureState.SafeName(wire.Remote.Name), wire.IsServer ? code : "Enter the six-digit code shown on the other computer."), decision));
            if (!wire.IsServer) _pairInputs[request] = input;
        }
        var notification = wire.IsServer ? await NotifyAsync("Continuity pairing code", $"{wire.Remote.Name}: {code} — enter this code on the pairing computer.") : null;
        bool approved;
        try
        {
            approved = wire.IsServer
                ? await PairAuthentication.VerifyAsync(wire, identity, code, token)
                : await PairAuthentication.ProveAsync(wire, identity, input.Task, token);
        }
        finally
        {
            lock (_gate) { _prompts.Remove(request); _pairInputs.Remove(request); }
            if (notification is { } notificationId) await CloseNotificationAsync(notificationId);
        }
        if (!approved) { lock (_gate) _diagnostic = "Pairing code did not match or pairing was cancelled. Try pairing again."; return; }
        lock (_gate)
        {
            if (!Ready || token.IsCancellationRequested || _trustEpoch.GetValueOrDefault(wire.PeerId) != epoch || (_state.Settings.Peers.Count >= 64 && !_state.Settings.Peers.ContainsKey(wire.PeerId))) return;
            _revoked.Remove(wire.PeerId);
            _clipboard.ClearPeer(wire.PeerId);
            _diagnostic = "";
            _state.Settings.Peers[wire.PeerId] = new(Convert.ToBase64String(wire.Remote.Certificate.ToByteArray()),
                SecureState.SafeName(wire.Remote.Name), address, Clipboard: true, Files: true, GrantId: Guid.NewGuid().ToString("N"), Input: true, Handoff: true, Screen: true, Audio: true);
            UpdateClipboardState();
            SaveSettings();
        }
        await NotifyAsync("Device paired", SecureState.SafeName(wire.Remote.Name));
    }
    internal ContinuityStatus Snapshot()
    {
        lock (_gate)
        {
            var devices = _state.Settings.Peers.Select(pair =>
            {
                var nearby = _nearby.GetValueOrDefault(pair.Key);
                return new ContinuityDevice(pair.Key, pair.Value.Name, nearby?.Address ?? pair.Value.Address,
                    nearby?.Status ?? "Trusted · unreachable", true, pair.Value.Clipboard, pair.Value.Files, pair.Value.Input, pair.Value.Handoff, pair.Value.Screen, pair.Value.Audio, pair.Value.InputEdge);
            }).Concat(_nearby.Values.Where(d => !_state.Settings.Peers.ContainsKey(d.Id))).ToArray();
            var summary = !_state.Settings.Enabled ? "Continuity is off." : _state.Settings.Paused ? "Continuity is paused." :
                !_sessionIsActive() ? "Sharing is unavailable outside an active unlocked DaisyOS session." : "Continuity is enabled for trusted devices.";
            if (_diagnostic.Length > 0) summary += " " + _diagnostic;
            return new(_state.Settings.Enabled, _state.Settings.Visible, _state.Settings.Paused, _sessionIsActive(), _state.Settings.Name,
                _state.Id, _clipboard.Status, summary, devices, _prompts.Values.Select(p => p.Prompt).ToArray(), _transfers.Values.ToArray(), _live.Values.Select(l => l.Info).Concat(_recentLive.Where(l => l.Expires > DateTimeOffset.UtcNow).Select(l => l.Info)).ToArray());
        }
    }
    private void SaveSettings()
    {
        try { _state.Save(); }
        catch { _sessionReady = false; StopSharing(); throw; }
    }
    private void StopSharing()
    {
        // Call with _gate held. Cancellation callbacks never mutate this lock;
        // async cancellation lets dispatch observe Ready=false immediately.
        // Mark the exact operation tokens first: linked-source callbacks from
        // CancelAsync may run later. No source can be recreated after cleanup.
        foreach (var operation in _peerOperations.Values.SelectMany(list => list)) _ = operation.CancelAsync();
        foreach (var transfer in _transferCancellation.Values) _ = transfer.CancelAsync();
        var old = _sharing; _sharing = new();
        _ = old.CancelAsync().ContinueWith(_ => old.Dispose(), TaskScheduler.Default);
        foreach (var pending in _prompts.Values) pending.Decision.TrySetResult(false);
        _clipboard.Stop();
        _ = RefreshVisibilityAsync();
    }
    private async Task RefreshVisibilityAsync()
    {
        try
        {
            bool visible; string name;
            lock (_gate) { visible = Ready && _state.Settings.Visible; name = _state.Settings.Name; }
            var label = new global::System.Text.StringBuilder();
            var bytes = 0;
            foreach (var rune in name.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > 48) break;
                label.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
            }
            await _discovery.PublishAsync(_state.Id, label + "-" + _state.Id[..8], _boundPort, visible);
        }
        catch { lock (_gate) _diagnostic = "Discovery unavailable. Check Avahi or connect by address."; }
    }
    private async Task<uint?> NotifyAsync(string title, string body, bool openFolder = false)
    {
        try
        {
            MessageBuffer call;
            using (var writer = _bus.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination: "org.freedesktop.Notifications", path: "/org/freedesktop/Notifications",
                    @interface: "org.freedesktop.Notifications", member: "Notify", signature: "susssasa{sv}i");
                writer.WriteString("DaisyOS Continuity"); writer.WriteUInt32(0); writer.WriteString("network-workgroup");
                writer.WriteString(title); writer.WriteString(global::System.Security.SecurityElement.Escape(body) ?? ""); writer.WriteArray(openFolder ? new[] { "open-folder", "Open containing folder" } : Array.Empty<string>());
                writer.WriteDictionary(new Dictionary<string, VariantValue>()); writer.WriteInt32(title == "Continuity pairing code" ? 120000 : 8000); call = writer.CreateMessage();
            }
            var id = await _bus.CallMethodAsync(call, (reply, _) => reply.GetBodyReader().ReadUInt32()).WaitAsync(TimeSpan.FromSeconds(3));
            if (openFolder) lock (_gate) { if (_fileNotifications.Count >= 64) _fileNotifications.Clear(); _fileNotifications.Add(id); }
            return id;
        }
        catch { return null; }
    }
    private async Task CloseNotificationAsync(uint id)
    {
        try
        {
            MessageBuffer call;
            using (var writer = _bus.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination: "org.freedesktop.Notifications", path: "/org/freedesktop/Notifications",
                    @interface: "org.freedesktop.Notifications", member: "CloseNotification", signature: "u");
                writer.WriteUInt32(id); call = writer.CreateMessage();
            }
            await _bus.CallMethodAsync(call).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch { }
    }
    public async ValueTask DisposeAsync()
    {
        lock (_gate) _disposing = true;
        _stop.Cancel(); _listener?.Stop();
        lock (_gate) { _sessionReady = false; StopSharing(); }
        try { await Task.WhenAll(_tasks.Values).WaitAsync(TimeSpan.FromSeconds(15)); } catch (Exception) { Console.Error.WriteLine("[continuity] shutdown_timeout_or_operation_failure"); }
        global::System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= NetworkChanged;
        _sessionWatch?.Dispose(); _sleepWatch?.Dispose(); _loginBus?.Dispose();
        _notificationWatch?.Dispose(); _ownerWatch?.Dispose(); await _discovery.DisposeAsync(); _bus.Dispose(); _stop.Dispose();
    }
}
