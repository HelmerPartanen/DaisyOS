using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DaisyOS.Shell.Views.Components.Launcher;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LauncherMenuPlacementTests
{
    [AvaloniaTheory]
    [InlineData(1, 1)]
    [InlineData(1.25, 1.25)]
    [InlineData(1.5, 1.5)]
    [InlineData(2, 2)]
    [InlineData(1.5, 1)]
    [InlineData(1, 1.5)]
    public void DetachedPowerMenuRetainsItsAnchorAcrossRepeatedOpens(double sourceScale, double overlayScale)
    {
        var launcher = new LauncherView { IsVisible = true };
        var source = new Window { Width = 1280, Height = 800, Content = launcher };
        var menu = (Border)launcher.DetachPowerMenuForNativeOverlay()!;
        var panel = new Panel(); panel.Children.Add(menu);
        var overlay = new Window { Width = 1280, Height = 800, Content = panel };
        source.Show(); overlay.Show();
        source.SetRenderScaling(sourceScale);
        overlay.SetRenderScaling(overlayScale);
        source.Position = new PixelPoint(100, 80);
        overlay.Position = new PixelPoint(24, 32);
        try
        {
            source.UpdateLayout(); overlay.UpdateLayout();
            var button = launcher.FindControl<Button>("PowerButton")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.UpdateLayout();
            var first = menu.Margin;
            var anchor = overlay.PointToClient(button.PointToScreen(new Point(button.Bounds.Width, 0)));
            var expectedX = Math.Clamp(anchor.X - menu.Bounds.Width, 6, Math.Max(6, overlay.Bounds.Width - menu.Bounds.Width - 6));
            var expectedY = Math.Clamp(anchor.Y - menu.Bounds.Height, 6, Math.Max(6, overlay.Bounds.Height - menu.Bounds.Height - 6));
            Assert.InRange(first.Left, expectedX - 1, expectedX + 1);
            Assert.InRange(first.Top, expectedY - 1, expectedY + 1);
            Assert.True(first.Left > 6, $"Expected an anchor near the launcher, got {first}.");
            Assert.True(first.Top > 6, $"Expected an anchor above the power button, got {first}.");
            for (var i = 0; i < 5; i++)
            {
                launcher.ClosePowerMenu();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                overlay.UpdateLayout();
                Assert.Equal(first, menu.Margin);
                Assert.True(menu.Bounds.Width <= 184);
                Assert.True(menu.Bounds.Height < 300);
            }
        }
        finally { overlay.Close(); source.Close(); Dispatcher.UIThread.RunJobs(); }
    }
}
