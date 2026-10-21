using DaisyOS.Shell.Services.Diagnostics;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class GamingPrefetchTests
{
    [Theory]
    [InlineData(4096, 600, false)]
    [InlineData(4096, 400, true)]
    [InlineData(8192, 900, true)]
    [InlineData(16384, 1500, true)]
    [InlineData(16384, 2500, false)]
    [InlineData(0, 1000, true)]
    public void MemoryPressureKeepsHeadroomForTheMachineSize(long totalMiB, long availableMiB, bool expected)
    {
        Assert.Equal(expected, RecentAppPrefetcher.HasMemoryPressure(totalMiB * 1024, availableMiB * 1024));
    }

    [Fact]
    public void Input_does_not_resume_prefetch_while_gaming_and_exit_restores_it()
    {
        using var prefetch = new RecentAppPrefetcher();
        prefetch.SetSuspended(true);
        prefetch.Interaction();
        Assert.True(prefetch.IsSuspended);
        prefetch.SetSuspended(false);
        Assert.False(prefetch.IsSuspended);
        prefetch.Dispose();
        prefetch.SetSuspended(true);
        prefetch.Interaction();
    }
}
