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
                "<interface name='org.daisyos.Continuity1'><method name='Execute'><arg type='s' direction='in'/><arg type='s' direction='out'/></method></interface>") });
            return default;
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
                var valid = command.Allowed && await SessionUnlockedAsync(command.Value);
                lock (_gate)
                {
                    if (revision != _sessionRevision) throw new UnauthorizedAccessException();
                    _shellOwner = sender; _sessionId = command.Value; _sessionGranted = command.Allowed; _sessionReady = valid;
                    if (!valid) StopSharing();
                    else { UpdateClipboardState(); _ = RefreshVisibilityAsync(); }
                }
                return;
            case "enable":
            case "visible":
            case "pause":
            case "name":
                lock (_gate)
                {
                    if (command.Action == "enable") _state.Settings.Enabled = command.Allowed;
                    if (command.Action == "visible") _state.Settings.Visible = command.Allowed;
                    if (command.Action == "pause") _state.Settings.Paused = command.Allowed;
                    if (command.Action == "name") _state.Settings.Name = SecureState.SafeName(command.Value);
                    SaveSettings();
                    if (!Ready) StopSharing(); else { UpdateClipboardState(); _ = RefreshVisibilityAsync(); }
                    _diagnostic = "";
                }
                return;
            case "permission":
                lock (_gate)
                {
                    if (!_state.Settings.Peers.TryGetValue(command.Id, out var peer)) throw new InvalidDataException();
                    _state.Settings.Peers[command.Id] = command.Value switch
                    { "clipboard" => peer with { Clipboard = command.Allowed }, "files" => peer with { Files = command.Allowed }, _ => throw new InvalidDataException() };
                    if (!command.Allowed)
                    {
                        _state.Settings.Peers[command.Id] = _state.Settings.Peers[command.Id] with { GrantId = Guid.NewGuid().ToString("N") };
                    }
                    if (!command.Allowed && _peerOperations.TryGetValue(command.Id, out var operations))
                        foreach (var operation in operations.ToArray()) _ = operation.CancelAsync();
                    if (command.Value == "clipboard" && !command.Allowed) _clipboard.ClearPeer(command.Id);
                    SaveSettings();
                    UpdateClipboardState();
                }
                return;
            case "forget":
                lock (_gate)
                {
                    _trustEpoch[command.Id] = _trustEpoch.GetValueOrDefault(command.Id) + 1;
                    _revoked.Add(command.Id); _state.Settings.Peers.Remove(command.Id); _nearby.Remove(command.Id);
                    if (_peerOperations.TryGetValue(command.Id, out var operations)) foreach (var operation in operations.ToArray()) _ = operation.CancelAsync();
                    _clipboard.ClearPeer(command.Id);
                    foreach (var request in _prompts.Values.Where(p => p.Prompt.PeerId == command.Id)) request.Decision.TrySetResult(false);
                    SaveSettings();
                }
                return;
            case "decide":
                lock (_gate)
                {
                    if (!Ready || !_prompts.TryGetValue(command.Id, out var prompt)) throw new InvalidDataException();
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
            case "clipboard":
                if (command.Value.Length > 1_400_000) throw new InvalidDataException();
                var data = Convert.FromBase64String(command.Value);
                try { await PublishClipboardAsync(data); } finally { global::System.Security.Cryptography.CryptographicOperations.ZeroMemory(data); }
                return;
            default: throw new InvalidDataException();
        }
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
