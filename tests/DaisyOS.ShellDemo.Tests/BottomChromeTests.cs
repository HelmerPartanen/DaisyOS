using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using DaisyOS.Shell.Services.Shell;
using DaisyOS.Shell.Views;
using DaisyOS.Shell.Views.Components.Desktop;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class BottomChromeTests
{
    [Fact]
    public async Task BottomAutoHideMigratesLegacyPreferencesAndPersistsOneChoice()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daisy-media-hide-" + Guid.NewGuid());
        var path = Path.Combine(directory, "appearance.json");
        var store = new DaisyOS.Shell.Services.Appearance.AppearanceSettingsStore(path);
        try
        {
            await store.SaveAsync(new DaisyOS.Shell.Services.Appearance.AppearanceSettings
            {
                TaskbarAutoHide = false, SystemBarAutoHide = true, MediaWidgetAutoHide = true
            });
            var settings = store.Load();
            Assert.True(settings.MediaWidgetAutoHide);
            Assert.True(settings.TaskbarAutoHide);
            Assert.True(settings.SystemBarAutoHide);
            await File.WriteAllTextAsync(path, "{\"TaskbarAutoHide\":true}");
            Assert.True(store.Load().MediaWidgetAutoHide);
            await store.SaveAsync(new DaisyOS.Shell.Services.Appearance.AppearanceSettings
            { BottomComponentsAutoHide = false, TaskbarAutoHide = true, SystemBarAutoHide = true, MediaWidgetAutoHide = true });
            var disabled = store.Load();
            Assert.False(disabled.TaskbarAutoHide); Assert.False(disabled.SystemBarAutoHide); Assert.False(disabled.MediaWidgetAutoHide);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task TooltipStaysAboveBarsAndCannotInterceptClicks()
    {
        var button = new Button { Content = "Steam", Width = 60, Height = 40 };
        ToolTip.SetTip(button, "Steam");
        var bar = new UserControl { Content = button, Height = 52, Margin = new Thickness(0, 0, 0, 3), VerticalAlignment = VerticalAlignment.Bottom };
        var tips = new BottomChromeTooltips();
        tips.Attach(bar);
        var window = new Window { Width = 600, Height = 240, Content = new Grid { Children = { bar, tips } } };
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        window.Show();
        try
        {
            using (window.CaptureRenderedFrame()) { }
            var point = button.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseMove(point);
            await Task.Delay(500);
            using (window.CaptureRenderedFrame()) { }
            var card = tips.GetVisualChildren().OfType<Border>().Single();
            Assert.True(card.IsVisible);
            Assert.False(tips.IsHitTestVisible);
            Assert.False(card.IsHitTestVisible);
            Assert.Equal(int.MaxValue, ToolTip.GetShowDelay(button));
            Assert.False(ToolTip.GetIsOpen(button));
            Assert.Equal(3, bar.TranslatePoint(default, window)!.Value.Y - card.Bounds.Bottom, 3);
            var nativeTip = new ToolTip { Content = "Steam" };
            ((Grid)window.Content!).Children.Add(nativeTip);
            nativeTip.ApplyTemplate();
            Assert.Equal(nativeTip.FontSize, ((TextBlock)card.Child!).FontSize);
            Assert.Equal(nativeTip.FontFamily, ((TextBlock)card.Child!).FontFamily);
            ((Grid)window.Content!).Children.Remove(nativeTip);
            ToolTip.SetTip(button, "Steam — Not responding");
            await Task.Delay(450);
            Assert.Equal("Steam — Not responding", ((TextBlock)card.Child!).Text);
            window.MouseDown(point, MouseButton.Left);
            Assert.False(card.IsVisible);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal(1, clicks);
            window.MouseMove(new Point(5, 5));
            Assert.False(card.IsVisible);
        }
        finally { window.Close(); }

        var options = LayerShellSurfaceOptions.BottomTooltipOverlay();
        Assert.True(options.InputPassthrough);
        Assert.Equal(LayerShellKeyboardInteractivity.None, options.KeyboardInteractivity);
        Assert.Equal(-1, options.ExclusiveZone);
    }

    [AvaloniaTheory]
    [InlineData(0.15)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void EachComponentRevealsIndependentlyAndMenusHoldItOpen(double position)
    {
        var shell = new ShellView();
        shell.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        shell.Resources["MotionDurationShellExit"] = TimeSpan.Zero;
        var desktop = shell.FindControl<DesktopView>("Desktop")!;
        desktop.ViewModel.BottomMediaWidgetVisible = true;
        shell.SetBottomBarAutoHide(true, true, true);
        Assert.True(shell.CanReleaseBottomWorkArea);
        Assert.True(shell.IsBottomPanelInputPassthrough);
        shell.RevealBottomBarAt(position * 1280, 1280);
        Assert.Equal(position == 0.15, shell.MediaWidget.IsVisible);
        Assert.Equal(position == 0.5, shell.Taskbar.IsVisible);
        Assert.Equal(position == 0.9, shell.SystemBar.IsVisible);
        shell.RevealBottomBarAt(0.15 * 1280, 1280);
        Assert.True(shell.MediaWidget.IsVisible);
        shell.MediaWidget.ShowContextMenu();
        var hide = typeof(ShellView).GetMethod("HideMediaIfUnhovered",
            global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!;
        hide.Invoke(shell, null);
        Assert.True(shell.MediaWidget.IsVisible);
        shell.MediaWidget.HideContextMenu();
        hide.Invoke(shell, null);
        Assert.False(shell.MediaWidget.IsVisible);
        Assert.Equal(position == 0.5, shell.Taskbar.IsVisible);
        Assert.Equal(position == 0.9, shell.SystemBar.IsVisible);
        shell.SetBottomBarAutoHide(true, true, false);
        Assert.True(shell.MediaWidget.IsVisible);
        Assert.False(shell.CanReleaseBottomWorkArea);
        shell.SetExternalFullscreenActive(true);
        Assert.False(shell.MediaWidget.IsVisible);
        shell.RevealBottomBarAt(0.15 * 1280, 1280);
        Assert.False(shell.MediaWidget.IsVisible);
        shell.SetExternalFullscreenActive(false);
        Assert.True(shell.MediaWidget.IsVisible);
        shell.SetBottomBarAutoHide(false, false, false);
        desktop.ViewModel.BottomMediaWidgetVisible = false;
    }

    [AvaloniaFact]
    public void LockScreenSuppressesAllBottomChromeAndEdgeReveal()
    {
        var shell = new ShellView();
        shell.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        shell.Resources["MotionDurationShellExit"] = TimeSpan.Zero;
        shell.FindControl<DesktopView>("Desktop")!.ViewModel.BottomMediaWidgetVisible = true;
        shell.SetBottomBarAutoHide(false, false, false);

        Assert.True(shell.Taskbar.IsVisible);
        Assert.True(shell.SystemBar.IsVisible);
        Assert.True(shell.MediaWidget.IsVisible);

        shell.SetLockScreenActive(true);

        Assert.False(shell.Taskbar.IsVisible);
        Assert.False(shell.SystemBar.IsVisible);
        Assert.False(shell.MediaWidget.IsVisible);
        Assert.True(shell.IsBottomPanelInputPassthrough);
        Assert.False(shell.IsBottomEdgeSensorEnabled);
        shell.RevealBottomBarAt(640, 1280);
        Assert.True(shell.IsBottomPanelInputPassthrough);

        shell.SetLockScreenActive(false);

        Assert.True(shell.Taskbar.IsVisible);
        Assert.True(shell.SystemBar.IsVisible);
        Assert.True(shell.MediaWidget.IsVisible);
    }

    [AvaloniaFact]
    public void ClosingLauncherAutoHidesTaskbarAndSystemBarWhenEnabled()
    {
        var shell = new ShellView();
        shell.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        shell.Resources["MotionDurationShellExit"] = TimeSpan.Zero;
        shell.Launcher.Resources["MotionDurationShellEnter"] = TimeSpan.FromMilliseconds(200);
        shell.Launcher.Resources["MotionDurationShellExit"] = TimeSpan.FromMilliseconds(200);

        shell.SetBottomBarAutoHide(true, true, true);
        Assert.False(shell.Taskbar.IsVisible);
        Assert.False(shell.SystemBar.IsVisible);
        Assert.True(shell.IsBottomPanelInputPassthrough);

        // Open Launcher: both bars reveal while Launcher is open.
        shell.ShowLauncher();
        shell.SetLauncherOpen(true);
        Assert.True(shell.IsLauncherVisible);
        Assert.True(shell.Taskbar.IsVisible);
        Assert.True(shell.SystemBar.IsVisible);

        // Close Launcher with animated exit (exact App.HideLauncher order):
        // HideLauncher runs first so IsLauncherVisible is already false when
        // SetLauncherOpen(false) evaluates auto-hide state.
        shell.HideLauncher(immediate: false);
        Assert.False(shell.IsLauncherVisible);
        shell.SetLauncherOpen(false);

        Assert.False(shell.Taskbar.IsVisible);
        Assert.False(shell.SystemBar.IsVisible);
        Assert.True(shell.IsBottomPanelInputPassthrough);

        // Selective auto-hide: only the auto-hidden bar hides on Launcher close.
        shell.HideLauncher(immediate: true);
        shell.SetBottomBarAutoHide(taskbarAutoHide: true, systemBarAutoHide: false);
        Assert.False(shell.Taskbar.IsVisible);
        Assert.True(shell.SystemBar.IsVisible);

        shell.ShowLauncher();
        shell.SetLauncherOpen(true);
        Assert.True(shell.Taskbar.IsVisible);
        Assert.True(shell.SystemBar.IsVisible);

        shell.HideLauncher(immediate: false);
        shell.SetLauncherOpen(false);
        Assert.False(shell.Taskbar.IsVisible);
        Assert.True(shell.SystemBar.IsVisible);

        shell.HideLauncher(immediate: true);
    }
}
