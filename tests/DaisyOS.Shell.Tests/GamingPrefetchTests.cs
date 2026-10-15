using DaisyOS.Shell.Services.Diagnostics;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class GamingPrefetchTests
{
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
