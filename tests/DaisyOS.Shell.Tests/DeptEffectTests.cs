using Avalonia;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Shell.Services.Wallpaper;
using Xunit;
using SkiaSharp;

namespace DaisyOS.Shell.Tests;

public class DeptEffectTests
{
    [Fact]
    public void RejectsEmptyFullAndUncertainMasks()
    {
        var empty = new byte[320 * 320];
        var full = Enumerable.Repeat((byte)255, empty.Length).ToArray();
        var mountain = Skyline();
        Assert.False(DeptEffectService.IsCompatible(empty, 0, false));
        Assert.False(DeptEffectService.IsCompatible(full, 0, false));
        Assert.False(DeptEffectService.IsCompatible(mountain, 0.3, true));
        Assert.True(DeptEffectService.IsCompatible(mountain, 0.02, true));
        Assert.False(DeptEffectService.IsCompatible(mountain.Reverse().ToArray(), 0.02, true));
    }

    [Fact]
    public void MeasuresGlyphPixelsInsteadOfClockRectangle()
    {
        var mask = new byte[320 * 320];
        // Foreground in the empty gap between two glyphs must not count as overlap.
        for (var y = 100; y < 200; y++)
            for (var x = 120; x < 180; x++) mask[y * 320 + x] = 255;
        Rect[] bands = [new(100, 100, 10, 1), new(190, 100, 10, 1)];
        Assert.Equal(0, DeptEffectPresenter.MeasureOverlap(mask, bands, 0, 0, new Size(320, 320), 101).Total);
        mask[100 * 320 + 100] = 255;
        Assert.Equal(0.05, DeptEffectPresenter.MeasureOverlap(mask, bands, 0, 0, new Size(320, 320), 101).Total, 6);
    }

    [Fact]
    public void OverlapUsesDisplayCoordinatesAndPartialAlpha()
    {
        var mask = Skyline();
        Rect[] bands = Enumerable.Range(0, 100).Select(y => new Rect(0, y, 20, 1)).ToArray();
        var normal = DeptEffectPresenter.MeasureOverlap(mask, bands, 100, 75, new Size(320, 320), 50);
        Assert.Equal(0.15, normal.Total, 6);
        Assert.Equal(0, normal.Upper);
        var scaled = DeptEffectPresenter.MeasureOverlap(mask, bands.Select(b => new Rect(b.X * 2, b.Y * 2, b.Width * 2, b.Height * 2)).ToArray(), 200, 150, new Size(640, 640), 100);
        Assert.Equal(normal.Total, scaled.Total, 6);
        Array.Fill(mask, (byte)128);
        Assert.Equal(128 / 255d, DeptEffectPresenter.MeasureOverlap(mask, bands, 100, 75, new Size(320, 320), 50).Total, 6);
    }

    [Theory]
    [InlineData(1920, 1080, 1080, 1920)]
    [InlineData(1080, 1920, 3440, 1440)]
    [InlineData(3840, 2160, 1920, 1080)]
    public void CenterCropPreservesAspectRatioAndAlignment(int sw, int sh, int tw, int th)
    {
        var crop = DeptEffectPresenter.Crop(new Size(sw, sh), new Size(tw, th));
        Assert.Equal(tw / (double)th, crop.Width / crop.Height, 6);
        Assert.Equal(sw / 2d, crop.Center.X, 6);
        Assert.Equal(sh / 2d, crop.Center.Y, 6);
        Assert.True(crop.Left >= 0 && crop.Top >= 0 && crop.Right <= sw && crop.Bottom <= sh);
    }

    [Fact]
    public async Task PreferencesRoundTripThroughExistingStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AppearanceSettingsStore(Path.Combine(directory, "appearance.json"));
            Assert.True(store.Load().DeptEffectAutomatic);
            Assert.Equal(15, store.Load().DeptEffectStrength);
            await store.SaveAsync(new AppearanceSettings { DeptEffectEnabled = false, DeptEffectAutomatic = false, DeptEffectStrength = 23 });
            var settings = store.Load();
            Assert.False(settings.DeptEffectEnabled);
            Assert.False(settings.DeptEffectAutomatic);
            Assert.Equal(23, settings.DeptEffectStrength);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CancellationAndInvalidImagesFallBackWithoutCacheFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new DeptEffectService(directory);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Null(await service.AnalyzeAsync([], true, canceled.Token));
        Assert.Null(await service.AnalyzeAsync([1, 2, 3], true, default));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task BundledModelsDetectSkylineCacheAndRejectPlainGradient()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var service = new DeptEffectService(directory);
            using var image = new SKBitmap(640, 640);
            for (var y = 0; y < 640; y++)
                for (var x = 0; x < 640; x++)
                    image.SetPixel(x, y, y > 150 + Math.Abs(x - 320) * 0.65
                        ? new SKColor(65, 75, 70) : new SKColor(100, 180, 235));
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            var masks = await service.AnalyzeAsync(png.ToArray(), true, default);
            Assert.NotNull(masks);
            Assert.Equal(2, masks.Length);
            Assert.All(masks, mask => Assert.Equal(640 * 640, mask.Length));
            Assert.True(masks[0].Take(640 * 8).Average(v => (double)v) < 30);
            Assert.True(masks[0].TakeLast(640 * 8).Average(v => (double)v) > 220);
            var file = Assert.Single(Directory.GetFiles(directory, "*.mask"));
            var modified = File.GetLastWriteTimeUtc(file);
            // Turning automatic detection off still allows cached segmentation to be reused.
            var cached = await service.AnalyzeAsync(png.ToArray(), false, default);
            Assert.NotNull(cached);
            Assert.Equal(masks[0], cached[0]);
            Assert.Equal(modified, File.GetLastWriteTimeUtc(file));
            for (var y = 0; y < 640; y++)
                for (var x = 0; x < 640; x++)
                    image.SetPixel(x, y, new SKColor((byte)(80 + y / 8), (byte)(100 + y / 8), (byte)(140 + y / 8)));
            using var gradient = image.Encode(SKEncodedImageFormat.Png, 100);
            var gradientMasks = await service.AnalyzeAsync(gradient.ToArray(), true, default);
            Assert.NotNull(gradientMasks);
            Assert.All(gradientMasks, mask => Assert.All(mask, value => Assert.Equal(0, value)));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static byte[] Skyline()
    {
        var mask = new byte[320 * 320];
        Array.Fill(mask, (byte)255, 320 * 160, 320 * 160);
        return mask;
    }
}
