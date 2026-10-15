using DaisyOS.Shell.Views.Components.Gaming;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class GamingTelemetryTests
{
    [Theory]
    [InlineData(null, "/run/user/1000", null, "/home/test", "/run/user/1000/daisyos/gaming-telemetry")]
    [InlineData("/custom", "/run/user/1000", null, "/home/test", "/custom")]
    [InlineData(null, null, "/cache", "/home/test", "/cache/DaisyOS/gaming-telemetry")]
    [InlineData(null, null, null, "/home/test", "/home/test/.cache/DaisyOS/gaming-telemetry")]
    public void Finds_launcher_telemetry_without_shell_environment_setup(string? configured, string? runtime, string? cache, string home, string expected) =>
        Assert.Equal(expected, GamingOverlay.TelemetryDirectory(configured, runtime, cache, home));

    [Theory]
    [InlineData("fps,gpu_load\n120.5,98\n144,90\n", 144d, 90d)]
    [InlineData("metadata\nvalues\nfps,cpu_load,gpu_load\n60,30,42\n", 60d, 42d)]
    [InlineData("fps,gpu_load\nNaN,Infinity\n", null, null)]
    [InlineData("fps,gpu_load\n-1,-2\n", null, null)]
    [InlineData("unrelated,data\n60,90\n", null, null)]
    [InlineData("fps,gpu_load\n120,95\npartial\n", 120d, 95d)]
    public void Reads_latest_complete_game_sample_without_inventing_missing_values(string csv, double? fps, double? gpu)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, csv);
            var sample = GamingOverlay.ReadMangoHudLog(path);
            Assert.Equal(fps, sample.Fps);
            Assert.Equal(gpu, sample.Gpu);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reads_recent_tail_of_large_game_logs()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "fps,gpu_load\n" + string.Concat(Enumerable.Repeat("60,50\n", 10000)) + "180,99\n");
            Assert.Equal((180d, 99d), GamingOverlay.ReadMangoHudLog(path));
        }
        finally { File.Delete(path); }
    }
}
