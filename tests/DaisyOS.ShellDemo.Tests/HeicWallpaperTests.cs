using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using DaisyOS.Shell.Services.Wallpaper;
using DaisyOS.Shell.Views.Components.Wallpaper;
using SkiaSharp;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class HeicWallpaperTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Heic", name);
    private static DateTimeOffset LocalHour(int hour) => new(DateTime.Today.AddHours(hour), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today.AddHours(hour)));

    [AvaloniaFact]
    public async Task RealHeicFileDecodesBothAppearancesWithoutChangingTheSource()
    {
        var source = Environment.GetEnvironmentVariable("DAISYOS_TEST_HEIC");
        Assert.SkipWhen(string.IsNullOrEmpty(source), "Set DAISYOS_TEST_HEIC to validate a real appearance HEIC.");
        var before = await File.ReadAllBytesAsync(source!, TestContext.Current.CancellationToken);
        var light = await HeicWallpaper.ResolveFrameAsync(source!, false, DateTimeOffset.Now, TestContext.Current.CancellationToken);
        var dark = await HeicWallpaper.ResolveFrameAsync(source!, true, DateTimeOffset.Now, TestContext.Current.CancellationToken);
        Assert.NotEqual(light, dark);
        using var first = SKBitmap.Decode(light);
        using var second = SKBitmap.Decode(dark);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
        Assert.Equal(before, await File.ReadAllBytesAsync(source!, TestContext.Current.CancellationToken));
        await File.WriteAllLinesAsync("/tmp/daisy-real-heic-frames.txt", new[] { light, dark }, TestContext.Current.CancellationToken);
    }

    [AvaloniaTheory]
    [InlineData("appearance.heic", false, 8, true)]
    [InlineData("appearance.heic", true, 8, false)]
    [InlineData("timed.heic", false, 7, true)]
    [InlineData("timed.heic", false, 20, false)]
    [InlineData("timed.heic", false, 2, false)]
    [InlineData("static.heif", true, 20, true)]
    public async Task NativeDecoderSelectsExpectedImage(string name, bool dark, int hour, bool gold)
    {
        var path = await HeicWallpaper.ResolveFrameAsync(Fixture(name), dark, LocalHour(hour), TestContext.Current.CancellationToken);
        using var bitmap = SKBitmap.Decode(path);
        Assert.NotNull(bitmap);
        Assert.Equal(64, bitmap.Width);
        Assert.Equal(64, bitmap.Height);
        var pixel = bitmap.GetPixel(32, 32);
        Assert.Equal(gold, pixel.Red > pixel.Blue);
        var modified = File.GetLastWriteTimeUtc(path);
        Assert.Equal(path, await HeicWallpaper.ResolveFrameAsync(Fixture(name), dark, LocalHour(hour), TestContext.Current.CancellationToken));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
        Assert.True(WallpaperFileTypes.IsImage(Fixture(name)));
        Assert.False(WallpaperFileTypes.IsVideo(Fixture(name)));
    }

    [AvaloniaFact]
    public async Task ThemeChangeReplacesDynamicImageAndBrokenFileRetainsCurrentWallpaper()
    {
        var layer = new WallpaperLayer();
        var window = new Window { Width = 256, Height = 160, Content = layer, RequestedThemeVariant = ThemeVariant.Light };
        var invalid = Path.Combine(Path.GetTempPath(), "invalid-wallpaper-" + Guid.NewGuid() + ".heic");
        await File.WriteAllTextAsync(invalid, "not a HEIC file", TestContext.Current.CancellationToken);
        window.Show();
        try
        {
            using (window.CaptureRenderedFrame()) { }
            Assert.True(await layer.TryPresentWallpaperAsync(Fixture("appearance.heic"), TimeSpan.FromSeconds(10)));
            await Task.Delay(400);
            using (window.CaptureRenderedFrame()) { }
            var source = layer.FindControl<Image>("WallpaperImage")!.Source;
            Assert.NotNull(source);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            for (var attempt = 0; attempt < 60 && ReferenceEquals(source, layer.FindControl<Image>("WallpaperImage")!.Source); attempt++)
            {
                await Task.Delay(50);
                using (window.CaptureRenderedFrame()) { }
            }
            var dark = layer.FindControl<Image>("WallpaperImage")!.Source;
            Assert.NotSame(source, dark);
            Assert.False(await layer.TryPresentWallpaperAsync(invalid, TimeSpan.FromSeconds(5)));
            Assert.Same(dark, layer.FindControl<Image>("WallpaperImage")!.Source);
        }
        finally { window.Close(); File.Delete(invalid); }
    }

    [Fact]
    public void MetadataFallbackAndSolarGeometryAreWellDefined()
    {
        var malformed = HeicWallpaperSchedule.FromXmp("<broken>");
        Assert.Equal(2, malformed.SelectFrame(3, 2, true, DateTimeOffset.Now));
        var noon = HeicWallpaperSchedule.SunPosition(new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero), (0, 0));
        var midnight = HeicWallpaperSchedule.SunPosition(new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero), (0, 0));
        Assert.InRange(noon.Altitude, 85, 90);
        Assert.InRange(midnight.Altitude, -90, -85);
        Assert.Throws<InvalidDataException>(() => WallpaperPropertyList.Read(new byte[40]));
        var apr = HeicWallpaperSchedule.FromXmp("<r xmlns:a='http://ns.apple.com/namespace/1.0/'><a:apr>YnBsaXN0MDDSAQIDBFFsUWQQABABCA0PERMAAAAAAAABAQAAAAAAAAAFAAAAAAAAAAAAAAAAAAAAFQ==</a:apr></r>");
        Assert.Equal(0, apr.SelectFrame(2, 0, false, DateTimeOffset.Now));
        Assert.Equal(1, apr.SelectFrame(2, 0, true, DateTimeOffset.Now));
        Assert.Equal(0, apr.SelectFrame(1, 0, true, DateTimeOffset.Now));
    }

    [AvaloniaFact]
    public async Task SolarMetadataSelectsDifferentFramesAcrossTheDay()
    {
        var day = await HeicWallpaper.ResolveFrameAsync(Fixture("solar.heic"), false, LocalHour(12), TestContext.Current.CancellationToken);
        var night = await HeicWallpaper.ResolveFrameAsync(Fixture("solar.heic"), false, LocalHour(0), TestContext.Current.CancellationToken);
        Assert.NotEqual(day, night);
    }
}
