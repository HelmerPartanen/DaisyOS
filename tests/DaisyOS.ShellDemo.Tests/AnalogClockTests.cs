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
    [InlineData(true, 3)]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    [InlineData(false, 4)]
    public async Task SuppliedHandsRenderInBothThemes(bool dark, int columns)
    {
        var widget = new AnalogClockWidget();
        widget.SetGridSize(columns, 2);
        var window = new Window { Width = columns * 74, Height = 176, Content = widget,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await Task.Delay(250);
            window.UpdateLayout();
            Assert.Equal(columns >= 4, widget.FindControl<ClockFace>("Face")!.ShowDayDate);
            Assert.True(widget.FindControl<ClockFace>("Face")!.IsAnalog);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save($"/tmp/daisy-analog-{columns}-{(dark ? "dark" : "light")}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DaydateHandsUseTheirActualRingCenters()
    {
        Assert.Equal(new Point(30.5, 87), ClockFace.HandPivot("Hour_Hand_daydate.svg"));
        Assert.Equal(new Point(30.5, 87), ClockFace.HandPivot("Minute_Hand_daydate.svg"));
        Assert.Equal(new Point(30.5, 116), ClockFace.HandPivot("Second_Hand_daydate.svg"));
    }

    [Theory]
    [InlineData(222, 176)]
    [InlineData(400, 200)]
    public void HourMarkersAreLongAndFollowClockAngles(double width, double height)
    {
        var center = new Point(width / 2, height / 2);
        for (var index = 0; index < 60; index++)
        {
            var (start, end) = ClockFace.AnalogMarker(width, height, index);
            var direction = end - center;
            var angle = index * Math.PI / 30;
            Assert.Equal(0, direction.X * -Math.Cos(angle) - direction.Y * Math.Sin(angle), 8);
            Assert.True(start.X >= 0 && start.X <= width && start.Y >= 0 && start.Y <= height);
            Assert.True(end.X >= 0 && end.X <= width && end.Y >= 0 && end.Y <= height);
            if (index % 15 == 0) Assert.InRange(Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2)), Math.Min(width, height) * 0.17, Math.Min(width, height) * 0.19);
            if (index % 15 != 0) Assert.True(Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2)) < Math.Min(width, height) * 0.11);
        }
    }

    [Theory]
    [InlineData(222, 176)]
    [InlineData(296, 176)]
    [InlineData(74, 88)]
    [InlineData(400, 200)]
    public void HandClearanceUsesOnlyThreeAndNineMarkers(double width, double height)
    {
        var reach = ClockFace.AnalogHandReach(width, height);
        foreach (var index in new[] { 15, 45 })
        {
            var (inner, _) = ClockFace.AnalogMarker(width, height, index);
            var distance = Math.Abs(inner.X - width / 2);
            Assert.True(distance - reach >= 5.99, $"Marker {index} has only {distance - reach:F2}px clearance.");
        }
        var (top, _) = ClockFace.AnalogMarker(width, height, 0);
        if (width >= height) Assert.True(reach > Math.Abs(top.Y - height / 2) - 6);
        Assert.True(reach <= height / 2);
    }

    [Theory]
    [InlineData(296, 176, 225, 71)]
    [InlineData(400, 200, 290, 110)]
    public void LargeHandsStopSixPixelsBeforeSideLabels(double width, double height, double rightLabelLeft, double leftLabelRight)
    {
        var reach = ClockFace.AnalogHandReach(width, height, rightLabelLeft, leftLabelRight);
        Assert.Equal(6, rightLabelLeft - width / 2 - reach, 8);
        Assert.Equal(6, width / 2 - leftLabelRight - reach, 8);
        Assert.True(reach < ClockFace.AnalogHandReach(width, height));
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
