using DaisyOS.System.Gaming;
using Xunit;

namespace DaisyOS.Tests;

public sealed class AutomaticGameModeTests
{
    [Theory]
    [InlineData("/home/u/.local/share/Steam/ubuntu12_32/reaper\0SteamLaunch\0AppId=730\0--\0cs2.sh\0", true)]
    [InlineData("reaper\0SteamLaunch\0AppId=0\0", false)]
    [InlineData("/usr/bin/steam\0-silent\0", false)]
    [InlineData("/usr/bin/python3\0/usr/bin/lutris-wrapper\0Game\0", true)]
    [InlineData("/usr/bin/mpv\0--fullscreen\0movie.mkv\0", false)]
    [InlineData("", false)]
    public void RecognisesGameLaunchers(string cmdline, bool expected) =>
        Assert.Equal(expected, GameProcessDetector.IsGameLauncherCommand(cmdline));

    [Fact]
    public void DetectorScansProcTree()
    {
        var proc = Directory.CreateTempSubdirectory("daisy-proc-");
        try
        {
            Directory.CreateDirectory(Path.Combine(proc.FullName, "self"));
            WriteCmdline(proc.FullName, "100", "/usr/bin/bash\0");
            var detector = new GameProcessDetector(proc.FullName);
            Assert.False(detector.IsGameRunning());

            WriteCmdline(proc.FullName, "200", "reaper\0SteamLaunch\0AppId=730\0");
            Assert.True(detector.IsGameRunning());
        }
        finally { proc.Delete(recursive: true); }
    }

    [Fact]
    public void PolicyFollowsGameAndOnlyUndoesItsOwnChange()
    {
        var policy = new AutomaticGamingModePolicy();
        Assert.True(policy.Evaluate(gameRunning: true, automaticEnabled: true, gamingModeOn: false));
        Assert.Null(policy.Evaluate(true, true, true));
        Assert.False(policy.Evaluate(false, true, true));

        // Manually enabled Gaming Mode is never switched off by the policy.
        Assert.Null(policy.Evaluate(true, true, true));
        Assert.Null(policy.Evaluate(false, true, true));
    }

    [Fact]
    public void PolicyRespectsUserDismissalUntilGameExits()
    {
        var policy = new AutomaticGamingModePolicy();
        Assert.True(policy.Evaluate(true, true, false));
        Assert.Null(policy.Evaluate(true, true, false)); // user switched it off mid-game
        Assert.Null(policy.Evaluate(true, true, false));
        Assert.Null(policy.Evaluate(false, true, false));
        Assert.True(policy.Evaluate(true, true, false)); // next game
    }

    [Fact]
    public void PolicyDoesNothingWhenAutomaticIsOff()
    {
        var policy = new AutomaticGamingModePolicy();
        Assert.Null(policy.Evaluate(true, false, false));
    }

    [Fact]
    public void ControllerHoldsAndReleasesClient()
    {
        var script = Path.Combine(Path.GetTempPath(), $"fake-gamemoderun-{Guid.NewGuid():N}");
        File.WriteAllText(script, "#!/bin/sh\nexec \"$@\"\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            using var controller = new GameModeController(script);
            controller.SetActive(true);
            Assert.True(controller.IsActive);
            controller.SetActive(false);
            Assert.False(controller.IsActive);
        }
        finally { File.Delete(script); }
    }

    [Fact]
    public void ControllerIsInertWithoutGameMode()
    {
        using var controller = new GameModeController("/nonexistent/gamemoderun");
        controller.SetActive(true);
        Assert.False(controller.IsActive);
    }

    private static void WriteCmdline(string root, string pid, string cmdline)
    {
        var dir = Path.Combine(root, pid);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "cmdline"), cmdline);
    }
}
