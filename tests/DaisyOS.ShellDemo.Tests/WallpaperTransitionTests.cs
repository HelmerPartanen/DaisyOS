using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.Services.Theming;
using DaisyOS.Shell.Services.Wallpaper;
using DaisyOS.Shell.Views.Components.Wallpaper;
using SkiaSharp;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class WallpaperTransitionTests
{
    private static string Appearance => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Heic", "appearance.heic");

    [AvaloniaFact]
    public async Task AppearanceSwitchKeepsOpaqueOutgoingImageUntilIncomingFrameHasFadedIn()
    {
        var layer = new WallpaperLayer();
        var window = new Window { Width = 256, Height = 160, Content = layer, Background = Brushes.Black, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        try
        {
            using (window.CaptureRenderedFrame()) { }
            Assert.True(await layer.TryPresentWallpaperAsync(Appearance, TimeSpan.FromSeconds(10)));
            var image = layer.FindControl<Image>("WallpaperImage")!;
            var previous = layer.FindControl<Image>("PreviousWallpaperImage")!;
            Assert.False(ThemeTransitionPolicy.GetEnabled(image));
            var old = image.Source;
            window.RequestedThemeVariant = ThemeVariant.Dark;
            for (var i = 0; i < 200 && ReferenceEquals(old, image.Source); i++) await Task.Delay(10);
            Assert.NotSame(old, image.Source);
            Assert.Same(old, previous.Source);
            Assert.Equal(1, previous.Opacity);
            Assert.NotNull(image.Source);
            var minimumLight = Math.Min(CenterLight((Bitmap)old!), CenterLight((Bitmap)image.Source!));
            // A blend of two opaque frames must not become darker than both
            // endpoints. Derive the bound from the actual fixture pixels.
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(25);
                using var frame = window.CaptureRenderedFrame();
                using var stream = new MemoryStream();
                frame!.Save(stream, PngBitmapEncoderOptions.Default);
                using var pixels = SKBitmap.Decode(stream.ToArray());
                var color = pixels.GetPixel(128, 80);
                Assert.Equal(255, color.Alpha);
                Assert.True(color.Red + color.Green + color.Blue >= minimumLight - 6, $"Wallpaper flashed dark: {color}");
            }
            await Task.Delay(100);
            Assert.Null(previous.Source);
            Assert.Equal(1, image.Opacity);
        }
        finally { window.Close(); }
    }

    private static int CenterLight(Bitmap image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, PngBitmapEncoderOptions.Default);
        using var pixels = SKBitmap.Decode(stream.ToArray());
        var color = pixels.GetPixel(pixels.Width / 2, pixels.Height / 2);
        return color.Red + color.Green + color.Blue;
    }

    [AvaloniaFact]
    public async Task LockSeedIsReadyBeforeMappingAndSurvivesDesktopAndCacheRetirement()
    {
        var desktop = new WallpaperLayer();
        var desktopWindow = new Window { Width = 256, Height = 160, Content = desktop };
        var seed = new WallpaperLayer();
        var lockWindow = new Window { Width = 256, Height = 160, Content = seed };
        desktopWindow.Show();
        try
        {
            using (desktopWindow.CaptureRenderedFrame()) { }
            Assert.True(await desktop.TryPresentWallpaperAsync(Appearance, TimeSpan.FromSeconds(10)));
            seed.SeedFrom(desktop);
            Assert.NotNull(seed.FindControl<Image>("WallpaperImage")!.Source);
            desktopWindow.Close();
            // A lock seed holds its own decoded-source lease, not the desktop's
            // rendered bitmap or an unowned borrowed image.
            lockWindow.Show();
            using var frame = lockWindow.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.True(((Bitmap)seed.FindControl<Image>("WallpaperImage")!.Source!).PixelSize.Width > 0);
        }
        finally { desktopWindow.Close(); lockWindow.Close(); }
    }

    [AvaloniaFact]
    public void NativeMaterialMaskUsesAncestorOpacityAndPublishesOneCompleteMotionStep()
    {
        var material = new Border { Width = 120, Height = 40, CornerRadius = new CornerRadius(20) };
        var panel = new Grid { Children = { material } };
        var window = new Window { Width = 300, Height = 200, Content = panel };
        using var blur = new KWinBlur(window, panel, () => [material]);
        window.Show();
        try
        {
            window.UpdateLayout();
            var original = blur.BuildRegions();
            Assert.NotEmpty(original);
            var changes = 0;
            blur.GeometryChanged += () => changes++;
            blur.UpdateGeometry(() => { panel.Opacity = 0.5; panel.RenderTransform = new TranslateTransform(0, 12); });
            Assert.Equal(1, changes);
            var moved = blur.BuildRegions();
            Assert.Equal(original[0].Y + 12, moved[0].Y);
            panel.Opacity = 0;
            Assert.Empty(blur.BuildRegions());
        }
        finally { window.Close(); }
    }
}
