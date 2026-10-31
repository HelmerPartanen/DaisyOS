using global::System.Buffers.Binary;
using global::System.Diagnostics;
using global::System.Globalization;
using global::System.Text;
using DaisyOS.Continuity.Protocol.V1;
using Google.Protobuf;

namespace DaisyOS.Continuity;

public sealed partial class ContinuityDaemon
{
    private readonly Dictionary<string, Process> _edgeCaptures = [];
    private string? _remoteInputOwner;
    private readonly SemaphoreSlim _edgeOwnership = new(1, 1);
    private readonly SemaphoreSlim _edgeCommands = new(1, 1);
    internal static void ValidateEdgeInput(string command)
    {
        var parts = command.Split(' ');
        if (parts.Length != 5 || !int.TryParse(parts[0], out var kind)) throw new InvalidDataException();
        if (kind is >= 1 and <= 4) { ValidateInput(command); return; }
        if (kind is not (6 or 7) || !uint.TryParse(parts[1], out var side) || side > 3 || parts[2] != "0" || parts[4] != "0.000000" && parts[4] != "0" ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction) || !double.IsFinite(fraction) || fraction is < 0 or > 1)
            throw new InvalidDataException();
    }
    private Process StartEdgeHelper(CancellationToken lifetime, params string[] args)
    {
        var path = global::System.IO.Path.Combine(_mediaExecutableDirectory ?? AppContext.BaseDirectory, "daisyos-continuity-media");
        if (!File.Exists(path)) throw new IOException("Install the Continuity input helper.");
        return StartPipe(path, args, lifetime, inputDiagnostics: true);
    }
    private static async Task ReadyEdgeHelperAsync(Process helper, CancellationToken token)
    {
        var header = new byte[4];
        await helper.StandardOutput.BaseStream.ReadExactlyAsync(header, token).AsTask().WaitAsync(TimeSpan.FromMinutes(2), token);
        if (BinaryPrimitives.ReadUInt32BigEndian(header) != 0) throw new InvalidDataException();
    }
    private async Task WriteEdgeLineAsync(Process helper, string command, CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        await _edgeCommands.WaitAsync(deadline.Token);
        try { await helper.StandardInput.WriteLineAsync(command.AsMemory(), deadline.Token); await helper.StandardInput.FlushAsync(deadline.Token); }
        finally { _edgeCommands.Release(); }
    }
    private async Task ChangeCaptureStateAsync(string owner, bool active)
    {
        await _edgeOwnership.WaitAsync();
        try
        {
            Process[] captures;
            lock (_gate)
            {
                if (active && _remoteInputOwner is not null && _remoteInputOwner != owner) throw new UnauthorizedAccessException();
                if (!active && _remoteInputOwner != owner) return;
                _remoteInputOwner = active ? owner : null;
                captures = _edgeCaptures.Values.ToArray();
            }
            foreach (var helper in captures)
            {
                try { await WriteEdgeLineAsync(helper, active ? "suspend" : "resume"); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException) { Kill(helper); }
            }
        }
        finally { _edgeOwnership.Release(); }
    }
    private async Task PumpEdgeHelperAsync(PeerWire wire, Live live, Process helper)
    {
        var header = new byte[4];
        while (true)
        {
            await helper.StandardOutput.BaseStream.ReadExactlyAsync(header, live.Stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), live.Stop.Token);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (length > 128) throw new InvalidDataException();
            var bytes = new byte[length];
            await helper.StandardOutput.BaseStream.ReadExactlyAsync(bytes, live.Stop.Token);
            if (length > 0)
            {
                var command = Encoding.ASCII.GetString(bytes); ValidateEdgeInput(command);
                if (!live.Info.Outgoing && command.StartsWith("7 ", StringComparison.Ordinal)) await ChangeCaptureStateAsync(live.Info.Id, false);
            }
            await wire.SendAsync(new() { LivePacket = new() { Sequence = (ulong)Interlocked.Increment(ref live.EdgeSequence), Data = ByteString.CopyFrom(bytes) } }, live.Stop.Token);
        }
    }
    private async Task PumpEdgePeerAsync(PeerWire wire, Live live, Process helper)
    {
        ulong sequence = 1;
        while (true)
        {
            var packet = (await wire.ReceiveAsync(live.Stop.Token).WaitAsync(TimeSpan.FromSeconds(10), live.Stop.Token)).LivePacket ?? throw new InvalidDataException();
            if (packet.Sequence != sequence++ || packet.Data.Length > 128) throw new InvalidDataException();
            if (packet.Data.Length == 0) continue;
            var command = Encoding.ASCII.GetString(packet.Data.Span); ValidateEdgeInput(command);
            if (live.Info.Outgoing)
            {
                if (!command.StartsWith("7 ", StringComparison.Ordinal)) throw new InvalidDataException();
                command = "release " + command.Split(' ')[3];
            }
            else
            {
                Require(live.Info.PeerId, "input");
                if (command.StartsWith("6 ", StringComparison.Ordinal)) await ChangeCaptureStateAsync(live.Info.Id, true);
                else if (command.StartsWith("7 ", StringComparison.Ordinal)) await ChangeCaptureStateAsync(live.Info.Id, false);
                else lock (_gate) if (_remoteInputOwner != live.Info.Id) throw new UnauthorizedAccessException();
            }
            await WriteEdgeLineAsync(helper, command, live.Stop.Token);
        }
    }
    private async Task RunEdgeSessionAsync(PeerWire wire, Live live, Process helper)
    {
        using var kill = live.Stop.Token.Register(() => Kill(helper));
        var send = PumpEdgeHelperAsync(wire, live, helper);
        var receive = PumpEdgePeerAsync(wire, live, helper);
        try { await await Task.WhenAny(send, receive); }
        finally
        {
            await live.Stop.CancelAsync(); Kill(helper);
            try { await Task.WhenAll(send, receive); } catch (OperationCanceledException) { }
        }
    }
    private async Task SendEdgeInputAsync(PeerWire wire, Live live)
    {
        SetLiveState(live, "Connecting");
        using var helper = StartEdgeHelper(live.Stop.Token, "--capture", live.Edge);
        using var kill = live.Stop.Token.Register(() => Kill(helper));
        try
        {
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(live.Stop.Token);
            var heartbeat = Task.Run(async () =>
            {
                while (true) { await Task.Delay(2000, waiting.Token); await wire.SendAsync(new() { LivePacket = new() { Sequence = (ulong)Interlocked.Increment(ref live.EdgeSequence) } }, waiting.Token); }
            });
            var ready = ReadyEdgeHelperAsync(helper, live.Stop.Token);
            try { await await Task.WhenAny(ready, heartbeat); }
            finally { await waiting.CancelAsync(); try { await heartbeat; } catch (OperationCanceledException) { } }
            await _edgeOwnership.WaitAsync(live.Stop.Token);
            try
            {
                bool suspended;
                lock (_gate) { _edgeCaptures[live.Info.Id] = helper; suspended = _remoteInputOwner is not null; }
                if (suspended) await WriteEdgeLineAsync(helper, "suspend", live.Stop.Token);
            }
            finally { _edgeOwnership.Release(); }
            SetLiveState(live, "Ready", "Move the pointer across the " + live.Edge + " edge. Ctrl+Alt+Shift+Escape returns it.");
            await RunEdgeSessionAsync(wire, live, helper);
        }
        finally { lock (_gate) _edgeCaptures.Remove(live.Info.Id); Kill(helper); }
    }
    private async Task ReceiveEdgeInputAsync(PeerWire wire, Live live)
    {
        SetLiveState(live, "Connecting");
        // The paired session carries a scoped grant into the compositor portal.
        using var helper = StartEdgeHelper(live.Stop.Token, "--input");
        using var kill = live.Stop.Token.Register(() => Kill(helper));
        try
        {
            await ReadyEdgeHelperAsync(helper, live.Stop.Token);
            RememberFeature(wire.PeerId, "input", live.Stop.Token);
            await wire.SendAsync(new() { LiveDecision = new() { Accepted = true } }, live.Stop.Token);
            SetLiveState(live, "Ready", "Keyboard and mouse link is ready.");
            await RunEdgeSessionAsync(wire, live, helper);
        }
        finally { await ChangeCaptureStateAsync(live.Info.Id, false); Kill(helper); }
    }
}
