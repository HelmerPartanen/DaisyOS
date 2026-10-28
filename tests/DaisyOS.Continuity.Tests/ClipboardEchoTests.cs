using System.Text;
using DaisyOS.Continuity;

namespace DaisyOS.Continuity.Tests;

public sealed class ClipboardEchoTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact]
    public void DelayedAndRepeatedInjectedChangesNeverBecomeLocalUpdates()
    {
        var bridge = new ClipboardBridge();
        var first = Encoding.UTF8.GetBytes("remote first");
        var second = Encoding.UTF8.GetBytes("remote second 🌼");
        bridge.RememberInjection(first); bridge.RememberInjection(second);
        Assert.True(bridge.IsEcho(first)); // Old helper event arrives after newer injection.
        Assert.True(bridge.IsEcho(second));
        Assert.False(bridge.IsEcho(Encoding.UTF8.GetBytes("a genuine local copy")));
        Assert.True(bridge.IsEcho(first)); // A local event must not erase pending injection tags.
        Assert.True(bridge.IsEcho(first));
    }
    [Fact]
    public void InjectionMetadataExpiresWithoutWallClockSleeps()
    {
        var clock = new Clock(); var bridge = new ClipboardBridge(clock);
        var data = Encoding.UTF8.GetBytes("temporary metadata");
        bridge.RememberInjection(data); Assert.True(bridge.IsEcho(data));
        clock.Now += TimeSpan.FromMinutes(5);
        Assert.False(bridge.IsEcho(data)); Assert.Equal(0, bridge.RetainedInjectionCount);
    }
    [Fact]
    public void InjectionTagsHaveABoundedMemoryBudget()
    {
        var clock = new Clock(); var bridge = new ClipboardBridge(clock);
        for (var i = 0; i < 1000; i++)
        {
            clock.Now += TimeSpan.FromMilliseconds(1);
            bridge.RememberInjection(Encoding.UTF8.GetBytes("remote " + i));
        }
        Assert.InRange(bridge.RetainedInjectionCount, 1, 256);
        Assert.True(bridge.IsEcho(Encoding.UTF8.GetBytes("remote 999")));
    }
}
