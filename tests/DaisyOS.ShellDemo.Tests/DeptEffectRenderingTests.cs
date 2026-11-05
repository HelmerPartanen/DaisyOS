using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Wallpaper;
using DaisyOS.Shell.Views.Windows;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class DeptEffectRenderingTests
{
    [AvaloniaFact]
    public async Task ManualForegroundAlignsAndLeavesClockAccessibleAndControlsUncovered()
    {
        using var wallpaper = new RenderTargetBitmap(new PixelSize(640, 400));
        using (var drawing = wallpaper.CreateDrawingContext())
        {
            drawing.FillRectangle(Brushes.SkyBlue, new Rect(0, 0, 640, 400));
            drawing.FillRectangle(Brushes.ForestGreen, new Rect(0, 135, 640, 265));
        }
        var image = new Image { Source = wallpaper, Stretch = Stretch.UniformToFill };
        var clock = new TextBlock { Text = "09:41", FontSize = 100, Foreground = Brushes.White, FontWeight = FontWeight.Bold };
        var area = new StackPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Children = { clock } };
        var overlay = new DeptEffectPresenter();
        var button = new Button { Content = "Unlock", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        var grid = new Grid { Children = { image, area, button, overlay } };
        var window = new Window { Width = 640, Height = 400, Content = grid };
        window.Show();
        try
        {
            // Phase 1: supply an exact, manually prepared foreground mask. This also makes
            // rendering/alignment checks independent of model accuracy and inference timing.
            overlay.Configure(image, clock, area, () => LockScreenWindow.RasterizeClockGlyphBands(clock.Text!,
                new Typeface(clock.FontFamily, clock.FontStyle, clock.FontWeight), clock.FontSize));
            overlay.SetPreferences(true, false, 15);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var bands = LockScreenWindow.RasterizeClockGlyphBands(clock.Text!, new Typeface(clock.FontFamily, clock.FontStyle, clock.FontWeight), clock.FontSize);
            var origin = clock.TranslatePoint(default, overlay)!.Value;
            var glyphMinTop = bands.Min(b => b.Top);
            var glyphMaxBottom = bands.Max(b => b.Bottom);
            var mask = new byte[DeptEffectService.OverlaySize * DeptEffectService.OverlaySize];
            var rowY = origin.Y + glyphMinTop + (glyphMaxBottom - glyphMinTop) * 0.78;
            var row = Math.Clamp((int)Math.Round(rowY / overlay.Bounds.Height * DeptEffectService.OverlaySize), 0, DeptEffectService.OverlaySize - 1);
            Array.Fill(mask, (byte)255, row * DeptEffectService.OverlaySize, mask.Length - row * DeptEffectService.OverlaySize);
            overlay.ApplyPreparedMasks([mask]);
            window.UpdateLayout();
            overlay.RefreshLayout();
            Assert.True(overlay.HasForeground);
            Assert.False(overlay.IsHitTestVisible);
            Assert.False(overlay.Focusable);
            Assert.Equal("09:41", clock.Text);
            Assert.Equal(40, area.Margin.Top);
            overlay.SetPreferences(true, false, 25);
            window.UpdateLayout();
            Assert.True(overlay.HasForeground);
            Assert.Equal(40, area.Margin.Top);
            origin = clock.TranslatePoint(default, overlay)!.Value;
            var overlap = DeptEffectPresenter.MeasureOverlap(mask, bands, origin.X, origin.Y, overlay.Bounds.Size, glyphMinTop + (glyphMaxBottom - glyphMinTop) * 0.55);
            Assert.InRange(overlap.Total, 0.15, 0.35);
            Assert.InRange(overlap.Upper, 0, 0.015);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Directory.CreateDirectory("/tmp/daisy-dept-effect");
            frame.Save("/tmp/daisy-dept-effect/manual.png", PngBitmapEncoderOptions.Default);
            foreach (var strength in new[] { 10, 15, 20, 30 })
            {
                overlay.SetPreferences(true, false, strength);
                window.UpdateLayout();
                Assert.True(overlay.HasForeground);
                Assert.Equal(40, area.Margin.Top);
            }
            // A narrow foreground can cover only part of one digit. Its best readable
            // overlap is below 10%; failing to reach the ideal 15% must not turn it off.
            var narrowMask = new byte[mask.Length];
            var baseOrigin = clock.TranslatePoint(default, overlay)!.Value;
            var glyphLeft = bands.Min(b => b.Left);
            var glyphRight = glyphLeft + (bands.Max(b => b.Right) - glyphLeft) / 5;
            var boundary = baseOrigin.Y + glyphMinTop + (glyphMaxBottom - glyphMinTop) * 0.75;
            for (var y = 0; y < DeptEffectService.OverlaySize; y++)
                for (var x = 0; x < DeptEffectService.OverlaySize; x++)
                {
                    var px = x / (double)DeptEffectService.OverlaySize * overlay.Bounds.Width;
                    var py = y / (double)DeptEffectService.OverlaySize * overlay.Bounds.Height;
                    if (px >= baseOrigin.X + glyphLeft && px <= baseOrigin.X + glyphRight && py >= boundary)
                        narrowMask[y * DeptEffectService.OverlaySize + x] = 255;
                }
            overlay.SetPreferences(true, false);
            overlay.ApplyPreparedMasks([narrowMask]);
            window.UpdateLayout();
            Assert.True(overlay.HasForeground);
            Assert.Equal(40, area.Margin.Top);
            origin = clock.TranslatePoint(default, overlay)!.Value;
            overlap = DeptEffectPresenter.MeasureOverlap(narrowMask, bands, origin.X, origin.Y, overlay.Bounds.Size,
                glyphMinTop + (glyphMaxBottom - glyphMinTop) * 0.55);
            Assert.InRange(overlap.Total, 0.03, 0.10);
            Assert.InRange(overlap.Upper, 0, 0.015);
            overlay.ApplyPreparedMasks([mask]);
            overlay.SetPreferences(true, false, 0);
            window.UpdateLayout();
            Assert.False(overlay.HasForeground);
            overlay.SetPreferences(true, false, 15);
            window.UpdateLayout();
            Assert.True(overlay.HasForeground);
            // An impossibly tall subject must restore the ordinary clock.
            overlay.ApplyPreparedMasks([Enumerable.Repeat((byte)255, mask.Length).ToArray()]);
            window.UpdateLayout();
            Assert.False(overlay.HasForeground);
            overlay.SetPreferences(false, false, 15);
            window.UpdateLayout();
            Assert.False(overlay.HasForeground);
            Assert.Equal(40, area.Margin.Top);
        }
        finally { window.Close(); }
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task ClockPositionRemainsStableAtUserConfiguredPositionAcrossReentryAndWallpaperSwap()
    {
        using var seededWallpaper = new RenderTargetBitmap(new PixelSize(1280, 800));
        using (var drawing = seededWallpaper.CreateDrawingContext())
        {
            drawing.FillRectangle(Brushes.SkyBlue, new Rect(0, 0, 1280, 800));
            drawing.FillRectangle(Brushes.ForestGreen, new Rect(0, 270, 1280, 530));
        }
        using var preRenderedWallpaper = new RenderTargetBitmap(new PixelSize(640, 400));
        using (var drawing = preRenderedWallpaper.CreateDrawingContext())
        {
            drawing.FillRectangle(Brushes.SkyBlue, new Rect(0, 0, 640, 400));
            drawing.FillRectangle(Brushes.ForestGreen, new Rect(0, 135, 640, 265));
        }

        var wallpaperIdentity = $"test-wallpaper-{Guid.NewGuid():N}";

        // First lock screen entry: clock stays at its configured TopFraction position (40px for 0.10 on 400px height)
        {
            var image = new Image { Source = seededWallpaper, Stretch = Stretch.UniformToFill };
            var clock = new TextBlock { Text = "22:24", FontSize = 100, Foreground = Brushes.White, FontWeight = FontWeight.Bold };
            var area = new StackPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Children = { clock } };
            var overlay = new DeptEffectPresenter { WallpaperIdentity = wallpaperIdentity };
            var grid = new Grid { Children = { image, area, overlay } };
            var window = new Window { Width = 640, Height = 400, Content = grid };
            window.Show();
            try
            {
                overlay.Configure(image, clock, area, () => LockScreenWindow.RasterizeClockGlyphBands(clock.Text!,
                    new Typeface(clock.FontFamily, clock.FontStyle, clock.FontWeight), clock.FontSize));
                overlay.SetPreferences(true, false, 15);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var bands = LockScreenWindow.RasterizeClockGlyphBands(clock.Text!, new Typeface(clock.FontFamily, clock.FontStyle, clock.FontWeight), clock.FontSize);
                var origin = clock.TranslatePoint(default, overlay)!.Value;
                var rowY = origin.Y + bands.Min(b => b.Top) + (bands.Max(b => b.Bottom) - bands.Min(b => b.Top)) * 0.78;
                var row = Math.Clamp((int)Math.Round(rowY / overlay.Bounds.Height * DeptEffectService.OverlaySize), 0, DeptEffectService.OverlaySize - 1);
                var mask = new byte[DeptEffectService.OverlaySize * DeptEffectService.OverlaySize];
                Array.Fill(mask, (byte)255, row * DeptEffectService.OverlaySize, mask.Length - row * DeptEffectService.OverlaySize);

                overlay.ApplyPreparedMasks([mask]);
                window.UpdateLayout();
                overlay.RefreshLayout();
                Assert.True(overlay.HasForeground);
                Assert.Equal(40, area.Margin.Top);

                // Swapping from the raw seeded bitmap (1280x800) to the pre-rendered bitmap (640x400)
                // for the same WallpaperIdentity keeps foreground and fixed position.
                image.Source = preRenderedWallpaper;
                window.UpdateLayout();
                overlay.RefreshLayout();
                Assert.True(overlay.HasForeground);
                Assert.Equal(40, area.Margin.Top);

                // Changing user position setting (Higher = 0.06, Balanced = 0.10, Lower = 0.16)
                overlay.ClockTopFraction = 0.16;
                overlay.RefreshLayout();
                Assert.Equal(64, area.Margin.Top);
                overlay.ClockTopFraction = 0.10;
                overlay.RefreshLayout();
                Assert.Equal(40, area.Margin.Top);
            }
            finally { window.Close(); }
        }
        await Task.CompletedTask;
    }
}
