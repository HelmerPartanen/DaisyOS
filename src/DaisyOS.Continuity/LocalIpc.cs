using global::System.Text;
using global::System.Text.Json;
using DaisyOS.Core.Continuity;
using DaisyOS.Core.Serialization;
using DaisyOS.System.Continuity;
using Tmds.DBus.Protocol;

namespace DaisyOS.Continuity;

public sealed partial class ContinuityDaemon
{
    public ValueTask HandleMethodAsync(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml(new[] { (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(
                "<interface name='org.daisyos.Continuity1'><method name='Execute'><arg type='s' direction='in'/><arg type='s' direction='out'/></method><method name='ReadFrame'><arg type='s' direction='in'/><arg type='ay' direction='out'/></method></interface>") });
            return default;
        }
        if (context.Request.InterfaceAsString == ContinuityClient.BusName && context.Request.MemberAsString == "ReadFrame" && context.Request.SignatureAsString == "s")
        {
            var id = context.Request.GetBodyReader().ReadString();
            return new ValueTask(ReplyFrameAsync(context, id));
        }
        if (context.Request.InterfaceAsString != ContinuityClient.BusName || context.Request.MemberAsString != "Execute" ||
            context.Request.SignatureAsString != "s")
        { context.ReplyError("org.freedesktop.DBus.Error.UnknownMethod", "Unsupported Continuity method."); return default; }
        try
        {
            var input = context.Request.GetBodyReader().ReadString();
            if (input.Length > 1_500_000) throw new InvalidDataException();
            var command = JsonSerializer.Deserialize(input, DaisyCoreJsonContext.Default.ContinuityCommand) ?? throw new InvalidDataException();
            return new ValueTask(ReplyAsync(context, command));
        }
        catch { context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", "Invalid Continuity request."); return default; }
    }
    private async Task ReplyFrameAsync(MethodContext context, string id)
    {
        try
        {
            if (id.Length != 32 || await BusUintAsync("GetConnectionUnixUser", context.Request.SenderAsString!) != GetUid()) throw new UnauthorizedAccessException();
            using var writer = context.CreateReplyWriter("ay");
            writer.WriteArray(ReadFrame(id)); context.Reply(writer.CreateMessage());
        }
        catch { context.ReplyError("org.daisyos.Continuity1.RequestFailed", "Frame unavailable."); }
    }
    private async Task<uint> BusUintAsync(string member, string sender)
    {
        MessageBuffer call;
        using (var writer = _bus.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: "org.freedesktop.DBus", path: "/org/freedesktop/DBus",
                @interface: "org.freedesktop.DBus", member: member, signature: "s");
            writer.WriteString(sender); call = writer.CreateMessage();
        }
        return await _bus.CallMethodAsync(call, (reply, _) => reply.GetBodyReader().ReadUInt32()).WaitAsync(TimeSpan.FromSeconds(3));
    }
    private async Task<string> ShellOwnerAsync()
    {
        MessageBuffer call;
        using (var writer = _bus.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: "org.freedesktop.DBus", path: "/org/freedesktop/DBus",
                @interface: "org.freedesktop.DBus", member: "GetNameOwner", signature: "s");
            writer.WriteString("org.daisyos.ContinuitySession1"); call = writer.CreateMessage();
        }
        return await _bus.CallMethodAsync(call, (reply, _) => reply.GetBodyReader().ReadString()).WaitAsync(TimeSpan.FromSeconds(3));
    }
    private async Task ReplyAsync(MethodContext context, ContinuityCommand command)
    {
        try
        {
            if (await BusUintAsync("GetConnectionUnixUser", context.Request.SenderAsString!) != GetUid()) throw new UnauthorizedAccessException();
            await ExecuteAsync(command, context.Request.SenderAsString!);
            using var writer = context.CreateReplyWriter("s");
            writer.WriteString(JsonSerializer.Serialize(Snapshot(), DaisyCoreJsonContext.Default.ContinuityStatus));
            context.Reply(writer.CreateMessage());
        }
        catch { context.ReplyError("org.daisyos.Continuity1.RequestFailed", "Continuity request failed. Check session, permission, device address, or service status."); }
    }
    internal async Task ExecuteAsync(ContinuityCommand command, string sender)
    {
        switch (command.Action)
        {
            case "status": return;
            case "session":
                long revision;
                lock (_gate) revision = ++_sessionRevision;
                if (await ShellOwnerAsync() != sender) throw new UnauthorizedAccessException();
                var sessionId = string.IsNullOrEmpty(command.Value) ? await ResolveSeatSessionIdAsync() : command.Value;
                var valid = command.Allowed && await _sessionUnlocked(sessionId);
                lock (_gate)
                {
                    if (revision != _sessionRevision) throw new UnauthorizedAccessException();
                    var wasReady = Ready;
                    _shellOwner = sender; _sessionId = sessionId; _sessionGranted = command.Allowed; _sessionReady = valid && !_preparingForSleep;
                    if (!_sessionReady) StopSharing();
                    else { UpdateClipboardState(); _ = RefreshVisibilityAsync(); if (!wasReady) Track(ReconcilePeersAsync); }
                }
                return;
            case "enable":
            case "visible":
            case "pause":
            case "name":
                lock (_gate)
                {
                    if (command.Action == "enable") { _state.Settings.Enabled = command.Allowed; if (command.Allowed) { _state.Settings.Visible = true; _state.Settings.Paused = false; } }
                    if (command.Action == "visible") _state.Settings.Visible = command.Allowed;
                    if (command.Action == "pause") _state.Settings.Paused = command.Allowed;
                    if (command.Action == "name") _state.Settings.Name = SecureState.SafeName(command.Value);
                    SaveSettings();
                    if (!Ready) StopSharing(); else { UpdateClipboardState(); _ = RefreshVisibilityAsync(); }
                    _diagnostic = "";
                }
                return;
            case "capture-grant":
                lock (_gate)
                {
                    if (!Ready || !_captureGrants.TryGetValue(command.Value, out var grant) || grant.IsCancellationRequested)
                        throw new UnauthorizedAccessException();
                }
                return;
            case "pair-cancel":
                lock (_gate)
                {
                    if (!_prompts.TryGetValue(command.Id, out var pairing) || !pairing.Prompt.Kind.StartsWith("pair-", StringComparison.Ordinal)) return;
                    if (_pairInputs.TryGetValue(command.Id, out var pendingInput)) pendingInput.TrySetCanceled();
                    if (_peerOperations.TryGetValue(pairing.Prompt.PeerId, out var operations))
                        foreach (var operation in operations.ToArray()) _ = operation.CancelAsync();
                }
                return;
            case "pair-code":
                lock (_gate)
                {
                    if (!Ready || !_pairInputs.TryGetValue(command.Id, out var input) || !_prompts.TryGetValue(command.Id, out var pending)) throw new InvalidDataException();
                    if (command.Value.Length != 6 || !command.Value.All(char.IsAsciiDigit)) throw new InvalidDataException();
                    input.TrySetResult(command.Value);
                }
                return;
            case "forget":
                await ForgetRemoteAsync(command.Id);
                return;
            case "decide":
                lock (_gate)
                {
                    if (!Ready || !_prompts.TryGetValue(command.Id, out var prompt) || prompt.Prompt.Kind.StartsWith("pair-")) throw new InvalidDataException();
                    prompt.Decision.TrySetResult(command.Allowed);
                }
                return;
            case "connect":
                lock (_gate) if (!Ready || _tasks.Count >= 16) throw new UnauthorizedAccessException();
                Track(async () =>
                {
                    using var life = CreateSharingLifetime(_stop.Token);
                    life.CancelAfter(TimeSpan.FromSeconds(15));
                    await using var wire = await DialAsync(command.Value, command.Id.Length == 64 ? command.Id : null, false, life.Token);
                    Require(wire.PeerId, null);
                    await wire.SendAsync(new() { Ping = new DaisyOS.Continuity.Protocol.V1.Ping { Sequence = 1 } }, life.Token);
                    if ((await wire.ReceiveAsync(life.Token)).Pong?.Sequence != 1) throw new InvalidDataException();
                    lock (_gate)
                    {
                        Require(wire.PeerId, null);
                        _diagnostic = "";
                        var peer = _state.Settings.Peers[wire.PeerId];
                        _state.Settings.Peers[wire.PeerId] = peer with { Address = command.Value };
                        _nearby[wire.PeerId] = new(wire.PeerId, peer.Name, command.Value, "Trusted · verified", true, peer.Clipboard, peer.Files);
                        SaveSettings();
                    }
                });
                return;
            case "pair":
                lock (_gate)
                {
                    if (!Ready || _tasks.Count >= 16) throw new UnauthorizedAccessException();
                    if (command.Id.Length > 0) _revoked.Remove(command.Id);
                }
                Track(async () =>
                {
                    using var deadline = CreateSharingLifetime(_stop.Token);
                    deadline.CancelAfter(TimeSpan.FromMinutes(3));
                    await using var wire = await DialAsync(command.Value, command.Id.Length == 64 ? command.Id : null, true, deadline.Token);
                    using var life = PeerLifetime(wire.PeerId, deadline.Token, true);
                    try { await PairAsync(wire, command.Value, life.Token); }
                    finally { EndPeer(wire.PeerId, life); }
                });
                return;
            case "send":
                Require(command.Id, "files");
                if (!_outgoingSlots.Wait(0)) throw new IOException("Transfer limit.");
                Track(async () => { try { await SendFileAsync(command.Id, command.Value, _stop.Token); } finally { _outgoingSlots.Release(); } });
                return;
            case "cancel":
                lock (_gate)
                {
                    if (_transferCancellation.TryGetValue(command.Id, out var cancellation)) _ = cancellation.CancelAsync();
                    if (_transferResumeRecords.TryGetValue(command.Id, out var record)) File.Delete(record);
                }
                return;
            case "edge":
                if (command.Value is not ("left" or "right" or "top" or "bottom")) throw new InvalidDataException();
                lock (_gate)
                {
                    Require(command.Id, null);
                    _state.Settings.Peers[command.Id] = _state.Settings.Peers[command.Id] with { InputEdge = command.Value };
                    SaveSettings();
                }
                lock (_gate)
                {
                    var links = _live.Values.Where(l => l.Info.PeerId == command.Id && l.Info.Kind == "input" && l.Info.Outgoing).ToArray();
                    foreach (var live in links) _ = live.Stop.CancelAsync();
                    if (links.Length == 0) StartLive(command.Id, "input", false, command.Value);
                }
                return;
            case "live":
                StartLive(command.Id, command.Value, command.Allowed);
                return;
            case "live-stop":
                lock (_gate) if (_live.TryGetValue(command.Id, out var live)) _ = live.Stop.CancelAsync();
                return;
            case "live-input":
                QueueInput(command.Id, command.Value);
                return;
            case "handoff":
                StartHandoff(command.Id, command.Value);
                return;
            case "clipboard":
                if (command.Value.Length > 1_400_000) throw new InvalidDataException();
                var data = Convert.FromBase64String(command.Value);
                try { await PublishClipboardAsync(data); } finally { global::System.Security.Cryptography.CryptographicOperations.ZeroMemory(data); }
                return;
            default: throw new InvalidDataException();
        }
    }
    private static async Task<string> ResolveSeatSessionIdAsync()
    {
        try
        {
            var uid = GetUid().ToString();
            var runner = new DaisyOS.System.Processes.SafeCommandRunner();
            var list = await runner.RunAsync("loginctl", ["list-sessions", "--no-legend"], TimeSpan.FromSeconds(2));
            if (!list.Succeeded) return "";
            foreach (var line in list.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 && parts[1] == uid && parts[3] != "-" && await SessionUnlockedAsync(parts[0]))
                    return parts[0];
            }
        }
        catch { }
        return "";
    }
    private static async Task<bool> SessionUnlockedAsync(string id)
    {
        if (id.Length is < 1 or > 64 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) return false;
        try
        {
            var runner = new DaisyOS.System.Processes.SafeCommandRunner();
            var result = await runner.RunAsync("loginctl", ["show-session", id, "--property=Active", "--property=LockedHint", "--property=User", "--property=State"], TimeSpan.FromSeconds(2));
            var lines = result.StandardOutput.Split('\n').ToHashSet();
            return result.Succeeded && lines.Contains("Active=yes") && lines.Contains("LockedHint=no") && lines.Contains("State=active") && lines.Contains("User=" + GetUid());
        }
        catch { return false; }
    }
    [global::System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetUid();
}
