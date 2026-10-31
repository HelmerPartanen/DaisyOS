using System.Reflection;
using DaisyOS.Continuity;

namespace DaisyOS.Continuity.Tests;

public sealed class SessionRecoveryTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task WakeRestoresOnlyAnUnlockedSessionWithLiveShellAuthority(bool unlocked, bool shellPresent)
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-session-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var state = new SecureState(root);
            state.Settings.Enabled = true;
            await using var daemon = new ContinuityDaemon(state, 47621, null, root, "unix:path=/nonexistent-session-recovery",
                sessionUnlocked: _ => Task.FromResult(unlocked));
            void Set(string name, object? value) => typeof(ContinuityDaemon).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(daemon, value);
            Set("_sessionGranted", true);
            Set("_sessionId", "fixture");
            Set("_shellOwner", shellPresent ? "fixture-shell" : null);
            Set("_sessionReady", true);
            Assert.True(daemon.Snapshot().SessionReady);
            daemon.PrepareForSleep(true);
            Assert.False(daemon.Snapshot().SessionReady);
            daemon.PrepareForSleep(false);
            if (unlocked && shellPresent)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                while (!daemon.Snapshot().SessionReady) await Task.Delay(10, timeout.Token);
            }
            else
            {
                await Task.Delay(100);
                Assert.False(daemon.Snapshot().SessionReady);
            }
            Assert.Empty(daemon.Snapshot().Prompts);
        }
        finally { Directory.Delete(root, true); }
    }
}
