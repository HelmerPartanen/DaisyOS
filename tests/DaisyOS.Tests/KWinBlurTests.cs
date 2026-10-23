using Avalonia;
using DaisyOS.Shell.Services.Compositor;
using Xunit;

namespace DaisyOS.Tests;

public sealed class KWinBlurTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(18)]
    [InlineData(32)]
    public void RoundedMaskStaysBeneathAntialiasedCornerPixels(int radius)
    {
        var regions = new List<Avalonia.Wayland.WaylandBlurRect>();
        KWinBlur.AddRoundedRegion(regions, default, new Size(120, 100), radius);
        Assert.NotEmpty(regions);
        Assert.All(regions, region =>
        {
            Assert.True(region.X >= 1 && region.X + region.Width <= 119);
            Assert.True(region.Y >= 1 && region.Y + region.Height <= 99);
        });
        var top = regions.Single(region => region.Y == 1);
        var bottom = regions.Single(region => region.Y == 98);
        Assert.Equal(top.X, bottom.X);
        Assert.Equal(top.Width, bottom.Width);
        Assert.True(top.X > 1);
    }
    [Fact]
    public void PasswordContainerUsesItsOwnCornerRadiusAfterScaling()
    {
        var regions = new List<Avalonia.Wayland.WaylandBlurRect>();
        KWinBlur.AddTransformedRegion(regions, new Size(320, 54),
            new Matrix(1.2, 0, 0, 1.2, 40, 80), 21);
        Assert.Contains(regions, region => region.X == 41 && region.Y == 105 && region.Width == 382 && region.Height == 15);
        Assert.All(regions, region => Assert.True(region.X >= 41 && region.Y >= 81));
    }

    [Fact]
    public void DragAndFlipTransformsMoveAndScaleTheEntireBlurMask()
    {
        var regions = new List<Avalonia.Wayland.WaylandBlurRect>();
        KWinBlur.AddTransformedRegion(regions, new Size(100, 50), new Matrix(1.2, 0, 0, 1.2, 33, 97));
        Assert.Contains(regions, region => region.X == 34 && region.Y == 116 && region.Width == 118 && region.Height == 22);
        Assert.All(regions, region => Assert.True(region.Y >= 98 && region.Y + region.Height <= 156));
        regions.Clear();
        KWinBlur.AddTransformedRegion(regions, new Size(100, 50), Matrix.CreateTranslation(33, 30));
        Assert.All(regions, region => Assert.True(region.Y >= 31 && region.Y + region.Height <= 79));
    }

    [Fact]
    public void FractionalTransformedEdgesStayInsideThePaintedFrame()
    {
        var regions = new List<Avalonia.Wayland.WaylandBlurRect>();
        KWinBlur.AddRoundedRegion(regions, new Point(33.4, 97.4), new Size(100, 50), 16);
        Assert.All(regions, region =>
        {
            Assert.True(region.X >= 34 && region.X + region.Width <= 133);
            Assert.True(region.Y >= 98 && region.Y + region.Height <= 147);
        });
    }

    [Theory]
    [InlineData(10.4, 20.4, 10, 20)]
    [InlineData(10.5, 20.5, 11, 21)]
    [InlineData(10.6, 20.6, 11, 21)]
    public void FractionalGlyphBandSnapsWithoutGrowing(double x, double y, int expectedX, int expectedY)
    {
        var regions = new List<Avalonia.Wayland.WaylandBlurRect>();

        KWinBlur.AddRoundedRegion(regions, new Point(x, y), new Size(24, 1), 0);

        var region = Assert.Single(regions);
        Assert.Equal(new Avalonia.Wayland.WaylandBlurRect(expectedX, expectedY, 24, 1), region);
    }

    [Fact]
    public void Desktop_card_region_tracks_its_position_and_covers_only_its_frame()
    {
        var regions = new List<Avalonia.Wayland.WaylandBlurRect>();
        KWinBlur.AddRoundedRegion(regions, new Point(204, 104), new Size(352, 136), 16);

        Assert.All(regions, region =>
        {
            Assert.InRange(region.X, 204, 555);
            Assert.InRange(region.Y, 104, 239);
            Assert.True(region.X + region.Width <= 556);
            Assert.True(region.Y + region.Height <= 240);
        });
        Assert.Contains(regions, region => region.X == 205 && region.Width == 350 && region.Height == 104);
    }

    [Fact]
    public void RoundedRegionExcludesTransparentCornersAndKeepsSolidMiddle()
    {
        var regions = new List<Avalonia.Wayland.WaylandBlurRect>();

        KWinBlur.AddRoundedRegion(regions, new Point(10, 20), new Size(100, 52), 16);

        Assert.DoesNotContain(regions, region => region.X == 10 || region.Y == 20);
        Assert.Contains(regions, region => region.X > 11 && region.Y == 21 && region.Width < 98);
        Assert.Contains(regions, region => region.X == 11 && region.Y == 36 && region.Width == 98);
    }
}
