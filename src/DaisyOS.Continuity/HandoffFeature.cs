using global::System.Text.Json;
using DaisyOS.Core.Continuity;
using DaisyOS.Core.Serialization;
using DaisyOS.Continuity.Protocol.V1;
using DaisyOS.System.Processes;

namespace DaisyOS.Continuity;

public sealed partial class ContinuityDaemon
{
    private readonly SemaphoreSlim _handoffSlot = new(1, 1);
    private void StartHandoff(string peer, string json)
    {
        Require(peer, null);
        if (json.Length > 400_000) throw new InvalidDataException();
        var activity = JsonSerializer.Deserialize(json, DaisyCoreJsonContext.Default.ContinuityActivity) ?? throw new InvalidDataException();
        activity.Validate();
        if (!_handoffSlot.Wait(0)) throw new IOException("An activity is already being sent.");
        Track(async () =>
        {
            CancellationTokenSource? life = null;
            try
            {
                life = PeerLifetime(peer, _stop.Token);
                await using var wire = await DialAsync(PeerAddress(peer), peer, false, life.Token);
                if (!wire.Supports(Capability.Handoff)) throw new InvalidDataException("Peer must be updated for handoff.");
                await wire.SendAsync(new() { ActivityOffer = new() { AppId = activity.AppId, Kind = activity.Kind,
                    SchemaVersion = (uint)activity.SchemaVersion, Title = activity.Title, Text = activity.Text } }, life.Token);
                var result = await wire.ReceiveAsync(life.Token).WaitAsync(TimeSpan.FromMinutes(2), life.Token);
                if (result.ActivityResult?.Accepted != true) await NotifyAsync("Handoff didn’t finish", "Check that Notes is up to date and open on " + _state.Settings.Peers[peer].Name + ".");
            }
            finally { if (life is not null) EndPeer(peer, life); _handoffSlot.Release(); }
        });
    }
    internal async Task ReceiveActivityAsync(PeerWire wire, ActivityOffer offer, CancellationToken token)
    {
        RequireRequestAllowed(wire.PeerId, "handoff");
        var activity = new ContinuityActivity(offer.AppId, offer.Kind, (int)offer.SchemaVersion, offer.Title, offer.Text);
        activity.Validate();
        var accepted = true;
        string? importPath = null;
        if (accepted)
        {
            lock (_gate)
            {
                token.ThrowIfCancellationRequested(); RememberFeature(wire.PeerId, "handoff", token);
                var root = global::System.IO.Path.Combine(_state.DirectoryPath, "activities");
                Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                SecureState.Check(root, true);
                foreach (var expired in Directory.EnumerateFiles(root, "*.json"))
                    if (File.GetLastWriteTimeUtc(expired) < DateTime.UtcNow.AddDays(-1)) File.Delete(expired);
                if (Directory.EnumerateFiles(root).Take(64).Count() >= 64) throw new IOException("Activity limit.");
                var path = global::System.IO.Path.Combine(root, Guid.NewGuid().ToString("N") + ".json");
                importPath = path;
                SecureState.WritePrivate(path, JsonSerializer.SerializeToUtf8Bytes(activity, DaisyCoreJsonContext.Default.ContinuityActivity));
                var notes = global::System.IO.Path.GetFullPath(global::System.IO.Path.Combine(AppContext.BaseDirectory, "..", "Notes", "DaisyOS.Notes"));
                if (!File.Exists(notes)) notes = global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "daisyos-notes");
                if (!File.Exists(notes)) notes = "/usr/lib/daisyos/apps/Notes/DaisyOS.Notes";
                if (!File.Exists(notes) || !new SafeProcessLauncher().Launch(notes, ["--continuity-import", path]).Succeeded)
                { File.Delete(path); throw new IOException("Install the DaisyOS Notes handoff provider."); }
            }
        }
        if (accepted && importPath is not null)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (File.Exists(importPath) && DateTimeOffset.UtcNow < deadline) await Task.Delay(100, token);
            accepted = !File.Exists(importPath); // Notes deletes the receipt only after saving the restored document.
            if (!accepted) { File.Delete(importPath); await NotifyAsync("Update Notes", "Close and reopen Notes before trying this handoff again."); }
        }
        await wire.SendAsync(new() { ActivityResult = new() { Accepted = accepted } }, token);
    }
}
