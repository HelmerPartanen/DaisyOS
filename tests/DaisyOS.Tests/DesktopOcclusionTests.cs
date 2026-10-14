using DaisyOS.Shell.Services.Compositor;
using Xunit;

namespace DaisyOS.Tests;

public sealed class DesktopOcclusionTests
{
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    public void ThirdPartyFullscreenDoesNotRequireKeyboardFocus(bool fullscreen, bool minimized, bool current, bool expected)
    {
        var window = new CompositorWindowRecord("window", "google-chrome", "Browser", null, null, null,
            1, false, minimized, false, fullscreen, true, IsOnCurrentWorkspace: current);
        Assert.Equal(expected, DesktopOcclusion.HasFullscreen([window]));
        Assert.Equal(expected, DesktopOcclusion.IsCovered([window]));
    }

    [Fact]
    public void MaximizedThirdPartyWindowCoversDesktopWithoutBecomingFullscreen()
    {
        var window = new CompositorWindowRecord("window", "code", "Code", null, null, null, 1, true, false, true, false, true);
        Assert.True(DesktopOcclusion.IsCovered([window]));
        Assert.False(DesktopOcclusion.HasFullscreen([window]));
    }
}
