using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Shell.Services;
using DaisyOS.System.Gaming;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class GamingModeAutomationTests
{
    [AvaloniaFact]
    public async Task NativeRegistrationAndCleanupRunOutsideTheInputThread()
    {
        var root = Directory.CreateTempSubdirectory("daisy-gaming-worker-");
        var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "100"));
            File.WriteAllText(Path.Combine(root.FullName, "100", "cmdline"), "reaper\0SteamLaunch\0AppId=730\0");
            var state = new ShellSessionState(Path.Combine(root.FullName, "state.json"));
            var controller = new GameModeController("/nonexistent/gamemoderun",
                _ => { registered.TrySetResult(Dispatcher.UIThread.CheckAccess()); return 0; },
                _ => { released.TrySetResult(Dispatcher.UIThread.CheckAccess()); return 0; });
            using var automation = new GamingModeAutomation(state, () => false, controller,
                new GameProcessDetector(root.FullName));
            automation.Start();
            state.GamingMode = true;
            Assert.False(await registered.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            automation.Dispose();
            Assert.False(await released.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { root.Delete(true); }
    }
}
