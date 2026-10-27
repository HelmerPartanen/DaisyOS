using System.Reflection;
using Avalonia.Controls;
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
    [InlineData(75, "#8899AA")]
    [InlineData(19, "#FFCC00")]
    public void DischargingBatteryUsesNeutralOrWarningFill(int percentage, string expected)
    {
        var view = new BatteryStatusView
        {
            ChargePercent = percentage,
            Foreground = new SolidColorBrush(Color.Parse("#8899AA"))
        };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var icon = view.FindControl<BatteryLevelIcon>("LevelIcon")!;
            Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(icon.FillBrush).Color);
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
            var body = icon.FindControl<DaisyIcon>("BatteryBody")!;
            Assert.Equal("battery", body.Icon);
            Assert.Equal(icon.BaseBrush, body.Foreground);
        }
        finally { window.Close(); }
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
