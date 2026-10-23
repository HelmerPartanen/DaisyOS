using System.Collections.Generic;
using DaisyOS.Shell.Services.Idle;
using DaisyOS.Shell.Services.Diagnostics;
using Xunit;

namespace DaisyOS.Tests;

public sealed class PowerIdleServiceTests
{
    [Theory]
    [InlineData("1 minute", 60u)]
    [InlineData("5 minutes", 300u)]
    [InlineData("10 minutes", 600u)]
    [InlineData("1 hour", 3600u)]
    [InlineData("2 hours", 7200u)]
    [InlineData("Never", 0u)]
    [InlineData("", 600u)]
    [InlineData(null, 600u)]
    [InlineData("unknown", 600u)]
    public void ParseSeconds_MapsCorrectly(string? input, uint expectedSeconds)
    {
        Assert.Equal(expectedSeconds, PowerPreferences.ParseSeconds(input, 600));
    }

    [Fact]
    public void EveryOfferedOption_Parses()
    {
        foreach (var option in PowerPreferences.LockTimeoutOptions)
            Assert.NotEqual(uint.MaxValue, PowerPreferences.ParseSeconds(option, uint.MaxValue));
        foreach (var option in PowerPreferences.ScreenOffTimeoutOptions)
            Assert.NotEqual(uint.MaxValue, PowerPreferences.ParseSeconds(option, uint.MaxValue));
        foreach (var option in PowerPreferences.SuspendTimeoutOptions)
            Assert.NotEqual(uint.MaxValue, PowerPreferences.ParseSeconds(option, uint.MaxValue));
    }

    [Fact]
    public void FromPreferences_UsesDefaults_WhenNothingIsStored()
    {
        var config = PowerIdleConfiguration.FromPreferences((_, fallback) => fallback);
        Assert.Equal(PowerIdleConfiguration.Default, config);
    }

    [Fact]
    public void FromPreferences_HonorsLegacyLockTimeout_UntilNewKeyIsSet()
    {
        var prefs = new Dictionary<string, string> { [PowerPreferences.LegacyScreenTimeoutKey] = "Never" };
        string Get(string key, string fallback) => prefs.TryGetValue(key, out var v) ? v : fallback;

        Assert.Equal(0u, PowerIdleConfiguration.FromPreferences(Get).LockSeconds);

        prefs[PowerPreferences.LockTimeoutKey] = "30 minutes";
        Assert.Equal(1800u, PowerIdleConfiguration.FromPreferences(Get).LockSeconds);
    }

    [Fact]
    public void FromPreferences_LockOnSuspendIsIndependentOfIdleLock()
    {
        var prefs = new Dictionary<string, string>
        {
            [PowerPreferences.LockOnSuspendKey] = "false",
            [PowerPreferences.LockTimeoutKey] = "5 minutes",
        };
        var config = PowerIdleConfiguration.FromPreferences((k, f) => prefs.TryGetValue(k, out var v) ? v : f);
        Assert.False(config.LockOnSuspend);
        Assert.Equal(300u, config.LockSeconds);
    }

    [Fact]
    public void UpdateAndDispose_AreSafe_WithoutCompositor()
    {
        var locked = false;
        var service = new PowerIdleService(() => locked = true, () => { }, () => { }, () => { },
            isOnBattery: () => false);
        service.UpdateConfiguration(new PowerIdleConfiguration(60, 60, 60, 60, true));
        service.PauseLockMonitoring();
        service.ResumeLockMonitoring();
        service.UpdateConfiguration(new PowerIdleConfiguration(0, 0, 0, 0, false));
        service.Dispose();
        service.Dispose();
        Assert.False(locked);
    }

    [Theory]
    [InlineData(16L * 1024 * 1024, 1L * 1024 * 1024, true)]
    [InlineData(16L * 1024 * 1024, 8L * 1024 * 1024, false)]
    public void MemoryTrimmer_PressureThreshold(long totalKiB, long availableKiB, bool expected)
    {
        Assert.Equal(expected, ShellMemoryTrimmer.IsUnderPressure(totalKiB, availableKiB));
    }
}
