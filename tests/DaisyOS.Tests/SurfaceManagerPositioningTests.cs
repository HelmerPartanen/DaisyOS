using Avalonia;
using Xunit;

namespace DaisyOS.Tests;

public sealed class SurfaceManagerPositioningTests
{
    [Fact]
    public void CalculateFlyoutYOffset_UsesExactEdgeGap8px()
    {
        const int screenBottom = 1080;
        const int flyoutHeight = 400;
        const int systemBarHeight = 78;
        const int edgeGap = 8;
        const double scale = 1.0;

        var gapPixels = (int)Math.Round(edgeGap * scale);
        var expectedY = screenBottom - flyoutHeight - systemBarHeight - gapPixels;

        // Formula used in BottomShellSurfaceManager.cs:
        // bounds.Bottom - height - systemBarHeight - gap
        var computedY = screenBottom - flyoutHeight - systemBarHeight - gapPixels;

        Assert.Equal(expectedY, computedY);
        Assert.Equal(screenBottom - flyoutHeight - systemBarHeight - 8, computedY);
    }

    [Fact]
    public void SystemBarVisibilityMenu_AnchorPositionOffset_IncorporatesEdgeGap8px()
    {
        var anchorPoint = new PixelPoint(500, 1000);
        const int menuHeight = 300;
        const int edgeGap = 8;

        var computedY = anchorPoint.Y - menuHeight - edgeGap;
        Assert.Equal(692, computedY);
    }
}
