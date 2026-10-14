using DaisyOS.Shell.Services.Compositor;
using Xunit;

namespace DaisyOS.Tests;

public sealed class KWinWindowSnapshotParserTests
{
    [Fact]
    public void ParsesAuthoritativeKWinWindowState()
    {
        const string payload = """
            {"windows":[{"id":"a1b2","appId":"org.mozilla.firefox","title":"Firefox","desktopFileId":"firefox.desktop","iconName":"firefox","outputName":"HDMI-A-1","workspace":2,"isActive":true,"isMinimized":false,"isMaximized":true,"isFullScreen":false,"canClose":true,"daisyAppId":"notes","processId":4242,"isUnresponsive":true}]}
            """;

        var parsed = KWinWindowSnapshotParser.TryParse(payload, out var windows);

        Assert.True(parsed);
        var window = Assert.Single(windows);
        Assert.Equal("a1b2", window.Id);
        Assert.Equal("org.mozilla.firefox", window.AppId);
        Assert.Equal("firefox.desktop", window.DesktopFileId);
        Assert.Equal("HDMI-A-1", window.OutputName);
        Assert.True(window.IsActive);
        Assert.True(window.IsMaximized);
        Assert.Equal("notes", window.DaisyAppId);
        Assert.Equal(4242, window.ProcessId);
        Assert.True(window.IsUnresponsive);
    }

    [Fact]
    public void RejectsMalformedOrIncompletePayloads()
    {
        Assert.False(KWinWindowSnapshotParser.TryParse("not-json", out _));
        Assert.False(KWinWindowSnapshotParser.TryParse("{}", out _));

        Assert.True(KWinWindowSnapshotParser.TryParse("{\"windows\":[{\"id\":\"only-id\"}]}", out var windows));
        Assert.Empty(windows);
    }

    [Fact]
    public void DefaultsMissingOptionalStateSafely()
    {
        Assert.True(KWinWindowSnapshotParser.TryParse("{\"windows\":[{\"id\":\"123\",\"appId\":\"foot\"}]}", out var windows));

        var window = Assert.Single(windows);
        Assert.Equal("foot", window.Title);
        Assert.False(window.IsActive);
        Assert.False(window.IsMinimized);
        Assert.True(window.CanClose);
        Assert.True(window.IsOnCurrentWorkspace);
    }

    [Fact]
    public void FullscreenCoverageIncludesClientsThatDoNotAppearInTaskbar()
    {
        Assert.True(KWinWindowSnapshotParser.TryParse("{\"windows\":[],\"desktopOccluded\":true,\"fullscreenActive\":true}",
            out var windows, out var covered, out var fullscreen));
        Assert.Empty(windows);
        Assert.True(covered);
        Assert.True(fullscreen);
    }

    [Fact]
    public void OtherWorkspaceDoesNotPauseWallpaper()
    {
        Assert.True(KWinWindowSnapshotParser.TryParse("{\"windows\":[{\"id\":\"other\",\"appId\":\"browser\",\"isFullScreen\":true,\"isOnCurrentWorkspace\":false}]}",
            out _, out var covered, out var fullscreen));
        Assert.False(covered);
        Assert.False(fullscreen);
    }
}
