using global::System.Buffers.Binary;
using global::System.Diagnostics;
using global::System.Globalization;
using global::System.Text;
using global::System.Threading.Channels;
using DaisyOS.Core.Continuity;
using DaisyOS.Continuity.Protocol.V1;
using DaisyOS.System.Processes;
using Google.Protobuf;

namespace DaisyOS.Continuity;

public sealed partial class ContinuityDaemon
{
    private sealed class Live(ContinuityLiveSession info, CancellationTokenSource stop)
    {
        public ContinuityLiveSession Info { get; set; } = info;
        public CancellationTokenSource Stop { get; } = stop;
        public byte[] Frame = [];
        public string Edge = "right";
        public long EdgeSequence;
        public Channel<byte[]> Input { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    }
    private readonly Dictionary<string, Live> _live = new();
    private readonly List<(ContinuityLiveSession Info, DateTimeOffset Expires)> _recentLive = [];
    private static int FeatureBit(string feature) => feature switch { "input" => 1, "audio" => 2, "screen" => 4, "handoff" => 8, _ => 0 };
    private void RequireRequestAllowed(string id, string feature)
    { lock (_gate) { Require(id, null); if ((_state.Settings.Peers[id].BlockedFeatures & FeatureBit(feature)) != 0) throw new UnauthorizedAccessException(); } }
    private void RememberFeature(string id, string feature, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested(); RequireRequestAllowed(id, feature);
            var peer = _state.Settings.Peers[id];
            _state.Settings.Peers[id] = feature switch { "audio" => peer with { Audio = true }, "input" => peer with { Input = true }, "screen" => peer with { Screen = true }, "handoff" => peer with { Handoff = true }, _ => throw new InvalidDataException() };
            SaveSettings();
        }
    }
    private void SetLiveState(Live live, string state, string detail = "")
    { lock (_gate) live.Info = live.Info with { State = state, Detail = detail }; }

    private const int AudioBytes = 3840; // 20 ms, 48 kHz, stereo signed 16-bit LE
    private const int MaxJpeg = 512 * 1024;

    private Live AddLive(string id, string peerId, string kind, bool outgoing, bool control, CancellationToken parent)
    {
        lock (_gate)
        {
            if (outgoing) Require(peerId, null);
            else if (kind is "audio" or "input" or "screen") RequireRequestAllowed(peerId, kind);
            else { Require(peerId, kind); if (control) Require(peerId, "input"); }
            if (!Guid.TryParseExact(id, "N", out _) || _live.Count >= 4 || _live.ContainsKey(id) ||
                _live.Values.Any(l => l.Info.Kind == kind && (kind == "audio" || l.Info.PeerId == peerId && l.Info.Outgoing == outgoing)))
                throw new InvalidDataException("Live session limit.");
            var life = PeerLifetime(peerId, parent);
            var live = new Live(new(id, peerId, _state.Settings.Peers[peerId].Name, kind, outgoing, control), life);
            _live.Add(id, live);
            return live;
        }
    }
    private void RemoveLive(Live live)
    {
        lock (_gate)
        {
            _live.Remove(live.Info.Id); live.Frame = [];
            if (live.Info.Kind != "input" && live.Info.State is "Failed" or "Declined")
            { if (_recentLive.Count >= 8) _recentLive.RemoveAt(0); _recentLive.Add((live.Info, DateTimeOffset.UtcNow.AddSeconds(30))); }
        }
        _ = live.Stop.CancelAsync();
        EndPeer(live.Info.PeerId, live.Stop);
    }
    private string PeerAddress(string id)
    {
        lock (_gate) return _nearby.GetValueOrDefault(id)?.Address ??
            (_state.Settings.Peers.TryGetValue(id, out var peer) ? peer.Address : throw new OperationCanceledException("Device was forgotten."));
    }
    private void StartLive(string peer, string kind, bool control, string edge = "right")
    {
        if (kind is not ("screen" or "audio" or "input") || kind == "audio" && control) throw new InvalidDataException();
        if (edge is not ("left" or "right" or "top" or "bottom")) throw new InvalidDataException();
        var live = AddLive(Guid.NewGuid().ToString("N"), peer, kind, true, control, _stop.Token);
        live.Edge = edge;
        Track(async () =>
        {
            try
            {
                await using var wire = await DialPeerAsync(peer, live.Stop.Token);
                if (!wire.Supports(kind == "audio" ? Capability.Audio : kind == "input" ? Capability.EdgeInput : Capability.Screen) || control && !wire.Supports(Capability.Input))
                    throw new InvalidDataException("Peer must be updated for this feature.");
                await wire.SendAsync(new() { LiveStart = new() { SessionId = live.Info.Id, Kind = kind, Control = control } }, live.Stop.Token);
                SetLiveState(live, "Connecting");
                var decision = await wire.ReceiveAsync(live.Stop.Token).WaitAsync(TimeSpan.FromMinutes(3), live.Stop.Token);
                if (decision.LiveDecision?.Accepted != true) { SetLiveState(live, "Declined", "The other computer declined the request."); return; }
                if (kind == "audio") await SendAudioAsync(wire, live);
                else if (kind == "input") await SendEdgeInputAsync(wire, live);
                else { SetLiveState(live, "Connected"); await ViewScreenAsync(wire, live); }
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException or global::System.Net.Sockets.SocketException or global::System.Security.Authentication.AuthenticationException)
            {
                if (!live.Stop.IsCancellationRequested)
                {
                    if (kind == "input") Console.Error.WriteLine("[continuity] input_link_failed: " + ex.GetType().Name + ": " + ex.Message);
                    SetLiveState(live, "Failed", "Couldn’t connect. Unlock the other computer and check that Continuity is up to date.");
                    if (kind != "input") _ = NotifyAsync("Continuity", "Couldn’t connect to " + live.Info.Name + ". Unlock the other computer and try again.");
                }
                if (kind != "input") throw;
            }
            finally { RemoveLive(live); }
        });
    }
    internal async Task ReceiveLiveAsync(PeerWire wire, LiveStart start, CancellationToken token)
    {
        if (start.Kind is not ("audio" or "screen" or "input") || start.Kind == "audio" && start.Control) throw new InvalidDataException();
        var live = AddLive(start.SessionId, wire.PeerId, start.Kind, false, start.Control, token);
        try
        {
            if (start.Kind == "input") { await ReceiveEdgeInputAsync(wire, live); return; }
            if (start.Kind == "screen") { await ShareScreenAsync(wire, live); return; }
            if (start.Kind == "audio") { RememberFeature(wire.PeerId, "audio", live.Stop.Token); await ReceiveAudioAsync(wire, live); }
            else { Require(wire.PeerId, start.Kind); if (start.Control) Require(wire.PeerId, "input"); await ShareScreenAsync(wire, live); }
        }
        finally { RemoveLive(live); }
    }
    private readonly Dictionary<string, CancellationToken> _captureGrants = new();
    private Process StartPipe(string name, IEnumerable<string> args, CancellationToken? captureLifetime = null, bool inputDiagnostics = false)
    {
        var start = new ProcessStartInfo(_mediaExecutableDirectory is not null && !global::System.IO.Path.IsPathFullyQualified(name) ? global::System.IO.Path.Combine(_mediaExecutableDirectory, name) : name) { UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (captureLifetime is { } lifetime)
        {
            var grant = Convert.ToHexStringLower(global::System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            lock (_gate) _captureGrants[grant] = lifetime;
            lifetime.Register(() => { lock (_gate) _captureGrants.Remove(grant); });
            start.Environment["DAISY_CONTINUITY_GRANT"] = grant;
        }
        Process process;
        try { process = Process.Start(start) ?? throw new IOException("Media helper unavailable."); }
        catch (global::System.ComponentModel.Win32Exception ex) { throw new IOException("Media dependency unavailable.", ex); }
        // Drain without retaining audio-server or portal diagnostics in memory/logs.
        _ = inputDiagnostics ? DrainInputDiagnosticsAsync(process.StandardError) : DrainAsync(process.StandardError.BaseStream);
        return process;
    }
    private static async Task DrainInputDiagnosticsAsync(StreamReader reader)
    {
        try
        {
            var buffer = new char[1024];
            while (await reader.ReadAsync(buffer) is var count && count > 0)
            {
                var text = new string(buffer, 0, count);
                foreach (var stage in new[] { "create session", "connect EIS", "configure barriers", "enable capture", "wait for keyboard and pointer devices" })
                    if (text.Contains("Continuity input initialization failed: " + stage, StringComparison.Ordinal))
                        Console.Error.WriteLine("[continuity] input_initialization_failed: " + stage);
            }
        }
        catch (IOException) { }
    }
    private static async Task DrainAsync(Stream stream)
    { try { var bytes = new byte[1024]; while (await stream.ReadAsync(bytes) > 0) { } } catch (IOException) { } }
    private static void Kill(Process process)
    { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception) { } }
    private async Task ShareScreenAsync(PeerWire wire, Live live)
    {
        var path = global::System.IO.Path.Combine(_mediaExecutableDirectory ?? AppContext.BaseDirectory, "daisyos-continuity-media");
        if (!File.Exists(path)) throw new IOException("Install the Continuity media helper.");
        using var helper = StartPipe(path, [live.Info.Control ? "--control" : "--view"], live.Stop.Token);
        using var kill = live.Stop.Token.Register(() => Kill(helper));
        try
        {
            var header = new byte[4];
            await helper.StandardOutput.BaseStream.ReadExactlyAsync(header, live.Stop.Token).AsTask().WaitAsync(TimeSpan.FromMinutes(3), live.Stop.Token);
            if (BinaryPrimitives.ReadUInt32BigEndian(header) != 0) throw new InvalidDataException();
            RememberFeature(wire.PeerId, "screen", live.Stop.Token);
            if (live.Info.Control) RememberFeature(wire.PeerId, "input", live.Stop.Token);
            SetLiveState(live, "Connected");
            await wire.SendAsync(new() { LiveDecision = new() { Accepted = true } }, live.Stop.Token);
            // Both directions are independent: frames never queue behind input.
            var read = ReceiveInputAsync(wire, live, helper);
            ulong sequence = 0;
            try
            {
                while (!live.Stop.IsCancellationRequested)
                {
                    await helper.StandardOutput.BaseStream.ReadExactlyAsync(header, live.Stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(15), live.Stop.Token);
                    var size = BinaryPrimitives.ReadUInt32BigEndian(header);
                    if (size > MaxJpeg || size is > 0 and < 4) throw new InvalidDataException();
                    var bytes = new byte[size];
                    await helper.StandardOutput.BaseStream.ReadExactlyAsync(bytes, live.Stop.Token);
                    await wire.SendAsync(new() { LivePacket = new() { Sequence = ++sequence, Data = ByteString.CopyFrom(bytes) } }, live.Stop.Token);
                }
            }
            finally { await live.Stop.CancelAsync(); try { await read; } catch (OperationCanceledException) { } }
        }
        finally
        {
            // EOF lets libei stop emulation and closes the authorized portal session.
            helper.StandardInput.Close();
            try { await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); } catch (TimeoutException) { Kill(helper); }
        }
    }
    private async Task ReceiveInputAsync(PeerWire wire, Live live, Process helper)
    {
        ulong expected = 1;
        try
        {
            while (true)
            {
                var packet = (await wire.ReceiveAsync(live.Stop.Token).WaitAsync(TimeSpan.FromSeconds(10), live.Stop.Token)).LivePacket ?? throw new InvalidDataException();
                if (packet.Sequence != expected++ || packet.Data.Length > 128) throw new InvalidDataException();
                if (packet.Data.Length == 0) continue; // liveness, including view-only sessions
                if (!live.Info.Control) throw new UnauthorizedAccessException();
                Require(wire.PeerId, "input");
                var command = Encoding.ASCII.GetString(packet.Data.Span);
                ValidateInput(command);
                await helper.StandardInput.WriteLineAsync(command.AsMemory(), live.Stop.Token);
                await helper.StandardInput.FlushAsync(live.Stop.Token);
            }
        }
        finally { await live.Stop.CancelAsync(); }
    }
    private async Task ViewScreenAsync(PeerWire wire, Live live)
    {
        var send = Task.Run(async () =>
        {
            ulong sequence = 0;
            while (!live.Stop.IsCancellationRequested)
            {
                byte[] bytes = [];
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(live.Stop.Token);
                tick.CancelAfter(TimeSpan.FromSeconds(2));
                try { bytes = await live.Input.Reader.ReadAsync(tick.Token); }
                catch (OperationCanceledException) when (!live.Stop.IsCancellationRequested) { }
                await wire.SendAsync(new() { LivePacket = new() { Sequence = ++sequence, Data = ByteString.CopyFrom(bytes) } }, live.Stop.Token);
            }
        });
        ulong expected = 1;
        try
        {
            while (true)
            {
                var packet = (await wire.ReceiveAsync(live.Stop.Token).WaitAsync(TimeSpan.FromSeconds(15), live.Stop.Token)).LivePacket ?? throw new InvalidDataException();
                if (packet.Sequence != expected++ || packet.Data.Length > MaxJpeg || packet.Data.Length > 0 && !IsVideoFrame(packet.Data.Span))
                    throw new InvalidDataException();
                if (packet.Data.Length > 0) lock (_gate) live.Frame = packet.Data.ToByteArray(); // latest frame only
            }
        }
        finally { await live.Stop.CancelAsync(); try { await send; } catch (OperationCanceledException) { } }
    }
    // JPEG SOF dimensions are checked before any decoder sees untrusted bytes.
    internal static bool IsVideoFrame(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8) return false;
        var offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset++] != 0xff) return false;
            while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
            if (offset >= bytes.Length) return false;
            var marker = bytes[offset++];
            if (marker is 0xda or 0xd9) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
            if (length < 2 || offset + length > bytes.Length) return false;
            if (marker is 0xc0 or 0xc1 or 0xc2)
                return length >= 8 && bytes[offset + 2] == 8 && BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 3)..]) == 720 &&
                    BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]) == 1280;
            offset += length;
        }
        return false;
    }
    internal static void ValidateInput(string command)
    {
        var parts = command.Split(' ');
        if (parts.Length != 5 || !uint.TryParse(parts[0], out var kind) || kind is < 1 or > 5 ||
            !uint.TryParse(parts[1], out var code) || !uint.TryParse(parts[2], out var state) || state > 1 ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 8192 || Math.Abs(y) > 8192 ||
            kind == 1 && code > 767 || kind == 2 && code is not (>= 272 and <= 279) ||
            kind == 5 && (x < 0 || x > 1 || y < 0 || y > 1)) throw new InvalidDataException("Invalid input event.");
    }
    private void QueueInput(string id, string command)
    {
        ValidateInput(command);
        lock (_gate)
        {
            if (!_live.TryGetValue(id, out var live) || !live.Info.Outgoing || !live.Info.Control) throw new UnauthorizedAccessException();
            Require(live.Info.PeerId, "input");
            if (!live.Input.Writer.TryWrite(Encoding.ASCII.GetBytes(command)))
            { _ = live.Stop.CancelAsync(); throw new IOException("Input backlog; control stopped to release held keys."); }
        }
    }
    internal byte[] ReadFrame(string id)
    {
        lock (_gate)
        {
            if (!_live.TryGetValue(id, out var live)) return [];
            Require(live.Info.PeerId, "screen");
            return live.Frame;
        }
    }
}
