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
            var mask = new byte[DeptEffectService.OverlaySize * DeptEffectService.OverlaySize];
            var row = (int)Math.Round(135 / 400d * DeptEffectService.OverlaySize);
            Array.Fill(mask, (byte)255, row * DeptEffectService.OverlaySize, mask.Length - row * DeptEffectService.OverlaySize);
            overlay.ApplyPreparedMasks([mask]);
            window.UpdateLayout();
            overlay.RefreshLayout();
            Assert.True(overlay.HasForeground);
            Assert.False(overlay.IsHitTestVisible);
            Assert.False(overlay.Focusable);
            Assert.Equal("09:41", clock.Text);
            var originalTop = area.Margin.Top;
            overlay.SetPreferences(true, false, 25);
            window.UpdateLayout();
            Assert.True(overlay.HasForeground);
            Assert.True(area.Margin.Top >= originalTop);
            var bands = LockScreenWindow.RasterizeClockGlyphBands(clock.Text!, new Typeface(clock.FontFamily, clock.FontStyle, clock.FontWeight), clock.FontSize);
            var origin = clock.TranslatePoint(default, overlay)!.Value;
            var overlap = DeptEffectPresenter.MeasureOverlap(mask, bands, origin.X, origin.Y, overlay.Bounds.Size, bands.Min(b => b.Top) + (bands.Max(b => b.Bottom) - bands.Min(b => b.Top)) * 0.55);
            Assert.InRange(overlap.Total, 0.20, 0.30);
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
                origin = clock.TranslatePoint(default, overlay)!.Value;
                overlap = DeptEffectPresenter.MeasureOverlap(mask, bands, origin.X, origin.Y, overlay.Bounds.Size,
                    bands.Min(b => b.Top) + (bands.Max(b => b.Bottom) - bands.Min(b => b.Top)) * 0.55);
                Assert.InRange(overlap.Total, strength / 100d - 0.05, strength / 100d + 0.04);
            }
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
}
