using DaisyOS.Shell.Services.Compositor;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class LauncherPinchDispatcherTests
{
    [Fact]
    public void BurstsDeliverLatestPositionWithoutQueuingOneCallbackPerSample()
    {
        var scheduled = new Queue<Action>();
        var delivered = new List<LauncherPinchUpdate>();
        var dispatcher = new LauncherPinchDispatcher(scheduled.Enqueue, delivered.Add);
        for (var i = 1; i <= 1000; i++) dispatcher.Push(new(true, i / 1000d, false, false));
        Assert.Single(scheduled);
        scheduled.Dequeue()();
        Assert.Equal(new LauncherPinchUpdate(true, 1, false, false), Assert.Single(delivered));
    }

    [Fact]
    public void ReleaseAndNextGestureStayInOrderEvenWhenUiIsBusy()
    {
        var scheduled = new Queue<Action>();
        var delivered = new List<LauncherPinchUpdate>();
        var dispatcher = new LauncherPinchDispatcher(scheduled.Enqueue, delivered.Add);
        LauncherPinchUpdate[] updates = [new(true, 0.2, false, false), new(true, 0.7, true, true),
            new(false, 0.3, false, false), new(false, 0.4, true, false)];
        foreach (var update in updates) dispatcher.Push(update);
        Assert.Single(scheduled);
        scheduled.Dequeue()();
        Assert.Equal(updates, delivered);
        dispatcher.Push(new(true, 0.5, false, false));
        Assert.Single(scheduled);
        scheduled.Dequeue()();
        Assert.Equal(5, delivered.Count);
    }
}
