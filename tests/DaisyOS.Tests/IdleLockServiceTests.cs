using DaisyOS.Shell.Services.Idle;
using Xunit;

namespace DaisyOS.Tests;

public sealed class IdleLockServiceTests
{
    [Theory]
    [InlineData("5 minutes", 300u)]
    [InlineData("10 minutes", 600u)]
    [InlineData("15 minutes", 900u)]
    [InlineData("Never", 0u)]
    [InlineData("", 600u)]
    [InlineData(null, 600u)]
    [InlineData("unknown", 600u)]
    public void ParseTimeoutSeconds_MapsCorrectly(string? input, uint expectedSeconds)
    {
        var actual = IdleLockService.ParseTimeoutSeconds(input);
        Assert.Equal(expectedSeconds, actual);
    }

    [Fact]
    public void UpdateConfiguration_UpdatesPropertiesCorrectly()
    {
        using var service = new IdleLockService(() => { });

        service.UpdateConfiguration("5 minutes", false);
        Assert.Equal(300u, service.TimeoutSeconds);
        Assert.False(service.LockOnSleep);
        Assert.False(service.IsRunning);

        service.UpdateConfiguration("15 minutes", true);
        Assert.Equal(900u, service.TimeoutSeconds);
        Assert.True(service.LockOnSleep);

        service.UpdateConfiguration("Never", true);
        Assert.Equal(0u, service.TimeoutSeconds);
        Assert.True(service.LockOnSleep);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public void Dispose_CleansUpWithoutThrowing()
    {
        var locked = false;
        var service = new IdleLockService(() => locked = true);
        service.UpdateConfiguration("10 minutes", true);
        service.Dispose();

        // Calling Dispose again should be safe
        service.Dispose();
        Assert.False(service.IsRunning);
        Assert.False(locked);
    }
}
