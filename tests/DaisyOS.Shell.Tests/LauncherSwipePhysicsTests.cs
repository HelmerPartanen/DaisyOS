using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Compositor;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class LauncherSwipePhysicsTests
{
    [Fact]
    public void SingleSampleAndBatchedFlicksCommitWithoutALongDrag()
    {
        var tracker = new SwipeVelocityTracker();
        tracker.Add(-32, 1000);
        Assert.True(LauncherSwipePhysics.ShouldCommit(-32, tracker.VelocityAt(1010), 1100));
        tracker.Add(-8, 1000); // Same timestamp must accumulate, not erase velocity.
        Assert.True(LauncherSwipePhysics.ShouldCommit(-40, tracker.VelocityAt(1010), 1100));
        Assert.False(LauncherSwipePhysics.ShouldCommit(-40, tracker.VelocityAt(1200), 1100));
    }

    [Fact]
    public void SamplingRateDoesNotChangeFingerVelocityAndReversalWins()
    {
        var fast = new SwipeVelocityTracker();
        var slow = new SwipeVelocityTracker();
        fast.Add(-16, 1000);
        slow.Add(-16, 1000);
        for (var time = 1008; time <= 1080; time += 8) fast.Add(-8, time);
        for (var time = 1016; time <= 1080; time += 16) slow.Add(-16, time);
        Assert.Equal(-1, fast.VelocityAt(1085), 5);
        Assert.Equal(-1, slow.VelocityAt(1085), 5);
        fast.Add(12, 1088);
        Assert.True(fast.VelocityAt(1090) > 0);
        Assert.False(LauncherSwipePhysics.ShouldCommit(-84, fast.VelocityAt(1090), 1100));
    }

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
