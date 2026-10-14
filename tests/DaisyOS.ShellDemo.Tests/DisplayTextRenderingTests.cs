using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class DisplayTextRenderingTests
{
    [AvaloniaTheory]
    [InlineData("Inter", 1d)]
    [InlineData("Inter", 1.25d)]
    [InlineData("Inter", 1.5d)]
    [InlineData("Inter", 1.75d)]
    [InlineData("Inter", 2d)]
    [InlineData("Inter", 3d)]
    [InlineData("DejaVu Serif", 1.25d)]
    [InlineData("DejaVu Sans Mono", 1.5d)]
    public void TextRasterizesAtTheCurrentDisplayScaleWithoutColorFringes(string family, double scale)
    {
        var text = new TextBlock { Text = "ÅÉgjpq 012345 日本語 العربية", FontFamily = new FontFamily(family),
            FontSize = 14, Foreground = Brushes.Black, Margin = new Thickness(12) };
        var window = new Window { Width = 400, Height = 80, Background = Brushes.White, Content = text };
        window.Show();
        try
        {
            foreach (var currentScale in new[] { scale, 1d, scale })
            {
                window.SetRenderScaling(currentScale);
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.Equal(currentScale, window.RenderScaling);
                Assert.Equal(TextRenderingMode.Antialias, TextOptions.GetTextRenderingMode(window));
                Assert.Equal(TextHintingMode.Strong, TextOptions.GetTextHintingMode(window));
                Assert.Equal(BaselinePixelAlignment.Aligned, TextOptions.GetBaselinePixelAlignment(window));
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(PixelSize.FromSize(window.ClientSize, currentScale), frame.PixelSize);
                using var stream = new MemoryStream();
                frame.Save(stream, PngBitmapEncoderOptions.Default);
                using var pixels = SkiaSharp.SKBitmap.Decode(stream.ToArray());
                var ink = 0;
                for (var y = 0; y < pixels.Height; y++)
                    for (var x = 0; x < pixels.Width; x++)
                    {
                        var pixel = pixels.GetPixel(x, y);
                        Assert.Equal(pixel.Red, pixel.Green);
                        Assert.Equal(pixel.Green, pixel.Blue);
                        if (pixel.Red < 200) ink++;
                    }
                Assert.True(ink > 100, "The scaled frame must contain visible text.");
                Assert.True(text.DesiredSize.Height <= window.ClientSize.Height);
            }
        }
        finally { window.Close(); }
    }
}
