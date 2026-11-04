using DaisyOS.Shell.Services.Compositor;
using Xunit;

namespace DaisyOS.Tests;

public sealed class KWinWindowBridgeCoverageTests
{
    [Fact]
    public async Task MovingFullscreenBetweenOutputsUpdatesCoverageWithoutChangingGlobalFlags()
    {
        await using var bridge = new KWinWindowBridge();
        var updates = 0;
        bridge.WindowsChanged += (_, _) => updates++;
        var snapshot = """
        {"windows":[],"desktopOccluded":true,"fullscreenActive":true,
         "outputCoverage":[{"outputName":"HDMI-A-1","covered":true,"fullscreen":true}]}
        """;
        bridge.ApplySerializedSnapshot(snapshot);
        Assert.False(bridge.IsFullscreenOnOutput("eDP-1"));
        bridge.ApplySerializedSnapshot(snapshot.Replace("HDMI-A-1", "eDP-1"));
        Assert.True(bridge.IsFullscreenOnOutput("eDP-1"));
        Assert.False(bridge.IsCoveredOnOutput("HDMI-A-1"));
        Assert.Equal(2, updates);
    }

    [Fact]
    public async Task CoverageUpdatesEvenWhenTaskbarWindowListIsUnchanged()
    {
        await using var bridge = new KWinWindowBridge();
        var updates = 0;
        bridge.WindowsChanged += (_, _) => updates++;
        bridge.ApplySerializedSnapshot("{\"windows\":[],\"desktopOccluded\":true,\"fullscreenActive\":true}");
        Assert.True(bridge.IsDesktopOccluded);
        Assert.True(bridge.HasFullscreenWindow);
        Assert.Empty(bridge.Windows);
        bridge.ApplySerializedSnapshot("{\"windows\":[],\"desktopOccluded\":true,\"fullscreenActive\":true}");
        Assert.Equal(1, updates);
        bridge.ApplySerializedSnapshot("{\"windows\":[],\"desktopOccluded\":false,\"fullscreenActive\":false}");
        Assert.False(bridge.IsDesktopOccluded);
        Assert.False(bridge.HasFullscreenWindow);
        Assert.Equal(2, updates);
    }

    [Fact]
    public async Task PublishedWindowListRemainsStableAcrossSubsequentUpdates()
    {
        await using var bridge = new KWinWindowBridge();
        bridge.ApplySerializedSnapshot("{\"windows\":[{\"id\":\"first\",\"appId\":\"browser\"}]}");
        var first = bridge.Windows;
        bridge.ApplySerializedSnapshot("{\"windows\":[{\"id\":\"second\",\"appId\":\"game\"}]}");
        Assert.Equal("first", Assert.Single(first).Id);
        Assert.Equal("second", Assert.Single(bridge.Windows).Id);
    }
}
