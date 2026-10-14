using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class ShellFrameRenderingTests
{
    [AvaloniaFact]
    public void DoubleBordersRenderAtIntegerAndFractionalScales()
    {
        var output = Environment.GetEnvironmentVariable("DAISYOS_UX_ARTIFACTS");
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
        foreach (var light in new[] { true, false })
        {
            var grid = new Grid { Width = 280, Height = 120, ColumnDefinitions = new ColumnDefinitions("*,*") };
            grid.Background = light ? Brushes.DarkGray : Brushes.Black;
            for (var column = 0; column < 2; column++)
            {
                var frame = new Border
                {
                    Margin = new Thickness(8), Padding = new Thickness(1), CornerRadius = new CornerRadius(32),
                    Background = new SolidColorBrush(Color.Parse("#99000000")),
                    Child = new Border
                    {
                        BorderBrush = new SolidColorBrush(Color.Parse("#80ffffff")), BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(31),
                        BackgroundSizing = column == 0 ? BackgroundSizing.CenterBorder : BackgroundSizing.InnerBorderEdge,
                        Child = new Border
                        {
                            CornerRadius = new CornerRadius(30),
                            Background = new SolidColorBrush(Color.Parse(light ? "#99f2f2f2" : "#99000000"))
                        }
                    }
                };
                if (column == 1) frame = Supersample(frame);
                Grid.SetColumn(frame, column);
                grid.Children.Add(frame);
            }
            grid.Measure(new Size(280, 120));
            grid.Arrange(new Rect(0, 0, 280, 120));
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)(280 * scale), (int)(120 * scale)), new Vector(96 * scale, 96 * scale));
            bitmap.Render(grid);
            using var encoded = new MemoryStream();
            bitmap.Save(encoded);
            using var pixels = SkiaSharp.SKBitmap.Decode(encoded.ToArray());
            var outside = pixels.GetPixel((int)(140 * scale), (int)(4 * scale));
            foreach (var corner in new[] { new Point(149, 9), new Point(271, 9), new Point(149, 111), new Point(271, 111) })
                Assert.Equal(outside, pixels.GetPixel((int)(corner.X * scale), (int)(corner.Y * scale)));
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                bitmap.Save(Path.Combine(output, $"frame-{(light ? "light" : "dark")}-{scale}.png"));
            }
        }
    }

    private static Border Supersample(Border border, int depth = 0)
    {
        if (depth == 2) return border;
        var content = border.Child;
        border.Child = null;
        return new DaisyOS.Shell.Controls.ShellFrameBorder
        {
            Margin = border.Margin, Padding = border.Padding, Background = border.Background,
            BorderBrush = border.BorderBrush, BorderThickness = border.BorderThickness,
            CornerRadius = border.CornerRadius, BackgroundSizing = border.BackgroundSizing,
            Child = content is Border child ? Supersample(child, depth + 1) : content
        };
    }
}
