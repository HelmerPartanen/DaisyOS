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
    public void RoundedMaskIncludesAntialiasedCornerPixels(int radius)
    {
        var regions = new List<Avalonia.Wayland.WaylandBlurRect>();
        KWinBlur.AddRoundedRegion(regions, default, new Size(120, 100), radius);
        for (var row = 0; row < radius; row++)
        {
            var boundary = radius - Math.Sqrt(radius * radius - Math.Pow(radius - row - 0.5, 2));
            var top = regions.Single(region => region.Y == row);
            Assert.True(top.X <= boundary && boundary < top.X + 1);
            Assert.Equal(120, top.X * 2 + top.Width);
            var bottom = regions.Single(region => region.Y == 99 - row);
            Assert.Equal(top.X, bottom.X);
            Assert.Equal(top.Width, bottom.Width);
        }
    }
    [Fact]
    public void PasswordContainerUsesItsOwnCornerRadiusAfterScaling()
    {
        var regions = new List<Avalonia.Wayland.WaylandBlurRect>();
        KWinBlur.AddTransformedRegion(regions, new Size(320, 54),
            new Matrix(1.2, 0, 0, 1.2, 40, 80), 21);
        Assert.Contains(regions, region => region.X == 40 && region.Y == 105 && region.Height == 15);
        Assert.All(regions, region => Assert.True(region.X >= 40 && region.Y >= 80));
    }

    [Fact]
    public void DragAndFlipTransformsMoveAndScaleTheEntireBlurMask()
    {
        var regions = new List<Avalonia.Wayland.WaylandBlurRect>();
        KWinBlur.AddTransformedRegion(regions, new Size(100, 50), new Matrix(1.2, 0, 0, 1.2, 33, 97));
        Assert.Contains(regions, region => region.X == 33 && region.Y == 116 && region.Width == 120 && region.Height == 22);
        Assert.All(regions, region => Assert.True(region.Y >= 97 && region.Y + region.Height <= 157));
        regions.Clear();
        KWinBlur.AddTransformedRegion(regions, new Size(100, 50), Matrix.CreateTranslation(33, 30));
        Assert.All(regions, region => Assert.True(region.Y >= 30 && region.Y + region.Height <= 80));
    }

    [Fact]
    public void FractionalTransformedEdgesRoundOutwardsOnBothSides()
    {
        var regions = new List<Avalonia.Wayland.WaylandBlurRect>();
        KWinBlur.AddRoundedRegion(regions, new Point(33.4, 97.4), new Size(100, 50), 16);
        Assert.Contains(regions, region => region.X == 33 && region.Width == 101);
        Assert.Equal(148, regions.Max(region => region.Y + region.Height));
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
        Assert.Contains(regions, region => region.X == 204 && region.Width == 352 && region.Height == 104);
    }

    [Fact]
    public void RoundedRegionExcludesTransparentCornersAndKeepsSolidMiddle()
    {
        var regions = new List<Avalonia.Wayland.WaylandBlurRect>();

        KWinBlur.AddRoundedRegion(regions, new Point(10, 20), new Size(100, 52), 16);

        Assert.DoesNotContain(regions, region => region.X == 10 && region.Y == 20);
        Assert.Contains(regions, region => region.X > 10 && region.Y == 20 && region.Width < 100);
        Assert.Contains(regions, region => region.X == 10 && region.Y == 36 && region.Width == 100);
    }
}
