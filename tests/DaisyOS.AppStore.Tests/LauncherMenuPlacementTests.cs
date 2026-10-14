using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DaisyOS.Shell.Views.Components.Launcher;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LauncherMenuPlacementTests
{
    [AvaloniaFact]
    public void DetachedPowerMenuRetainsItsAnchorAcrossRepeatedOpens()
    {
        var launcher = new LauncherView { IsVisible = true };
        var source = new Window { Width = 1280, Height = 800, Content = launcher };
        var menu = (Border)launcher.DetachPowerMenuForNativeOverlay()!;
        var panel = new Panel(); panel.Children.Add(menu);
        var overlay = new Window { Width = 1280, Height = 800, Content = panel };
        source.Show(); overlay.Show();
        try
        {
            source.UpdateLayout(); overlay.UpdateLayout();
            var button = launcher.FindControl<Button>("PowerButton")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.UpdateLayout();
            var first = menu.Margin;
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
