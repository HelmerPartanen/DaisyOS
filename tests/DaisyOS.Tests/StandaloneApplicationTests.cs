using DaisyOS.Shell.Services;
using Avalonia;
using Xunit;

namespace DaisyOS.Tests;

public sealed class StandaloneApplicationTests
{
    [Theory]
    [InlineData(1280, 720, 760, 560, 1536, 864, 1.25, 1024, 576)]
    [InlineData(1040, 680, 740, 480, 1536, 864, 1.25, 832, 544)]
    [InlineData(960, 640, 720, 480, 1536, 864, 1.0, 960, 640)]
    public void NativeAppStartupSizeCompensatesForFractionalDisplayScale(
        double width, double height, double minWidth, double minHeight,
        double workWidth, double workHeight, double scaling, double expectedWidth, double expectedHeight)
    {
        var result = NativeAppWindowSizing.Calculate(width, height, minWidth, minHeight,
            new Size(workWidth, workHeight), scaling);

        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
    }

    [Fact]
    public void NativeAppStartupSizeNeverExceedsLaptopWorkArea()
    {
        var result = NativeAppWindowSizing.Calculate(1280, 720, 760, 560,
            new Size(1024, 640), 1.25);

        Assert.True(result.Width <= 1024 * 0.92);
        Assert.True(result.Height <= 640 * 0.92);
        Assert.True(result.MinWidth <= result.Width);
        Assert.True(result.MinHeight <= result.Height);
    }

    [Fact]
    public void NewDesktopPinsEveryNativeApplicationExactlyOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), "daisy-state-" + Guid.NewGuid().ToString("N"), "state.json");
        try
        {
            var state = new ShellSessionState(path);
            Assert.Equal(ShellSessionState.DefaultDockOrder, state.DockOrder);
            Assert.Equal(ShellSessionState.DefaultDockOrder.Count,
                ShellSessionState.DefaultDockOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            state.SetDockOrder([]);
            Assert.Empty(new ShellSessionState(path).DockOrder);
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Theory]
    [InlineData("settings", "Asetukset")]
    [InlineData("files", "Tiedostot")]
    [InlineData("notes", "Muistiinpanot")]
    [InlineData("diskmanager", "Disk Manager")]
    public void TaskbarResolvesStandaloneAppsByStableIdInEveryLanguage(string shortId, string name)
    {
        var entry = new DaisyOS.Core.Models.AppEntry("org.daisyos." + shortId + ".desktop", name, "", "org.daisyos." + shortId,
            false, DaisyOS.Core.Models.AppLaunchKind.DesktopEntry, "/apps/DaisyOS." + shortId);
        var other = entry with { Id = "another-app.desktop", Name = shortId, Icon = "another-app", LaunchTarget = "/apps/another-app" };
        var lookup = DaisyOS.Shell.App.BuildAppLookupIndex([other, entry]);
        Assert.Same(entry, lookup[shortId]);
        Assert.Equal(entry.LaunchTarget, lookup[shortId].LaunchTarget);
        Assert.Same(entry, lookup[entry.Id]);
    }

    [Fact]
    public async Task PipeRepliesOnlyAfterOperationCompletesAndRecoversFromFailure()
    {
        var name = "daisyos-test-" + Guid.NewGuid().ToString("N");
        using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ApplicationPipe.ServeAsync(name, async request =>
        {
            if (request[0] == "fail") throw new InvalidOperationException("Expected failure.");
            entered.SetResult();
            await finish.Task;
            Assert.Equal(["preference", "path", "folder with spaces/日本語"], request);
            return "ok";
        }, stop.Token);
        try
        {
            Assert.Equal("Expected failure.", await ApplicationPipe.RequestAsync(name, ["fail"]));
            var pending = ApplicationPipe.RequestAsync(name, ["preference", "path", "folder with spaces/日本語"]);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(pending.IsCompleted);
            finish.SetResult();
            Assert.Equal("ok", await pending);
        }
        finally { finish.TrySetResult(); stop.Cancel(); await server; }
    }

    [Fact]
    public void ApplicationStateForwardsWritesWithoutOverwritingDesktopState()
    {
        var path = Path.Combine(Path.GetTempPath(), "daisy-state-" + Guid.NewGuid().ToString("N"), "state.json");
        try
        {
            var desktop = new ShellSessionState(path);
            desktop.SetDockOrder(["org.daisyos.files"]);
            var application = new ShellSessionState(path);
            string? operation = null;
            application.RemoteWrite = (name, values) =>
            {
                operation = name;
                if (name == "preference") desktop.SetPreference(values[0], values[1]);
                if (name == "show-date") desktop.ShowDateInSystemBar = bool.Parse(values[0]);
            };
            desktop.SetDockOrder(["org.daisyos.files", "org.daisyos.notes"]);
            application.SetPreference("clock.format", "24h");
            application.ShowDateInSystemBar = true;
            application.Reload();
            Assert.Equal("show-date", operation);
            Assert.True(application.ShowDateInSystemBar);
            Assert.Equal("24h", application.GetPreference("clock.format"));
            Assert.Equal(["org.daisyos.files", "org.daisyos.notes"], new ShellSessionState(path).DockOrder);
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
}
