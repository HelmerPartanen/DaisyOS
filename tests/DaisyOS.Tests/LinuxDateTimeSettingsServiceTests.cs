using DaisyOS.System.Processes;
using DaisyOS.System.Time;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxDateTimeSettingsServiceTests
{
    [Fact]
    public async Task StatusUsesSystemTimeZoneAndListsAvailableZones()
    {
        var runner = new ScriptedCommandRunner(
            Succeeded("Timezone=Europe/Helsinki\nNTP=yes\n"),
            Succeeded("Europe/Helsinki\nEurope/London\nAmerica/New_York\n"));
        var service = new LinuxDateTimeSettingsService(runner);

        var status = await service.GetStatusAsync();

        Assert.True(status.IsAvailable);
        Assert.True(status.AutomaticTime);
        Assert.Equal("Europe/Helsinki", status.TimeZoneId);
        Assert.Equal(["America/New_York", "Europe/Helsinki", "Europe/London"], status.AvailableTimeZones);
    }

    [Fact]
    public async Task ChangeCommandsUseArgumentListsAndPlainResults()
    {
        var runner = new ScriptedCommandRunner(Succeeded(), Succeeded(), Succeeded());
        var service = new LinuxDateTimeSettingsService(runner);

        var zone = await service.SetTimeZoneAsync("Europe/Helsinki");
        var automatic = await service.SetAutomaticTimeAsync(false);
        var manual = await service.SetLocalTimeAsync(new DateTime(2026, 7, 24, 15, 53, 0));

        Assert.True(zone.Succeeded);
        Assert.True(automatic.Succeeded);
        Assert.True(manual.Succeeded);
        Assert.Collection(
            runner.Calls,
            call => Assert.Equal(["set-timezone", "Europe/Helsinki"], call.Arguments),
            call => Assert.Equal(["set-ntp", "false"], call.Arguments),
            call => Assert.Equal(["set-time", "2026-07-24 15:53:00"], call.Arguments));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/etc/passwd")]
    [InlineData("Europe/../passwd")]
    public async Task InvalidTimeZoneIsRejectedBeforeRunningACommand(string timeZone)
    {
        var runner = new ScriptedCommandRunner();
        var service = new LinuxDateTimeSettingsService(runner);

        var result = await service.SetTimeZoneAsync(timeZone);

        Assert.False(result.Succeeded);
        Assert.Empty(runner.Calls);
    }

    private static CommandResult Succeeded(string output = "") =>
        new(0, output, string.Empty, TimedOut: false);
}
