using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Compositor;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class LauncherSwipePhysicsTests
{
    [Theory]
    [InlineData(-220, 0, true)]
    [InlineData(220, 0, true)]
    [InlineData(-40, -1, true)]
    [InlineData(40, 1, true)]
    [InlineData(-40, -0.1, false)]
    [InlineData(-12, -2, false)]
    [InlineData(-220, 0.8, false)]
    [InlineData(220, -0.8, false)]
    public void DistanceFlickAndReversalChoosePredictablePage(double offset, double velocity, bool expected)
    {
        Assert.Equal(expected, LauncherSwipePhysics.ShouldCommit(offset, velocity, 1000));
    }

    [Fact]
    public void InvalidMotionCannotCommit()
    {
        Assert.False(LauncherSwipePhysics.ShouldCommit(double.NaN, 1, 1000));
        Assert.False(LauncherSwipePhysics.ShouldCommit(100, double.PositiveInfinity, 1000));
        Assert.False(LauncherSwipePhysics.ShouldCommit(100, 1, 0));
    }

    [Fact]
    public void PinchMessagesPreserveDirectionCompletionAndClampProgress()
    {
        Assert.True(LauncherPinchUpdate.TryParse("""{"opening":false,"progress":1.2,"finished":true,"commit":false}""", out var update));
        Assert.Equal(new LauncherPinchUpdate(false, 1, true, false), update);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("""{"opening":true,"progress":"bad","finished":false,"commit":false}""")]
    public void MalformedPinchMessagesAreIgnored(string payload)
    {
        Assert.False(LauncherPinchUpdate.TryParse(payload, out _));
    }
}
