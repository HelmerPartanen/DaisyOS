using System.Reflection;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Views.Components.SystemBar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class BatteryStatusViewTests
{
    [AvaloniaTheory]
    [InlineData(75, "#CCF5F5F5", false)]
    [InlineData(75, "#CC000000", true)]
    [InlineData(19, "#FFCC00", false)]
    [InlineData(19, "#FFCC00", true)]
    public void DischargingBatteryUsesIconColorOrWarningFill(int percentage, string expected, bool light)
    {
        var view = new BatteryStatusView
        {
            ChargePercent = percentage,
            IconWidth = 28,
            IconHeight = 22,
            Foreground = new SolidColorBrush(Color.Parse("#8899AA"))
        };
        var window = new Window
        {
            Content = view, Width = 160, Height = 60,
            RequestedThemeVariant = light ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark,
            Background = new SolidColorBrush(light ? Color.Parse("#F0F0F0") : Color.Parse("#202020"))
        };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var icon = view.FindControl<BatteryLevelIcon>("LevelIcon")!;
            Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(icon.FillBrush).Color);
            var screenshot = $"/tmp/daisy-battery-fill-{percentage}-{(light ? "light" : "dark")}.png";
            window.CaptureRenderedFrame()!.Save(screenshot);
            using var pixels = SkiaSharp.SKBitmap.Decode(screenshot);
            var baseBrush = Assert.IsAssignableFrom<ISolidColorBrush>(icon.BaseBrush);
            var baseColor = baseBrush.Color;
            var background = light ? 240 : 32;
            var alpha = baseColor.A / 255d * baseBrush.Opacity * 0.6;
            var expectedEmpty = (int)Math.Round(background * (1 - alpha) + baseColor.R * alpha);
            Assert.InRange((int)pixels.GetPixel(19, 30).Red, expectedEmpty - 2, expectedEmpty + 2);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneIconMatchesStatusIconAndUpdatesCharge(bool light)
    {
        var standalone = new BatteryLevelIcon { Width = 24, Height = 18, Percentage = 75 };
        var status = new BatteryStatusView { ChargePercent = 75 };
        var panel = new StackPanel();
        panel.Children.Add(standalone);
        panel.Children.Add(status);
        var window = new Window
        {
            Content = panel, Width = 160, Height = 60,
            Background = new SolidColorBrush(light ? Color.Parse("#F0F0F0") : Color.Parse("#202020")),
            RequestedThemeVariant = light ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark
        };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var wrapped = status.FindControl<BatteryLevelIcon>("LevelIcon")!;
            Assert.Equal(24, status.IconWidth);
            Assert.Equal(18, status.IconHeight);
            Assert.Equal(Color.Parse(light ? "#CC000000" : "#CCF5F5F5"), Assert.IsAssignableFrom<ISolidColorBrush>(standalone.FillBrush).Color);
            Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(wrapped.FillBrush).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(standalone.FillBrush).Color);
            Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(wrapped.BaseBrush).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(standalone.BaseBrush).Color);
            standalone.Percentage = 9;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Critical", standalone.Classes);
            Assert.Equal(BatteryLevelIcon.GetFillWidth(9), standalone.FindControl<Border>("FillBar")!.Width);
            standalone.IsCharging = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(standalone.FindControl<DaisyIcon>("ChargingGlyph")!.IsVisible);
            Assert.Contains("Charging", standalone.Classes);
            window.CaptureRenderedFrame()!.Save($"/tmp/daisy-shared-battery-{(light ? "light" : "dark")}.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ChargingBatteryUsesPercentageFillWithNormalColoredLightning()
    {
        var view = new BatteryStatusView
        {
            ChargePercent = 75,
            IsCharging = true,
            Foreground = new SolidColorBrush(Color.Parse("#8899AA"))
        };
        var window = new Window { Content = view };
        window.Resources["BatteryChargingBrush"] = new SolidColorBrush(Color.Parse("#34C759"));
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var levelIcon = view.FindControl<BatteryLevelIcon>("LevelIcon")!;
            Assert.True(levelIcon.IsVisible);
            var chargingIcon = levelIcon.FindControl<DaisyIcon>("ChargingGlyph")!;
            Assert.True(chargingIcon.IsVisible);
            Assert.Equal("lightning", chargingIcon.Icon);
            Assert.Equal(Color.Parse("#34C759"),
                Assert.IsAssignableFrom<ISolidColorBrush>(levelIcon.FillBrush).Color);
            Assert.Equal(BatteryLevelIcon.GetFillWidth(75),
                levelIcon.FindControl<Border>("FillBar")!.Width,
                precision: 3);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DarkThemeChargingBatteryUsesConfiguredBrightGreenFill()
    {
        var view = new BatteryStatusView { ChargePercent = 60, IsCharging = true };
        var window = new Window
        {
            Content = view,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark
        };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var icon = view.FindControl<BatteryLevelIcon>("LevelIcon")!;
            Assert.Equal(Color.Parse("#00CA48"),
                Assert.IsAssignableFrom<ISolidColorBrush>(icon.FillBrush).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ChargeOverlaysSolidBodyWithoutAStroke()
    {
        var icon = new BatteryLevelIcon { Percentage = 85 };
        var window = new Window { Content = icon };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var canvas = Assert.IsType<Canvas>(Assert.IsType<Viewbox>(icon.Content).Child);
            var fill = icon.FindControl<Border>("FillBar")!;
            Assert.Equal(0, Canvas.GetLeft(fill));
            Assert.Equal(1, Canvas.GetTop(fill));
            Assert.Equal(12, fill.Height);
            Assert.Equal(21 * 0.85, fill.Width, 5);
            var body = icon.FindControl<Avalonia.Controls.Shapes.Path>("BatteryBody")!;
            Assert.NotNull(body.Data);
            Assert.Equal(icon.BaseBrush, body.Fill);
            Assert.Equal(0.6, body.Opacity, 2);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void BatteryFillClipMatchesTheUpdatedSvgBodyExactly()
    {
        var root = FindRepoRoot();
        var svg = XDocument.Load(Path.Combine(root, "src", "DaisyOS.Shell", "Assets", "Icons", "HugeIcons", "battery.svg"));
        var body = svg.Root!.Elements().First().Attribute("d")!.Value;
        var view = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Controls", "BatteryLevelIcon.axaml"));
        Assert.Contains($"Clip=\"{body}\"", view);
        var silhouette = string.Join(" ", svg.Root.Elements().Select(element => element.Attribute("d")!.Value));
        Assert.Contains($"Data=\"{silhouette}\"", view);
        var settings = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Apps", "System", "Settings", "SettingsView.axaml"));
        Assert.DoesNotContain("Icon=\"battery-charging\"", settings);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Theory]
    [InlineData("Charging", true)]
    [InlineData("charging", true)]
    [InlineData("Discharging", false)]
    [InlineData("Not charging", false)]
    [InlineData("Full", false)]
    public void OnlyChargingStateUsesChargingPresentation(string state, bool expected) =>
        Assert.Equal(expected, BatteryStatusView.IsChargingState(state));

    [AvaloniaFact]
    public void CriticalFillUsesTheCurrentDaisyDangerColor()
    {
        var view = new BatteryStatusView { ChargePercent = 9 };
        var window = new Window { Content = view };
        var danger = new SolidColorBrush(Color.Parse("#B4232A"));
        window.Resources["StatusDangerBrush"] = danger;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var icon = view.FindControl<BatteryLevelIcon>("LevelIcon")!;
            Assert.Equal(danger.Color, Assert.IsAssignableFrom<ISolidColorBrush>(icon.FillBrush).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SystemBarShowsBatteryOnlyWhenAPercentageIsAvailable()
    {
        var bar = new SystemBarView();
        var update = typeof(SystemBarView).GetMethod("UpdateBatteryPresentation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var battery = new BatteryStatus(true, 28, "Discharging", "BAT0: 28% Discharging");

        update.Invoke(bar, [battery]);

        var status = bar.FindControl<BatteryStatusView>("SystemBarBattery")!;
        Assert.True(status.IsVisible);
        Assert.Equal(28, status.ChargePercent);
    }
}
