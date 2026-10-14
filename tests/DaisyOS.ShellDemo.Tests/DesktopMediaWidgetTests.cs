using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Shell.DesktopWidgets;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class DesktopMediaWidgetTests
{
    [AvaloniaFact]
    public void MediumTransportRevealsForPointerAndRetainsKeyboardFocus()
    {
        var widget = new MediaWidget();
        widget.Resources["MotionDurationFast"] = TimeSpan.Zero;
        widget.Resources["MotionDurationBase"] = TimeSpan.Zero;
        widget.SetGridSize(3, 2);
        var outside = new Button { Content = "Outside", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        widget.Width = 224;
        widget.Height = 180;
        var host = new Grid();
        host.Children.Add(widget);
        host.Children.Add(outside);
        var window = new Window { Width = 400, Height = 300, Content = host };
        window.Show();
        try
        {
            using (window.CaptureRenderedFrame()) { }
            var layout = widget.FindControl<Grid>("MediumLayout")!;
            var info = widget.FindControl<Grid>("MediumInfoHost")!;
            var controls = widget.FindControl<StackPanel>("MediumControlsStack")!;
            var button = widget.FindControl<Button>("MediumPlayPauseButton")!;
            window.MouseMove(new Point(390, 290));
            Assert.True(outside.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, info.Opacity);
            Assert.Equal(0, controls.Opacity);
            Assert.True(button.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsFocused);
            Assert.Equal(1, controls.Opacity);
            Assert.True(outside.Focus());
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(layout.TranslatePoint(new Point(50, 50), window)!.Value);
            Assert.Equal(0, info.Opacity);
            Assert.Equal(1, controls.Opacity);
            Assert.True(controls.IsHitTestVisible);
            Assert.True(button.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsFocused);
            window.MouseMove(new Point(390, 290));
            Assert.Equal(1, controls.Opacity);
            Assert.True(outside.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, info.Opacity);
            Assert.Equal(0, controls.Opacity);
            Assert.False(controls.IsHitTestVisible);
        }
        finally { window.Close(); }
    }
}
