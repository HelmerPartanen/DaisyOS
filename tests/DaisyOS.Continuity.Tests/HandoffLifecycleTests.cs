using System.Text.Json;
using DaisyOS.Core.Continuity;
using DaisyOS.Core.Serialization;
using DaisyOS.Continuity;
using Xunit;

namespace DaisyOS.Continuity.Tests;

public sealed class HandoffLifecycleTests
{
    [Fact]
    public async Task LosingSessionBeforeBackgroundWorkStartsDoesNotPermanentlyBlockHandoff()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-handoff-cancel-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var state = new SecureState(Path.Combine(root, "host")); using var peer = new SecureState(Path.Combine(root, "peer"));
            state.Settings.Enabled = true;
            state.Settings.Peers[peer.Id] = new(Convert.ToBase64String(peer.Identity.RawData), "Peer", "127.0.0.1:1");
            var checks = 0; var resumed = false;
            await using var daemon = new ContinuityDaemon(state, 47621,
                () => Volatile.Read(ref resumed) || Interlocked.Increment(ref checks) == 1, root, "unix:path=/nonexistent-handoff-test");
            var activity = JsonSerializer.Serialize(new ContinuityActivity("org.daisyos.notes", "note.text", 1, "Note", "Text"), DaisyCoreJsonContext.Default.ContinuityActivity);
            await daemon.ExecuteAsync(new("handoff", peer.Id, activity), "test-ui");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!daemon.Snapshot().Diagnostic.Contains("permission", StringComparison.OrdinalIgnoreCase)) await Task.Delay(10, timeout.Token);
            Volatile.Write(ref resumed, true);
            // The session loss must release the one-flight slot even when no peer lifetime was created.
            await daemon.ExecuteAsync(new("handoff", peer.Id, activity), "test-ui");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
