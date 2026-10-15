using DaisyOS.System.Diagnostics;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class NvidiaGpuMetricsTests
{
    [Fact]
    public void Reuses_initialization_and_releases_once()
    {
        var starts = 0;
        var stops = 0;
        var sampler = new NvidiaGpuMetrics(() => { starts++; return 0; }, () => (0, 2u),
            index => (0, (nint)index), device => (0, device == 0 ? 20u : 85u), () => stops++, () => 0);
        Assert.Equal(85d, sampler.ReadUsage());
        Assert.Equal(85d, sampler.ReadUsage());
        Assert.Equal(1, starts);
        sampler.Dispose(); sampler.Dispose();
        Assert.Equal(1, stops);
        Assert.Null(sampler.ReadUsage());
    }

    [Fact]
    public void Missing_driver_backs_off_then_recovers()
    {
        long clock = 0;
        var attempts = 0;
        using var sampler = new NvidiaGpuMetrics(() => { if (++attempts == 1) throw new DllNotFoundException(); return 0; },
            () => (0, 1u), _ => (0, (nint)1), _ => (0, 50u), () => { }, () => clock);
        Assert.Null(sampler.ReadUsage());
        clock = 29_999;
        Assert.Null(sampler.ReadUsage());
        Assert.Equal(1, attempts);
        clock = 30_000;
        Assert.Equal(50d, sampler.ReadUsage());
    }

    [Fact]
    public void Lost_gpu_releases_session_and_recovers_after_backoff()
    {
        long clock = 0;
        var stops = 0;
        var lost = true;
        using var sampler = new NvidiaGpuMetrics(() => 0, () => (0, 1u), _ => (0, (nint)1),
            _ => lost ? (15, 0u) : (0, 42u), () => stops++, () => clock);
        Assert.Null(sampler.ReadUsage());
        Assert.Equal(1, stops);
        lost = false;
        Assert.Null(sampler.ReadUsage());
        clock = 30_000;
        Assert.Equal(42d, sampler.ReadUsage());
    }
}
