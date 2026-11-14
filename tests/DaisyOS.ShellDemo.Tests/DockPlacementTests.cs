using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Shell.Services.Shell;
using DaisyOS.Shell.Views;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class DockPlacementTests
{
    [Theory]
    [InlineData(DockEdge.Bottom, false)]
    [InlineData(DockEdge.Top, false)]
    [InlineData(DockEdge.Left, true)]
    [InlineData(DockEdge.Right, true)]
    public void NativeGeometryUsesTheSelectedEdgeAndAStretchedLongAxis(DockEdge edge, bool vertical)
    {
        var panel = DockGeometry.AtEdge(LayerShellSurfaceOptions.PrimaryPanel(58), edge, 82);
        panel.Validate();
        Assert.Equal(vertical ? 82u : 0u, panel.Width);
        Assert.Equal(vertical ? 0u : 82u, panel.Height);
        Assert.Equal(58, panel.ExclusiveZone);
        Assert.Equal(3, global::System.Numerics.BitOperations.PopCount((uint)panel.Anchor));
        var sensor = DockGeometry.AtEdge(LayerShellSurfaceOptions.BottomEdgeSensor(), edge, 2);
        Assert.Equal(vertical ? 2u : 0u, sensor.Width);
        Assert.Equal(vertical ? 0u : 2u, sensor.Height);
        Assert.Equal(0, sensor.ExclusiveZone);
        Assert.True(sensor.InputPassthrough);
    }

    [AvaloniaTheory]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Right)]
    public void ComponentsStayUprightInsideTheReservedStripWithoutOverlapping(DockEdge edge)
    {
        var shell = new ShellView();
        shell.Desktop.ViewModel.BottomMediaWidgetVisible = true;
        var panel = shell.DetachDesktopChromeForNativePanel();
        shell.ApplyTaskbarPreferences(new TaskbarPreferences { Edge = edge }, 1);
        shell.SetBottomBarAutoHide(false, false, false);
        var vertical = DockGeometry.IsVertical(edge);
        var window = new Window { Width = vertical ? shell.DockReservedExtent + 24 : 1440, Height = vertical ? 1080 : 82, Content = panel, Background = Brushes.DarkSlateBlue };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var components = new Control[] { shell.MediaWidget, shell.Taskbar, shell.SystemBar };
            var rectangles = components.Select(c => new Rect(c.TranslatePoint(default, panel)!.Value, c.Bounds.Size)).ToArray();
            foreach (var rect in rectangles)
            {
                Assert.True(rect.Width > 0 && rect.Height > 0);
                Assert.True(rect.X >= 0 && rect.Y >= 0 && rect.Right <= panel.Bounds.Width + 1 && rect.Bottom <= panel.Bounds.Height + 1, $"{edge}: {rect} in {panel.Bounds}");
            }
            for (var a = 0; a < rectangles.Length; a++)
                for (var b = a + 1; b < rectangles.Length; b++)
                    Assert.False(rectangles[a].Intersects(rectangles[b]), $"{edge}: overlapping components");
            var body = shell.Taskbar.FindControl<Border>("TaskbarBody")!;
            Assert.Equal(vertical ? Orientation.Vertical : Orientation.Horizontal, shell.Taskbar.FindControl<StackPanel>("DockItems")!.Orientation);
            if (vertical)
            {
                Assert.True(body.Bounds.Height > 100, $"Vertical taskbar was collapsed to {body.Bounds}");
                Assert.True(shell.SystemBar.FindControl<Border>("SystemBarFrame")!.Bounds.Height > 100);
            }
            Assert.Equal(vertical ? 52 : body.Bounds.Width, body.Bounds.Width);
            Assert.Equal(vertical ? body.Bounds.Height : 52, body.Bounds.Height);
            var input = shell.GetDockInputRectangles(window);
            Assert.Equal(3, input.Length);
            Assert.True(input.Sum(r => r.Width * r.Height) < window.ClientSize.Width * window.ClientSize.Height);
            if (vertical)
            {
                Assert.True(shell.SystemBar.FindControl<StackPanel>("QuickStatusItems")!.IsVisible);
                Assert.Equal(Orientation.Vertical, shell.SystemBar.FindControl<StackPanel>("QuickStatusItems")!.Orientation);
                Assert.Equal(Orientation.Vertical, shell.SystemBar.FindControl<StackPanel>("CalendarValues")!.Orientation);
                var systemFrame = shell.SystemBar.FindControl<Border>("SystemBarFrame")!;
                Assert.Equal(52, systemFrame.Bounds.Width);
                Assert.Equal((CornerRadius)shell.SystemBar.FindResource("RoundedXL")!, systemFrame.CornerRadius);
                foreach (var name in new[] { "AppTrayButton", "NotificationsButton" })
                {
                    var button = shell.SystemBar.FindControl<Button>(name)!;
                    var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, 0), systemFrame)!.Value.X;
                    Assert.InRange(Math.Abs(centre - systemFrame.Bounds.Width / 2), 0, 1);
                }
                Assert.DoesNotContain("\n", shell.SystemBar.FindControl<TextBlock>("ClockText")!.Text ?? "");
                Assert.Equal(edge == DockEdge.Left ? "caret-right" : "caret-left", shell.SystemBar.FindControl<DaisyIcon>("AppTrayChevron")!.Icon);
                var battery = shell.SystemBar.FindControl<BatteryStatusView>("SystemBarBattery")!;
                Assert.False(battery.ShowIcon);
                Assert.DoesNotMatch(@"[A-Za-z]", shell.SystemBar.FindControl<TextBlock>("DateText")!.Text ?? "");
                battery.IsVisible = true; battery.ChargePercent = 68;
                shell.SystemBar.FindControl<TextBlock>("DateText")!.IsVisible = true;
                shell.SystemBar.FindControl<TextBlock>("DateText")!.Text = "7.1";
                shell.SystemBar.FindControl<TextBlock>("ClockText")!.Text = "9.41";
                shell.SystemBar.FindControl<DaisyIcon>("SystemBarBluetoothIcon")!.IsVisible = true;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            }
            using var frame = window.CaptureRenderedFrame();
            frame!.Save($"/tmp/daisy-dock-{edge}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 760)]
    [InlineData(true, 1000)]
    public void TaskbarPageOwnsItsControlsAndUsesDesktopSvg(bool dark, int width)
    {
        var window = new SettingsWindow { Width = width, Height = 900, RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light };
        window.Show();
        try
        {
            window.SelectCategory("Taskbar"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var view = window.FindControl<SettingsView>("SettingsContent")!;
            var content = view.FindControl<StackPanel>("CategoryContent")!;
            Assert.Equal(9, content.GetVisualDescendants().OfType<ToggleSwitch>().Count());
            Assert.Equal(3, content.GetVisualDescendants().OfType<ComboBox>().Count());
            Assert.Single(content.GetVisualDescendants().OfType<QuickSettingsSlider>());
            var nav = view.FindControl<Button>("NavTaskbar")!;
            Assert.Equal("desktop", Assert.Single(nav.GetVisualDescendants().OfType<DaisyIcon>()).Icon);
            Assert.True(nav.Classes.Contains("Active"));
            foreach (var picker in content.GetVisualDescendants().OfType<ComboBox>())
            {
                var point = picker.TranslatePoint(default, content)!.Value;
                Assert.True(point.X >= 0 && point.X + picker.Bounds.Width <= content.Bounds.Width + 1);
            }
            using var frame = window.CaptureRenderedFrame();
            frame!.Save($"/tmp/daisy-taskbar-settings-{dark}-{width}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            window.SelectCategory("Appearance"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Empty(view.FindControl<StackPanel>("AppearanceContent")!.GetVisualDescendants().OfType<QuickSettingsSlider>().Where(c => c.Name == "TaskbarSizeSlider"));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void AutoHideRevealsOnlyTheComponentUnderThePointerAndIgnoresGaps(DockEdge edge)
    {
        var shell = new ShellView();
        shell.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        shell.Resources["MotionDurationShellExit"] = TimeSpan.Zero;
        shell.Desktop.ViewModel.BottomMediaWidgetVisible = true;
        var panel = (DockComponentsPanel)shell.DetachDesktopChromeForNativePanel();
        shell.ApplyTaskbarPreferences(new TaskbarPreferences { Edge = edge }, 1);
        var vertical = DockGeometry.IsVertical(edge);
        var window = new Window { Width = vertical ? shell.DockReservedExtent + 24 : 1440, Height = vertical ? 1080 : 82, Content = panel };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var components = new Control[] { shell.MediaWidget, shell.Taskbar, shell.SystemBar };
            var extent = vertical ? panel.Bounds.Height : panel.Bounds.Width;
            for (var target = 0; target < 3; target++)
            {
                shell.SetBottomBarAutoHide(true, true, true);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var slot = panel.GetComponentSlot(target);
                var coordinate = vertical ? slot.Y + slot.Height / 2 : slot.X + slot.Width / 2;
                shell.RevealBottomBarAt(coordinate, extent);
                for (var index = 0; index < components.Length; index++) Assert.Equal(index == target, components[index].IsVisible);
            }
            shell.SetBottomBarAutoHide(true, true, true);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var sensorSize = vertical ? new Size(2, extent) : new Size(extent, 2);
            var reveal = shell.GetDockRevealRectangles(sensorSize);
            Assert.Equal(3, reveal.Length);
            for (var coordinate = 0d; coordinate <= extent; coordinate += 5)
            {
                if (panel.ComponentAtEdgePosition(coordinate, extent) != -1) continue;
                var point = vertical ? new Point(1, coordinate) : new Point(coordinate, 1);
                Assert.DoesNotContain(reveal, rect => rect.Contains(point));
                shell.RevealBottomBarAt(coordinate, extent);
                Assert.All(components, component => Assert.False(component.IsVisible));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ComponentFlagsAndAlignmentApplyToTheActualDock()
    {
        var shell = new ShellView();
        var panel = shell.DetachDesktopChromeForNativePanel();
        shell.Desktop.ViewModel.BottomMediaWidgetVisible = false;
        shell.ApplyTaskbarPreferences(new TaskbarPreferences { ShowSystemBar = false, ShowLauncherButton = false,
            ShowWindowIndicators = false, ShowTooltips = false, ShowWindowPreviews = false, LockIconOrder = true, Alignment = DockAlignment.Start }, 1);
        shell.SetBottomBarAutoHide(false, false, false);
        var window = new Window { Width = 1440, Height = 82, Content = panel };
        window.Show();
        try
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.False(shell.SystemBar.IsVisible); Assert.False(shell.MediaWidget.IsVisible);
            Assert.False(shell.Taskbar.FindControl<Button>("StartButton")!.IsVisible);
            Assert.False(((BottomChromeTooltips)shell.DetachBottomTooltipsForNativeOverlay()).Enabled);
            var start = shell.Taskbar.Bounds.X;
            shell.ApplyTaskbarPreferences(new TaskbarPreferences { Alignment = DockAlignment.End }, 1);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.True(shell.Taskbar.Bounds.X > start);
            Assert.True(shell.Taskbar.FindControl<Button>("StartButton")!.IsVisible);
            Assert.True(shell.SystemBar.IsVisible);
            Assert.Equal(2, shell.GetDockInputRectangles(window).Length);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CrowdedVerticalDockScrollsWithoutShrinkingIconsBelowReadableSize()
    {
        var bar = new DaisyOS.Shell.Views.Components.Taskbar.TaskbarView();
        bar.ApplyDockPreferences(new TaskbarPreferences { Edge = DockEdge.Left });
        for (var i = 0; i < 40; i++) bar.PinApp("vertical-fit-test-" + i);
        var window = new Window { Width = 82, Height = 450, Content = bar };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var scroll = bar.FindControl<ScrollViewer>("DockIconScroll")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.All(bar.FindControl<Canvas>("AppIconsCanvas")!.Children.OfType<Button>(), button => Assert.True(button.Height >= 26));
            scroll.Offset = new Vector(0, 100);
            Dispatcher.UIThread.RunJobs();
            Assert.True(scroll.Offset.Y > 0);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Right)]
    public void ContextMenusOpenIntoTheWorkspace(DockEdge edge)
    {
        var anchor = new Border { Width = 40, Height = 40,
            HorizontalAlignment = edge == DockEdge.Left ? HorizontalAlignment.Left : edge == DockEdge.Right ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            VerticalAlignment = edge == DockEdge.Top ? VerticalAlignment.Top : edge == DockEdge.Bottom ? VerticalAlignment.Bottom : VerticalAlignment.Center };
        var popup = new CartoucheBorder { IsVisible = false, Width = 180, Child = new TextBlock { Text = "Context menu", Height = 80 } };
        var root = new Grid { Children = { anchor, popup } };
        var window = new Window { Width = 640, Height = 480, Content = root };
        window.Show();
        try
        {
            window.UpdateLayout(); DockPopupPlacement.Position(popup, anchor, edge, nativePanel: false);
            window.UpdateLayout();
            Assert.False(anchor.Bounds.Intersects(popup.Bounds));
            Assert.True(popup.Bounds.X >= 0 && popup.Bounds.Y >= 0 && popup.Bounds.Right <= root.Bounds.Width && popup.Bounds.Bottom <= root.Bounds.Height);
            Assert.Equal(8, popup.TailHeight);
            Assert.Equal(edge switch { DockEdge.Left => CartoucheTailEdge.Left, DockEdge.Right => CartoucheTailEdge.Right, DockEdge.Top => CartoucheTailEdge.Top, _ => CartoucheTailEdge.Bottom }, popup.TailEdge);
            Assert.Equal(edge == DockEdge.Top, popup.TailOnTop);
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task PreferencesPersistAndInvalidValuesNormalize()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store = new AppearanceSettingsStore(path);
        try
        {
            await store.SaveAsync(new AppearanceSettings { Taskbar = new TaskbarPreferences { Edge = DockEdge.Right, Alignment = DockAlignment.End, ShowSystemBar = false, ShowTooltips = false, HideDelayMilliseconds = 2000, LockIconOrder = true } });
            var loaded = store.Load().Taskbar;
            Assert.Equal(DockEdge.Right, loaded.Edge); Assert.Equal(DockAlignment.End, loaded.Alignment);
            Assert.False(loaded.ShowSystemBar); Assert.False(loaded.ShowTooltips); Assert.True(loaded.LockIconOrder);
            Assert.Equal(2000, loaded.HideDelayMilliseconds);
            var invalid = new TaskbarPreferences { Edge = (DockEdge)99, Alignment = (DockAlignment)99, HideDelayMilliseconds = -1 }.Normalize();
            Assert.Equal(DockEdge.Bottom, invalid.Edge); Assert.Equal(DockAlignment.Center, invalid.Alignment); Assert.Equal(300, invalid.HideDelayMilliseconds);
        }
        finally { File.Delete(path); }
    }
    [AvaloniaTheory]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void DetachedMenusStayAtTheirAnchorAcrossRepeatedOpeningsAndContentChanges(DockEdge edge)
    {
        var vertical = DockGeometry.IsVertical(edge);
        var anchor = new Border { Width = 40, Height = 40 };
        var strip = new Canvas { Children = { anchor } };
        Canvas.SetLeft(anchor, vertical ? 24 : 420);
        Canvas.SetTop(anchor, vertical ? 280 : 24);
        var label = new TextBlock { Text = "Menu", Height = 80 };
        var popup = new CartoucheBorder { Width = 180, Child = label, IsVisible = false };
        var overlay = new Grid { Children = { popup } };
        var owner = new Window { Width = vertical ? 82 : 960, Height = vertical ? 720 : 82, Content = strip };
        var window = new Window { Width = 960, Height = 720, Content = overlay };
        owner.Show(); window.Show();
        try
        {
            owner.UpdateLayout(); window.UpdateLayout();
            Rect? previous = null;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                DockPopupPlacement.Position(popup, anchor, edge);
                window.UpdateLayout();
                if (previous is { } before) Assert.Equal(before, popup.Bounds);
                previous = popup.Bounds;
                popup.IsVisible = false;
            }
            label.Height = 180;
            DockPopupPlacement.Position(popup, anchor, edge);
            window.UpdateLayout();
            var anchorPoint = anchor.TranslatePoint(default, owner)!.Value;
            if (edge == DockEdge.Right) anchorPoint += new Vector(878, 0);
            if (edge == DockEdge.Bottom) anchorPoint += new Vector(0, 638);
            var expected = DockPopupPlacement.Fit(new Rect(anchorPoint, anchor.Bounds.Size), popup.Bounds.Size, window.ClientSize, edge);
            Assert.Equal(expected, popup.Bounds);
            Assert.InRange(popup.Bounds.Right, 0, 954);
            Assert.InRange(popup.Bounds.Bottom, 0, 714);
            var restingBounds = popup.Bounds;
            for (var frameIndex = 0; frameIndex < 4; frameIndex++)
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(restingBounds, popup.Bounds);
            }
            strip.RenderTransform = new TranslateTransform(DockGeometry.Slide(edge, 8.4).X, DockGeometry.Slide(edge, 8.4).Y);
            owner.UpdateLayout(); window.UpdateLayout();
            DockPopupPlacement.Position(popup, anchor, edge);
            window.UpdateLayout();
            Assert.Equal(restingBounds, popup.Bounds);
            strip.RenderTransform = null;
            owner.UpdateLayout(); window.UpdateLayout();
            Assert.Equal(restingBounds, popup.Bounds);
        }
        finally { owner.Close(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(DockEdge.Bottom, 0)]
    [InlineData(DockEdge.Top, 0)]
    [InlineData(DockEdge.Left, 0)]
    [InlineData(DockEdge.Right, 0)]
    [InlineData(DockEdge.Bottom, 1)]
    [InlineData(DockEdge.Top, 1)]
    [InlineData(DockEdge.Left, 1)]
    [InlineData(DockEdge.Right, 1)]
    [InlineData(DockEdge.Bottom, 2)]
    [InlineData(DockEdge.Top, 2)]
    [InlineData(DockEdge.Left, 2)]
    [InlineData(DockEdge.Right, 2)]
    public void SystemFlyoutsFollowTheirOwnButtonsAndAnimateFromTheirDockEdge(DockEdge edge, int taskbarSize)
    {
        var shell = new ShellView();
        shell.ApplyTaskbarPreferences(new TaskbarPreferences { Edge = edge }, taskbarSize);
        var strip = shell.DetachDesktopChromeForNativePanel();
        var overlay = (BottomChromeOverlayHost)shell.SystemBar.DetachFlyoutsForNativeOverlay(shell.DockReservedExtent);
        var vertical = DockGeometry.IsVertical(edge);
        var owner = new Window { Width = vertical ? shell.DockReservedExtent + 24 : 1200, Height = vertical ? 900 : shell.DockReservedExtent + 24, Content = strip };
        var window = new Window { Width = 1200, Height = 900, Content = overlay };
        owner.Show(); window.Show();
        try
        {
            owner.UpdateLayout(); window.UpdateLayout();
            Assert.Equal(DockGeometry.Inset(edge, shell.DockReservedExtent), overlay.Viewport.Margin);
            var scenarios = new (string Panel, string Button, Action Open, Action Close)[]
            {
                ("AppTrayPanel", "AppTrayButton", shell.SystemBar.ShowAppTrayPanel, () => shell.SystemBar.HideAppTrayPanel(true)),
                ("QuickSettingsPanel", "QuickSettingsButton", shell.SystemBar.ShowQuickSettingsPanel, () => shell.SystemBar.HideQuickSettingsPanel(true)),
                ("NotificationDrawer", "NotificationsButton", shell.SystemBar.ShowNotificationPanel, () => shell.SystemBar.HideNotificationPanel(true)),
                ("CalendarPanel", "CalendarButton", shell.SystemBar.ShowCalendarPanel, () => shell.SystemBar.HideCalendarPanel(true))
            };
            foreach (var scenario in scenarios)
            {
                var panel = overlay.Viewport.Children.Single(c => c.Name == scenario.Panel);
                panel.Resources["MotionDurationShellEnter"] = TimeSpan.FromSeconds(1);
                scenario.Open();
                Assert.True(panel.Bounds.Width > 0 && panel.Bounds.Height > 0, "Flyout animation started before its final layout was arranged.");
                var openingBounds = panel.Bounds;
                window.UpdateLayout();
                Assert.Equal(openingBounds, panel.Bounds);
                var button = shell.SystemBar.FindControl<Button>(scenario.Button)!;
                var point = button.TranslatePoint(default, owner)!.Value;
                if (edge == DockEdge.Right) point += new Vector(window.ClientSize.Width - owner.ClientSize.Width, 0);
                if (edge == DockEdge.Bottom) point += new Vector(0, window.ClientSize.Height - owner.ClientSize.Height);
                var origin = overlay.Viewport.TranslatePoint(default, window)!.Value;
                var expected = DockPopupPlacement.Fit(new Rect(point - (Vector)origin, button.Bounds.Size), panel.Bounds.Size, overlay.Viewport.Bounds.Size, edge);
                Assert.InRange(Math.Abs(panel.Bounds.X - expected.X), 0, 1);
                Assert.InRange(Math.Abs(panel.Bounds.Y - expected.Y), 0, 1);
                var motion = scenario.Panel switch
                {
                    "AppTrayPanel" => shell.SystemBar.AppTrayMaterialFrame,
                    "QuickSettingsPanel" => shell.SystemBar.QuickSettingsMaterialFrame,
                    "NotificationDrawer" => shell.SystemBar.NotificationMaterialFrame,
                    _ => panel
                };
                var transform = motion.RenderTransform?.Value ?? Matrix.Identity;
                if (edge == DockEdge.Top) Assert.True(transform.M32 < 0);
                if (edge == DockEdge.Bottom) Assert.True(transform.M32 > 0);
                if (edge == DockEdge.Left) Assert.True(transform.M31 < 0);
                if (edge == DockEdge.Right) Assert.True(transform.M31 > 0);
                for (var frameIndex = 0; frameIndex < 3; frameIndex++)
                {
                    using var rendered = window.CaptureRenderedFrame();
                    Assert.NotNull(rendered);
                }
                scenario.Close();
            }
        }
        finally { owner.Close(); window.Close(); }
    }

}
