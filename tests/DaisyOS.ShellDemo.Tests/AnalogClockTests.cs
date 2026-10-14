using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using DaisyOS.Shell.DesktopWidgets;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class AnalogClockTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void SuppliedHandsRenderInBothThemes(bool dark)
    {
        var widget = new AnalogClockWidget();
        var window = new Window { Width = 222, Height = 176, Content = widget,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.True(widget.FindControl<ClockFace>("Face")!.IsAnalog);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save($"/tmp/daisy-analog-{(dark ? "dark" : "light")}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(30.5, 87)] // Hour and minute rings.
    [InlineData(30.5, 116)] // Second ring.
    public void RingCentersStayFixedThroughoutRotation(double x, double y)
    {
        var pivot = new Point(x, y);
        var center = new Point(111, 88);
        foreach (double scale in new[] { 0.1, 0.2, 0.5 })
        foreach (double angle in new[] { 0, Math.PI / 2, Math.PI, Math.PI * 1.5, Math.PI * 1.97 })
        {
            var matrix = ClockFace.HandTransform(pivot, center, scale, angle);
            var transformed = pivot.Transform(matrix);
            Assert.Equal(center.X, transformed.X, 8);
            Assert.Equal(center.Y, transformed.Y, 8);
            var tip = new Point(x, y + 100).Transform(matrix);
            Assert.Equal(center.X + Math.Sin(angle) * 100 * scale, tip.X, 8);
            Assert.Equal(center.Y - Math.Cos(angle) * 100 * scale, tip.Y, 8);
        }
    }
}
