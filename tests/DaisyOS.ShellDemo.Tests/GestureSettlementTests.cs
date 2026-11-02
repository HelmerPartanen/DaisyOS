using DaisyOS.Shell.Services.Motion;
using DaisyOS.Shell.Services.Compositor;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class GestureSettlementTests
{
    [Theory]
    [InlineData(0, 1, 0.002)]
    [InlineData(1, 0, -0.002)]
    [InlineData(0.5, 1, -0.001)]
    public void CarriesReleaseVelocityAndSettlesExactly(double from, double to, double velocity)
    {
        const double duration = 200;
        const double dt = 0.001;
        var derivative = (GestureSettlement.Position(from, to, velocity, dt, duration) - from) / dt;
        Assert.Equal(velocity, derivative, 6);
        Assert.Equal(to, GestureSettlement.Position(from, to, velocity, duration, duration));
        Assert.Equal(to, GestureSettlement.Position(from, to, velocity, duration * 2, duration));
        var endVelocity = (to - GestureSettlement.Position(from, to, velocity, duration - dt, duration)) / dt;
        Assert.Equal(0, endVelocity, 6);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(180)]
    public void SettlementHasNoFrameRateDependentState(int hz)
    {
        double position = 0;
        for (var frame = 0; frame <= hz; frame++)
            position = GestureSettlement.Position(0, 1, 0.004, frame * 1000d / hz, 200);
        Assert.Equal(1, position);
        Assert.Equal(0, GestureSettlement.Duration(0, 0));
        Assert.True(GestureSettlement.Duration(1, 0.01) < GestureSettlement.Duration(1, 0.002));
    }

    [Fact]
    public void PinchVelocitySurvivesTheCompositorContractAndLegacyMessagesRemainValid()
    {
        Assert.True(LauncherPinchUpdate.TryParse("""{"opening":false,"progress":0.4,"finished":true,"commit":true,"velocity":0.002}""", out var update));
        Assert.Equal(0.002, update!.Velocity);
        Assert.True(LauncherPinchUpdate.TryParse("""{"opening":true,"progress":0.4,"finished":false,"commit":false}""", out update));
        Assert.Equal(0, update!.Velocity);
    }
}
