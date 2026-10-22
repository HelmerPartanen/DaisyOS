using System.Linq;
using System.Reflection;
using Avalonia.Headless;
using DaisyOS.Core.Models;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DaisyOS.Shell.Views.Components.SystemBar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class QuickSettingsTests
{
    [AvaloniaFact]
    public async Task AirPodsControlsShowConfirmedModeCapabilitiesAndRestoreBluetooth()
    {
        var panel = new DaisyOS.Shell.Views.Components.QuickSettings.QuickSettings();
        var window = new Window { Width = 340, SizeToContent = SizeToContent.Height, Content = panel,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (panel.GetType().GetField("_statusRefreshTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel) is Task refresh)
                await refresh;
            panel.GetType().GetField("_bluetoothAvailable", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, true);
            panel.FindControl<DaisyOS.Shell.Controls.QuickSettingTile>("WifiTile")!.IsVisible = false;
            panel.FindControl<DaisyOS.Shell.Controls.QuickSettingTile>("EthernetTile")!.IsVisible = true;
            var apply = panel.GetType().GetMethod("ApplyAirPodsStatus", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var status = new AirPodsStatus(true, true, "AA:BB:CC:DD:EE:FF", "Alex’s AirPods", AirPodsNoiseMode.Anc,
                [AirPodsNoiseMode.Off, AirPodsNoiseMode.Anc, AirPodsNoiseMode.Transparency], "Left 80%, Right 75%");
            panel.GetType().GetField("_defaultAudioName", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, status.Name);
            apply.Invoke(panel, [status]);
            panel.FindControl<DaisyOS.Shell.Controls.QuickSettingTile>("EthernetTile")!.IsEnabled = true;
            panel.FindControl<DaisyOS.Shell.Controls.QuickSettingTile>("EthernetTile")!.IsChecked = true;
            panel.FindControl<DaisyOS.Shell.Controls.QuickSettingTile>("LocalSendTile")!.IsChecked = true;
            window.UpdateLayout();
            await Task.Delay(300);
            Assert.True(panel.FindControl<StackPanel>("AirPodsControls")!.IsVisible);
            Assert.True(panel.FindControl<DaisyOS.Shell.Controls.QuickSettingTile>("BluetoothTile")!.IsVisible);
            Assert.True(panel.FindControl<DaisyOS.Shell.Controls.QuickSettingTile>("DndTile")!.IsVisible);
            Assert.True(panel.FindControl<ToggleButton>("AirPodsAnc")!.IsChecked);
            Assert.False(panel.FindControl<ToggleButton>("AirPodsAdaptive")!.IsEnabled);
            Assert.Equal("AirPods", panel.FindControl<DaisyOS.Shell.Controls.QuickSettingsSlider>("VolumeSlider")!.Icon);
            var buttons = new[] { "Off", "Transparency", "Anc", "Adaptive" }.Select(mode => panel.FindControl<ToggleButton>("AirPods" + mode)!).ToArray();
            Assert.All(buttons, button => Assert.True(button.Bounds.Width >= 60));
            Assert.Single(buttons.Where(button => button.IsChecked == true));
            using var frame = window.CaptureRenderedFrame();
            frame!.Save("/tmp/daisy-airpods-quick-settings.png");
            apply.Invoke(panel, [AirPodsStatus.Unavailable]);
            Assert.False(panel.FindControl<StackPanel>("AirPodsControls")!.IsVisible);
            Assert.True(panel.FindControl<DaisyOS.Shell.Controls.QuickSettingTile>("BluetoothTile")!.IsVisible);
            Assert.Equal("speaker-high", panel.FindControl<DaisyOS.Shell.Controls.QuickSettingsSlider>("VolumeSlider")!.Icon);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WifiTileUsesSettingsWifiIcon()
    {
        var panel = new DaisyOS.Shell.Views.Components.QuickSettings.QuickSettings();

        Assert.Equal("wifi-02", panel.FindControl<DaisyOS.Shell.Controls.QuickSettingTile>("WifiTile")!.Icon);
    }

    [AvaloniaFact]
    public void OutputRowsWrapLongNamesAndMarkTheSelectedDevice()
    {
        var panel = new DaisyOS.Shell.Views.Components.QuickSettings.QuickSettings();
        var main = panel.FindControl<StackPanel>("QuickSettingsMainPage")!;
        main.IsVisible = false;
        panel.FindControl<StackPanel>("OutputDevicesPage")!.IsVisible = true;
        var host = panel.FindControl<StackPanel>("OutputDevicesHost")!;
        host.Children.Clear();
        var create = panel.GetType().GetMethod("CreateOutputDeviceButton", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var device in new[]
        {
            new AudioDeviceInfo("1", "Scarlett Solo (3rd Gen.) Direct Scarlett Solo", "", true),
            new AudioDeviceInfo("2", "Sisäinen äänentoisto Digitaalinen stereo (IEC958)", "", false),
            new AudioDeviceInfo("3", "TU104 HD Audio Controller Digitaalinen stereo (HDMI)", "", false)
        }) host.Children.Add((Button)create.Invoke(panel, new object[] { device })!);
        var window = new Window { Width = 340, SizeToContent = SizeToContent.Height, Content = panel,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        try
        {
            window.UpdateLayout();
            foreach (var row in host.Children.Cast<Button>())
            {
                Assert.True(row.Bounds.Height >= 52);
                Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.MaxLines == 2 && t.TextWrapping == Avalonia.Media.TextWrapping.Wrap && t.Bounds.Width > 150);
            }
            Assert.Contains("SelectedOutputDevice", ((Button)host.Children[0]).Classes);
            using var frame = window.CaptureRenderedFrame();
            frame!.Save("/tmp/daisy-output-devices.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SplitCornersMatchSingleButtonsAndDividersKeepThemeColor()
    {
        foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
        {
            var tile = new DaisyOS.Shell.Controls.QuickSettingTile { HasDetails = true };
            var window = new Window { Content = tile, RequestedThemeVariant = theme };
            window.Show();
            try
            {
                var radius = tile.FindControl<ToggleButton>("SingleToggle")!.CornerRadius.TopLeft;
                Assert.Equal(new Avalonia.CornerRadius(radius, 0, 0, radius), tile.FindControl<ToggleButton>("SplitToggle")!.CornerRadius);
                Assert.Equal(new Avalonia.CornerRadius(0, radius, radius, 0), tile.FindControl<Button>("DetailsButton")!.CornerRadius);
                Assert.Equal(44, tile.FindControl<ToggleButton>("SingleToggle")!.Height);
                Assert.Equal(44, tile.FindControl<ToggleButton>("SplitToggle")!.Bounds.Height);
                Assert.Equal(44, tile.FindControl<Button>("DetailsButton")!.Bounds.Height);
                var divider = tile.FindControl<Border>("SplitDivider")!;
                var expected = Avalonia.Media.Color.Parse(theme == Avalonia.Styling.ThemeVariant.Light ? "#26000000" : "#26ffffff");
                foreach (var active in new[] { false, true })
                {
                    tile.IsChecked = active;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.Equal(expected, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(divider.Background).Color);
                    Assert.Equal(1, divider.Opacity);
                }
            }
            finally { window.Close(); }
        }
    }
    [AvaloniaFact]
    public void DetachedQuickSettingsKeepsControlsAndRoutesConsoleRequest()
    {
        var bar = new SystemBarView();
        var panel = bar.FindControl<DaisyOS.Shell.Views.Components.QuickSettings.QuickSettings>("QuickSettingsPanel")!;
        var overlay = Assert.IsType<DaisyOS.Shell.Services.Shell.BottomChromeOverlayHost>(bar.DetachFlyoutsForNativeOverlay());
        Assert.Contains(panel, overlay.Viewport.Children);
        Assert.Same(panel.MaterialSurface, bar.QuickSettingsMaterialFrame);
        Assert.NotNull(panel.FindControl<Control>("VolumeSlider"));
        Assert.NotNull(panel.FindControl<Control>("WifiNetworksPage"));
        Assert.NotNull(panel.FindControl<Control>("BluetoothDevicesPage"));

        var consoleRequests = 0;
        bar.ConsoleModeRequested += (_, _) => consoleRequests++;
        var tile = panel.FindControl<DaisyOS.Shell.Controls.QuickSettingTile>("GamingTile")!;
        tile.ActivateDetails();
        Assert.Equal(1, consoleRequests);
        bar.HideQuickSettingsPanel();
        Assert.False(bar.IsQuickSettingsVisible);
    }
    [AvaloniaFact]
    public async Task ActiveTileUsesAccentIconsAndKeepsLabelAndTooltipNeutral()
    {
        foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
        {
            var tile = new DaisyOS.Shell.Controls.QuickSettingTile { Label = "Wi-Fi", Icon = "wifi", HasDetails = true, ToolTipText = "Wi-Fi status" };
            var window = new Window { Content = tile, RequestedThemeVariant = theme };
            window.Show();
            try
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var label = tile.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("QuickSettingLabel"));
                var foreground = label.Foreground;
                var weight = label.FontWeight;
                tile.IsChecked = true;
                await Task.Delay(250);
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(foreground, label.Foreground);
                Assert.Equal(weight, label.FontWeight);
                foreach (var icon in tile.GetVisualDescendants().OfType<DaisyOS.Shell.Controls.DaisyIcon>())
                    {
                    Assert.True(tile.TryFindResource("ActionPrimaryBrush", theme, out var accent));
                    Assert.Equal(((Avalonia.Media.ISolidColorBrush)accent!).Color,
                        ((Avalonia.Media.ISolidColorBrush)icon.Foreground!).Color);
                    }
                var tooltip = Assert.IsType<TextBlock>(ToolTip.GetTip(tile.FindControl<ToggleButton>("SplitToggle")!));
                var tooltipColor = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(tooltip.Foreground).Color;
                Assert.Equal(tooltipColor.R, tooltipColor.G);
                Assert.Equal(tooltipColor.G, tooltipColor.B);
            }
            finally { window.Close(); }
        }
    }
}
