using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.Shell.Services.Shell;
using DaisyOS.Shell.Views;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class SettingsShortcutTests
{
    [Theory]
    [InlineData("Appearance", null, "Wallpaper")]
    [InlineData("Taskbar", "AUTO-HIDE", null)]
    [InlineData("Taskbar", "LAYOUT", "Taskbar position")]
    [InlineData("Desktop", null, null)]
    public void SettingsDestinationsSurviveProcessAndSingleInstanceActivation(string category, string? section, string? setting)
    {
        var target = new SettingsDestination(category, section, setting);
        Assert.Equal(target, SettingsDestination.FromArguments(target.ToArguments()));
        Assert.Null(SettingsDestination.FromArguments([]));
    }

    [AvaloniaFact]
    public void WallpaperShortcutSelectsAndScrollsToTheWallpaperControlInAnExistingWindow()
    {
        var window = new SettingsWindow { Width = 1000, Height = 600 };
        window.Show();
        try
        {
            var view = window.FindControl<SettingsView>("SettingsContent")!;
            window.Navigate(new("Appearance", Setting: "Wallpaper"));
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(view.FindControl<ScrollViewer>("AppearanceView")!.IsVisible);
            var picker = view.FindControl<ComboBox>("WallpaperTypePicker")!;
            Assert.Equal(picker, window.FocusManager!.GetFocusedElement());
            var scroll = view.FindControl<ScrollViewer>("AppearanceView")!;
            var location = picker.TranslatePoint(default, scroll)!.Value;
            Assert.True(location.Y >= 0 && location.Y + picker.Bounds.Height <= scroll.Bounds.Height);
            window.Navigate(new("Taskbar", "AUTO-HIDE"));
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var toggle = view.GetVisualDescendants().OfType<ToggleSwitch>().Single(c => c.Name == "TaskbarAutoHideToggle");
            Assert.Equal(toggle, window.FocusManager.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(DockEdge.Bottom, 1d)]
    [InlineData(DockEdge.Top, 1d)]
    [InlineData(DockEdge.Left, 1d)]
    [InlineData(DockEdge.Right, 1d)]
    [InlineData(DockEdge.Bottom, .85)]
    [InlineData(DockEdge.Top, .85)]
    [InlineData(DockEdge.Left, .85)]
    [InlineData(DockEdge.Right, .85)]
    public void EmptyTaskbarMenuUsesSettingsDestinationsAndDismissesOnSelection(DockEdge edge, double scale)
    {
        var shell = new ShellView();
        var panel = shell.DetachDesktopChromeForNativePanel();
        shell.ApplyTaskbarPreferences(new TaskbarPreferences { Edge = edge }, 1);
        shell.Taskbar.Resources["TaskbarScale"] = scale;
        var overlay = (BottomChromeOverlayHost)shell.SystemBar.DetachFlyoutsForNativeOverlay();
        var menu = shell.Taskbar.DetachContextMenuForNativeOverlay()!;
        overlay.Children.Add(menu);
        var vertical = DockGeometry.IsVertical(edge);
        var owner = new Window { Width = vertical ? 82 : 1200, Height = vertical ? 900 : 82, Content = panel };
        var window = new Window { Width = 1200, Height = 900, Content = overlay };
        owner.Show(); window.Show();
        try
        {
            owner.UpdateLayout(); window.UpdateLayout();
            SettingsDestination? selected = null;
            shell.Taskbar.SettingsRequested += (_, target) => selected = target;
            var expected = new[] { new SettingsDestination("Taskbar"), new("Taskbar", "LAYOUT"), new("Taskbar", "AUTO-HIDE"), new("Desktop") };
            foreach (var (target, index) in expected.Select((value, index) => (value, index)))
            {
                var body = shell.Taskbar.FindControl<Border>("TaskbarBody")!;
                var position = body.TranslatePoint(new Point(2, 2), owner)!.Value;
                using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
                body.RaiseEvent(new PointerPressedEventArgs(body, pointer, owner, position, 0,
                    new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed), KeyModifiers.None));
                window.UpdateLayout();
                if (edge == DockEdge.Right) position += new Vector(window.ClientSize.Width - owner.ClientSize.Width, 0);
                if (edge == DockEdge.Bottom) position += new Vector(0, window.ClientSize.Height - owner.ClientSize.Height);
                var cartouche = (CartoucheBorder)menu;
                if (vertical) Assert.InRange(Math.Abs(menu.Bounds.Y + cartouche.TailY - position.Y), 0, 1);
                else Assert.InRange(Math.Abs(menu.Bounds.X + cartouche.TailX - position.X), 0, 1);
                Assert.True(shell.Taskbar.IsContextMenuVisible);
                var items = menu.GetVisualDescendants().OfType<MenuItem>().ToArray();
                Assert.Equal(4, items.Length);
                items[index].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal(target, selected);
                Assert.False(shell.Taskbar.IsContextMenuVisible);
            }
        }
        finally { owner.Close(); window.Close(); }
    }
}
