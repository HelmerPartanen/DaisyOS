using System.Diagnostics;
using DaisyOS.System.Display;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class DisplayScalingTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void DesktopCellsStayInAvaloniaLogicalUnits(double dpi)
    {
        var metrics = new AvaloniaMetricsProvider();

        Assert.Equal(74, metrics.GetCellWidth(dpi));
        Assert.Equal(88, metrics.GetCellHeight(dpi));
    }

    [Fact]
    public void DesktopApplicationEnvironmentPrefersNativeFractionalScaling()
    {
        var start = new ProcessStartInfo();
        start.Environment.Remove("GDK_BACKEND");
        start.Environment["ELECTRON_RUN_AS_NODE"] = "1";
        start.Environment["ELECTRON_NO_ATTACH_CONSOLE"] = "1";

        SafeProcessLauncher.ApplyToolkitScalingEnvironment(start);

        Assert.False(start.Environment.ContainsKey("ELECTRON_RUN_AS_NODE"));
        Assert.False(start.Environment.ContainsKey("ELECTRON_NO_ATTACH_CONSOLE"));
        Assert.Equal("auto", start.Environment["ELECTRON_OZONE_PLATFORM_HINT"]);
        Assert.Equal("PassThrough", start.Environment["QT_SCALE_FACTOR_ROUNDING_POLICY"]);
        Assert.Equal("wayland,x11", start.Environment["GDK_BACKEND"]);
    }
}
