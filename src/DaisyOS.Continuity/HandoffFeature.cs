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
        Require(peer, "handoff");
        if (json.Length > 400_000) throw new InvalidDataException();
        var activity = JsonSerializer.Deserialize(json, DaisyCoreJsonContext.Default.ContinuityActivity) ?? throw new InvalidDataException();
        activity.Validate();
        if (!_handoffSlot.Wait(0)) throw new IOException("An activity is already being sent.");
        Track(async () =>
        {
            using var life = PeerLifetime(peer, _stop.Token);
            try
            {
                await using var wire = await DialAsync(PeerAddress(peer), peer, false, life.Token);
                if (!wire.Supports(Capability.Handoff)) throw new InvalidDataException("Peer must be updated for handoff.");
                await wire.SendAsync(new() { ActivityOffer = new() { AppId = activity.AppId, Kind = activity.Kind,
                    SchemaVersion = (uint)activity.SchemaVersion, Title = activity.Title, Text = activity.Text } }, life.Token);
                var result = await wire.ReceiveAsync(life.Token).WaitAsync(TimeSpan.FromMinutes(2), life.Token);
                if (result.ActivityResult?.Accepted != true) throw new OperationCanceledException("Activity declined.");
            }
            finally { EndPeer(peer, life); _handoffSlot.Release(); }
        });
    }
    internal async Task ReceiveActivityAsync(PeerWire wire, ActivityOffer offer, CancellationToken token)
    {
        Require(wire.PeerId, "handoff");
        var activity = new ContinuityActivity(offer.AppId, offer.Kind, (int)offer.SchemaVersion, offer.Title, offer.Text);
        activity.Validate();
        var accepted = await PromptAsync("handoff", wire.PeerId, wire.Remote.Name, "Open a new Notes document: " + activity.Title, token);
        if (accepted)
        {
            lock (_gate)
            {
                token.ThrowIfCancellationRequested(); Require(wire.PeerId, "handoff");
                var root = global::System.IO.Path.Combine(_state.DirectoryPath, "activities");
                Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                SecureState.Check(root, true);
                foreach (var expired in Directory.EnumerateFiles(root, "*.json"))
                    if (File.GetLastWriteTimeUtc(expired) < DateTime.UtcNow.AddDays(-1)) File.Delete(expired);
                if (Directory.EnumerateFiles(root).Take(64).Count() >= 64) throw new IOException("Activity limit.");
                var path = global::System.IO.Path.Combine(root, Guid.NewGuid().ToString("N") + ".json");
                SecureState.WritePrivate(path, JsonSerializer.SerializeToUtf8Bytes(activity, DaisyCoreJsonContext.Default.ContinuityActivity));
                var notes = global::System.IO.Path.GetFullPath(global::System.IO.Path.Combine(AppContext.BaseDirectory, "..", "Notes", "DaisyOS.Notes"));
                if (!File.Exists(notes)) notes = global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "daisyos-notes");
                if (!File.Exists(notes)) notes = "/usr/lib/daisyos/apps/Notes/DaisyOS.Notes";
                if (!File.Exists(notes) || !new SafeProcessLauncher().Launch(notes, ["--continuity-import", path]).Succeeded)
                { File.Delete(path); throw new IOException("Install the DaisyOS Notes handoff provider."); }
            }
        }
        await wire.SendAsync(new() { ActivityResult = new() { Accepted = accepted } }, token);
    }
}
