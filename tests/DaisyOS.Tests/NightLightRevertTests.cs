using DaisyOS.System.Display;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class NightLightRevertTests
{
    [Fact]
    public async Task CapturesAndRestoresScheduledModeTemperatureAndEnabledState()
    {
        var runner = new ScriptedCommandRunner(CommandResult.Success("true\n"), CommandResult.Success("2\n"), CommandResult.Success("3500\n"),
            CommandResult.Success(), CommandResult.Success(), CommandResult.Success(), CommandResult.Success(), CommandResult.Success());
        var service = new LinuxNightLightService(runner);
        var previous = await service.CaptureConfigurationAsync();
        Assert.Equal(new NightLightConfiguration("true", "2", "3500"), previous);
        Assert.True(await service.RestoreConfigurationAsync(previous!));
        var writes = runner.Calls.Where(c => c.FileName == "kwriteconfig6").ToArray();
        Assert.Equal(3, writes.Length);
        Assert.Contains("2", writes[0].Arguments);
        Assert.Contains("3500", writes[1].Arguments);
        Assert.Contains("true", writes[2].Arguments);
        Assert.Contains(runner.Calls, c => c.Arguments.Contains("reconfigure"));
        Assert.Contains(runner.Calls, c => c.Arguments.Contains("stopPreview"));
    }

    [Fact]
    public async Task FailedSnapshotCannotBeUsedAsARevertBaseline()
    {
        var service = new LinuxNightLightService(new ScriptedCommandRunner(CommandResult.Failed()));
        Assert.Null(await service.CaptureConfigurationAsync());
    }
}
