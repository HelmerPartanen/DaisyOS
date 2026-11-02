using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Theming;
using DaisyOS.Shell.Views.Components.SystemBar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class WifiSignalRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SystemBarActuallyRendersEachSignalBandInTheCurrentTheme(bool dark)
    {
        var bar = new SystemBarView();
        var window = new Window { Content = bar, Width = 800, Height = 100,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout();
            DisableTransitions(window);
            var update = typeof(SystemBarView).GetMethod("UpdateNetworkPresentation", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await Task.Delay(300); // Let the shell's initial status and theme transitions settle.
            async Task SetSignal(int signal)
            {
                update.Invoke(bar, [new NetworkStatus(true, "Test", "", NetworkConnectionKind.WiFi, NetworkConnectivity.Full, signal),
                    new WirelessRadioStatus(true, true, ""), new BluetoothStatus(true, true, false, [], "")]);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                await Task.Delay(220);
                Dispatcher.UIThread.RunJobs();
            }
            var icon = bar.FindControl<WifiSignalIcon>("SystemBarWifiIcon")!;
            await SetSignal(20);
            Assert.Equal(0.3, icon.InactiveBandOpacity);
            var weak = CaptureBands(window, icon);
            await SetSignal(80);
            var strong = CaptureBands(window, icon);
            Assert.True(ColorDistance(weak.outer, strong.outer) >= 30, $"outer: {weak.outer} → {strong.outer}");
            Assert.True(ColorDistance(weak.inner, strong.inner) >= 30, $"inner: {weak.inner} → {strong.inner}");
            Assert.True(weak.dot == strong.dot, $"weak={weak}, strong={strong}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task QuickSettingsTileUsesSignalBandsForBothStatesAndThemes(bool dark, bool active)
    {
        var tile = new QuickSettingTile { UseWifiSignalIcon = true, HasDetails = true, IsChecked = active, WifiSignalPercent = 20 };
        var window = new Window { Content = tile, Width = 120, Height = 90,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout();
            DisableTransitions(window);
            await Task.Delay(220);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var icon = tile.GetVisualDescendants().OfType<WifiSignalIcon>().Single(x => x.IsEffectivelyVisible);
            var bands = icon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().ToArray();
            var activeColor = Assert.IsAssignableFrom<ISolidColorBrush>(icon.ActiveBrush).Color;
            var expected = Color.Parse(active ? (dark ? "#FF148BE8" : "#FF1598FF") : (dark ? "#CCF5F5F5" : "#CC000000"));
            Assert.Equal(expected, activeColor);
            Assert.Equal(0.3, bands[0].Opacity);
            Assert.Equal(0.3, bands[1].Opacity);
            Assert.Equal(1, bands[2].Opacity);
            var weak = CaptureBands(window, icon);
            tile.WifiSignalPercent = 80;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(80, icon.SignalPercent);
            Assert.All(bands, band => Assert.Equal(1, band.Opacity));
            await Task.Delay(220);
            Dispatcher.UIThread.RunJobs();
            var strong = CaptureBands(window, icon);
            Assert.NotEqual(weak.outer, strong.outer);
            Assert.NotEqual(weak.inner, strong.inner);
            Assert.Equal(weak.dot, strong.dot);
            tile.IsChecked = !active;
            Dispatcher.UIThread.RunJobs();
            var toggledColor = Assert.IsAssignableFrom<ISolidColorBrush>(icon.ActiveBrush).Color;
            var toggledExpected = Color.Parse(!active ? (dark ? "#FF148BE8" : "#FF1598FF") : (dark ? "#CCF5F5F5" : "#CC000000"));
            Assert.Equal(toggledExpected, toggledColor);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NetworkStatusUpdatesWifiTileStrengthAndRadioState()
    {
        var panel = new DaisyOS.Shell.Views.Components.QuickSettings.QuickSettings();
        var window = new Window { Content = panel, Width = 340, Height = 500 };
        window.Show();
        try
        {
            var update = panel.GetType().GetMethod("UpdateNetworkPresentation", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var tile = panel.FindControl<QuickSettingTile>("WifiTile")!;
            var bluetooth = new BluetoothStatus(true, true, false, [], "");
            update.Invoke(panel, [new NetworkStatus(true, "Test", "", NetworkConnectionKind.WiFi, NetworkConnectivity.Full, 50),
                new WirelessRadioStatus(true, true, ""), bluetooth]);
            Dispatcher.UIThread.RunJobs();
            Assert.True(tile.IsChecked);
            Assert.Equal(50, tile.WifiSignalPercent);
            var icon = tile.GetVisualDescendants().OfType<WifiSignalIcon>().Single(x => x.IsEffectivelyVisible);
            Assert.Equal(50, icon.SignalPercent);
            update.Invoke(panel, [new NetworkStatus(false, "", "", NetworkConnectionKind.Unknown, NetworkConnectivity.None, null),
                new WirelessRadioStatus(true, false, ""), bluetooth]);
            Dispatcher.UIThread.RunJobs();
            Assert.False(tile.IsChecked);
            Assert.Equal(0, icon.SignalPercent);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task WifiNetworkMenuUpdatesBandBrushesWhenTheThemeChanges()
    {
        var panel = new DaisyOS.Shell.Views.Components.QuickSettings.QuickSettings();
        panel.FindControl<StackPanel>("QuickSettingsMainPage")!.IsVisible = false;
        panel.FindControl<Grid>("WifiNetworksPage")!.IsVisible = true;
        var create = panel.GetType().GetMethod("CreateWifiNetworkButton", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var button = (Button)create.Invoke(panel, [new WirelessNetworkInfo("Test", 20, "WPA2", true)])!;
        panel.FindControl<StackPanel>("WifiNetworksHost")!.Children.Add(button);
        var window = new Window { Content = panel, Width = 340, Height = 500, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout();
            DisableTransitions(window);
            Dispatcher.UIThread.RunJobs();
            var icon = button.GetVisualDescendants().OfType<WifiSignalIcon>().Single();
            var bands = icon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().ToArray();
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark, ThemeVariant.Light })
            {
                window.RequestedThemeVariant = theme;
                await Task.Delay(220);
                Dispatcher.UIThread.RunJobs();
                var rgb = theme == ThemeVariant.Light ? "000000" : "F5F5F5";
                icon.SignalPercent = 20;
                Assert.Equal(0.3, icon.InactiveBandOpacity);
                Assert.Equal(0.3, bands[0].Opacity);
                Assert.Equal(0.3, bands[1].Opacity);
                Assert.Equal(Color.Parse("#99" + rgb), Assert.IsAssignableFrom<ISolidColorBrush>(bands[0].Stroke).Color);
                Assert.Equal(Color.Parse("#99" + rgb), Assert.IsAssignableFrom<ISolidColorBrush>(bands[1].Stroke).Color);
                Assert.Equal(Color.Parse("#CC" + rgb), Assert.IsAssignableFrom<ISolidColorBrush>(bands[2].Fill).Color);
                var weak = CaptureBands(window, icon);
                icon.SignalPercent = 80;
                var strong = CaptureBands(window, icon);
                Assert.True(ColorDistance(weak.outer, strong.outer) >= 30, $"outer: {weak.outer} → {strong.outer}");
                Assert.True(ColorDistance(weak.inner, strong.inner) >= 30, $"inner: {weak.inner} → {strong.inner}");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingNetworkRowsUpdateSignalWithoutLosingFocus(bool connected)
    {
        var panel = new DaisyOS.Shell.Views.Components.QuickSettings.QuickSettings();
        panel.FindControl<StackPanel>("QuickSettingsMainPage")!.IsVisible = false;
        panel.FindControl<Grid>("WifiNetworksPage")!.IsVisible = true;
        var host = panel.FindControl<StackPanel>("WifiNetworksHost")!;
        var window = new Window { Content = panel, Width = 340, Height = 500 };
        window.Show();
        try
        {
            var initial = new WirelessNetworkStatus(true, [new WirelessNetworkInfo("Test", 90, "WPA2", connected, IsKnown: true)], "");
            var updated = new WirelessNetworkStatus(true, [new WirelessNetworkInfo("Test", 20, "WPA2", connected, IsKnown: true)], "");
            var type = panel.GetType();
            type.GetMethod("RenderWifiNetworks", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, [host, initial]);
            window.UpdateLayout();
            var button = Assert.Single(host.Children.OfType<Button>());
            var icon = Assert.Single(button.GetVisualDescendants().OfType<WifiSignalIcon>());
            Assert.Equal(90, icon.SignalPercent);
            Assert.True(button.Focus());
            Assert.True((bool)type.GetMethod("HasSameWifiRows", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [initial, updated])!);
            type.GetMethod("UpdateWifiSignalStrengths", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [host, updated]);
            Assert.Same(button, Assert.Single(host.Children.OfType<Button>()));
            Assert.Same(icon, Assert.Single(button.GetVisualDescendants().OfType<WifiSignalIcon>()));
            Assert.True(button.IsFocused);
            Assert.Equal(20, icon.SignalPercent);
            var bands = icon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().ToArray();
            Assert.Equal(0.3, bands[0].Opacity);
            Assert.Equal(0.3, bands[1].Opacity);
            Assert.Equal(1, bands[2].Opacity);
            Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == (connected ? "Connected" : "Known network · 20%"));
            button.Classes.Add("Loading");
            var detail = button.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == (connected ? "Connected" : "Known network · 20%"));
            if (!connected) detail.Text = "Connecting…";
            type.GetMethod("UpdateWifiSignalStrengths", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [host, initial]);
            Assert.Equal(90, icon.SignalPercent);
            if (!connected) Assert.Equal("Connecting…", detail.Text);
        }
        finally { window.Close(); }
    }

    private static int ColorDistance(SkiaSharp.SKColor left, SkiaSharp.SKColor right) =>
        Math.Max(Math.Abs(left.Red - right.Red), Math.Max(Math.Abs(left.Green - right.Green), Math.Abs(left.Blue - right.Blue)));

    private static void DisableTransitions(Window window)
    {
        foreach (var control in window.GetVisualDescendants().OfType<Control>().Prepend(window))
        {
            ThemeTransitionPolicy.SetEnabled(control, false);
            control.Transitions = null;
        }
    }

    private static (SkiaSharp.SKColor outer, SkiaSharp.SKColor inner, SkiaSharp.SKColor dot) CaptureBands(Window window, WifiSignalIcon icon)
    {
        var bands = icon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().ToArray();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var encoded = new MemoryStream();
        frame.Save(encoded, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        using var pixels = SkiaSharp.SKBitmap.Decode(encoded.ToArray());
        SkiaSharp.SKColor Sample(int index, double y)
        {
            var p = bands[index].TranslatePoint(new Point(11, y), window)!.Value;
            return pixels.GetPixel((int)Math.Floor(p.X), (int)Math.Floor(p.Y));
        }
        return (Sample(0, 1), Sample(1, 7), Sample(2, 14));
    }
}
