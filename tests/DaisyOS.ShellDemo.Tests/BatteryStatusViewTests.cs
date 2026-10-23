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
    [InlineData(75, "#34C759")]
    [InlineData(19, "#FFCC00")]
    public void BatteryFillUsesRequestedNormalAndLowColors(int percentage, string expected)
    {
        var view = new BatteryStatusView { ChargePercent = percentage };
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
