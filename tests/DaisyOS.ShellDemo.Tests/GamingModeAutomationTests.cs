using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Shell.Services;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class GamingModeAutomationTests
{
    [AvaloniaFact]
    public async Task ConfigurationRunsOutsideTheInputThreadAndManualPreferenceTracksChanges()
    {
        var root = Directory.CreateTempSubdirectory("daisy-gaming-worker-");
        var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var state = new ShellSessionState(Path.Combine(root.FullName, "state.json"));
            using var automation = new GamingModeAutomation(state, () =>
            {
                configured.TrySetResult(Dispatcher.UIThread.CheckAccess());
                // Stop before contacting the real session gaming daemon.
                throw new IOException("Gaming configuration unavailable in this test.");
            });
            automation.Start();
            Assert.Equal("false", state.GetPreference("gaming.manual-enabled"));
            state.GamingMode = true;
            Assert.Equal("true", state.GetPreference("gaming.manual-enabled"));
            Assert.False(await configured.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            automation.Dispose();
            state.GamingMode = false;
            Assert.Equal("true", state.GetPreference("gaming.manual-enabled"));
        }
        finally { root.Delete(true); }
    }
}
