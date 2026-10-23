using DaisyOS.Shell.Controls;
using Xunit;

namespace DaisyOS.Tests;

public sealed class BatteryLevelIconTests
{
    [Theory]
    [InlineData(100, 0)]
    [InlineData(20, 0)]
    [InlineData(19, 1)]
    [InlineData(10, 1)]
    [InlineData(9, 2)]
    [InlineData(0, 2)]
    public void SeverityUsesExactLowAndCriticalThresholds(int percentage, int expected) =>
        Assert.Equal((BatteryLevelSeverity)expected, BatteryLevelIcon.GetSeverity(percentage));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 1.95)]
    [InlineData(50, 9.75)]
    [InlineData(100, 19.5)]
    [InlineData(150, 19.5)]
    public void FillWidthGrowsLeftToRightAndClampsToTheSvgCavity(int percentage, double expected) =>
        Assert.Equal(expected, BatteryLevelIcon.GetFillWidth(percentage), precision: 3);
}
