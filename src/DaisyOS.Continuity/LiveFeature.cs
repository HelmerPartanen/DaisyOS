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
        public ContinuityLiveSession Info { get; } = info;
        public CancellationTokenSource Stop { get; } = stop;
        public byte[] Frame = [];
        public Channel<byte[]> Input { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    }
    private readonly Dictionary<string, Live> _live = new();
    private const int AudioBytes = 3840; // 20 ms, 48 kHz, stereo signed 16-bit LE
    private const int MaxJpeg = 512 * 1024;

    private Live AddLive(string id, string peerId, string kind, bool outgoing, bool control, CancellationToken parent)
    {
        lock (_gate)
        {
            Require(peerId, kind);
            if (control) Require(peerId, "input");
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
        lock (_gate) { _live.Remove(live.Info.Id); live.Frame = []; }
        _ = live.Stop.CancelAsync();
        EndPeer(live.Info.PeerId, live.Stop);
    }
    private string PeerAddress(string id)
    {
        lock (_gate) return _nearby.GetValueOrDefault(id)?.Address ?? _state.Settings.Peers[id].Address;
    }
    private void StartLive(string peer, string kind, bool control)
    {
        if (kind is not ("screen" or "audio") || kind == "audio" && control) throw new InvalidDataException();
        var live = AddLive(Guid.NewGuid().ToString("N"), peer, kind, true, control, _stop.Token);
        Track(async () =>
        {
            try
            {
                await using var wire = await DialAsync(PeerAddress(peer), peer, false, live.Stop.Token);
                if (!wire.Supports(kind == "audio" ? Capability.Audio : Capability.Screen) || control && !wire.Supports(Capability.Input))
                    throw new InvalidDataException("Peer must be updated for this feature.");
                await wire.SendAsync(new() { LiveStart = new() { SessionId = live.Info.Id, Kind = kind, Control = control } }, live.Stop.Token);
                var decision = await wire.ReceiveAsync(live.Stop.Token).WaitAsync(TimeSpan.FromMinutes(3), live.Stop.Token);
                if (decision.LiveDecision?.Accepted != true) return;
                if (kind == "audio") await SendAudioAsync(wire, live);
                else await ViewScreenAsync(wire, live);
            }
            finally { RemoveLive(live); }
        });
    }
    internal async Task ReceiveLiveAsync(PeerWire wire, LiveStart start, CancellationToken token)
    {
        if (start.Kind is not ("audio" or "screen") || start.Kind == "audio" && start.Control) throw new InvalidDataException();
        var live = AddLive(start.SessionId, wire.PeerId, start.Kind, false, start.Control, token);
        try
        {
            var detail = start.Kind == "audio" ? "Play this computer’s audio on your speakers?" :
                start.Control ? "Allow this computer to view your screen and control your keyboard and mouse?" : "Allow this computer to view your screen?";
            if (!await PromptAsync(start.Kind, wire.PeerId, wire.Remote.Name, detail, live.Stop.Token))
            { await wire.SendAsync(new() { LiveDecision = new() { Accepted = false } }, live.Stop.Token); return; }
            Require(wire.PeerId, start.Kind);
            if (start.Control) Require(wire.PeerId, "input");
            if (start.Kind == "audio") await ReceiveAudioAsync(wire, live);
            else await ShareScreenAsync(wire, live);
        }
        finally { RemoveLive(live); }
    }
    private Process StartPipe(string name, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(_mediaExecutableDirectory is not null && !global::System.IO.Path.IsPathFullyQualified(name) ? global::System.IO.Path.Combine(_mediaExecutableDirectory, name) : name) { UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        Process process;
        try { process = Process.Start(start) ?? throw new IOException("Media helper unavailable."); }
        catch (global::System.ComponentModel.Win32Exception ex) { throw new IOException("Media dependency unavailable.", ex); }
        // Drain without retaining audio-server or portal diagnostics in memory/logs.
        _ = DrainAsync(process.StandardError.BaseStream);
        return process;
    }
    private static async Task DrainAsync(Stream stream)
    { try { var bytes = new byte[1024]; while (await stream.ReadAsync(bytes) > 0) { } } catch (IOException) { } }
    private static void Kill(Process process)
    { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
    private async Task ShareScreenAsync(PeerWire wire, Live live)
    {
        var path = global::System.IO.Path.Combine(_mediaExecutableDirectory ?? AppContext.BaseDirectory, "daisyos-continuity-media");
        if (!File.Exists(path)) throw new IOException("Install the Continuity media helper.");
        using var helper = StartPipe(path, [live.Info.Control ? "--control" : "--view"]);
        using var kill = live.Stop.Token.Register(() => Kill(helper));
        try
        {
            var header = new byte[4];
            await helper.StandardOutput.BaseStream.ReadExactlyAsync(header, live.Stop.Token).AsTask().WaitAsync(TimeSpan.FromMinutes(3), live.Stop.Token);
            if (BinaryPrimitives.ReadUInt32BigEndian(header) != 0) throw new InvalidDataException();
            Require(wire.PeerId, "screen"); if (live.Info.Control) Require(wire.PeerId, "input");
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
