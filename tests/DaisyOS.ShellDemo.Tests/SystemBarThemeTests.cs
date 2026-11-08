using System.Reflection;
using Avalonia.Controls;
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

public sealed class SystemBarThemeTests
{
    [AvaloniaFact]
    public void WifiBandsAndNormalBatteryFollowThemeAfterNetworkRefresh()
    {
        var bar = new SystemBarView();
        var window = new Window { Content = bar, Width = 800, Height = 100,
            RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            typeof(SystemBarView).GetMethod("UpdateNetworkPresentation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(bar, [new NetworkStatus(true, "Test", "", NetworkConnectionKind.WiFi, NetworkConnectivity.Full, 45),
                    new WirelessRadioStatus(true, true, ""), new BluetoothStatus(true, true, false, [], "")]);
            bar.FindControl<BatteryStatusView>("SystemBarBattery")!.ChargePercent = 75;
            var wifi = bar.FindControl<WifiSignalIcon>("SystemBarWifiIcon")!;
            var bands = wifi.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().ToArray();
            var battery = bar.FindControl<BatteryStatusView>("SystemBarBattery")!.FindControl<BatteryLevelIcon>("LevelIcon")!;
            // Check target resource values independently of theme crossfades.
            foreach (var control in new Control[] { wifi, battery }.Concat(bands))
            {
                ThemeTransitionPolicy.SetEnabled(control, false);
                control.Transitions = null;
            }
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                Dispatcher.UIThread.RunJobs();
                var rgb = theme == ThemeVariant.Light ? "000000" : "F5F5F5";
                Assert.Equal(Color.Parse("#99" + rgb), Assert.IsAssignableFrom<ISolidColorBrush>(wifi.ActiveBrush).Color);
                Assert.Equal(Color.Parse("#99" + rgb), Assert.IsAssignableFrom<ISolidColorBrush>(wifi.InactiveBrush).Color);
                Assert.Equal(Color.Parse("#99" + rgb), Assert.IsAssignableFrom<ISolidColorBrush>(bands[0].Stroke).Color);
                Assert.Equal(Color.Parse("#99" + rgb), Assert.IsAssignableFrom<ISolidColorBrush>(bands[1].Stroke).Color);
                Assert.Equal(Color.Parse("#99" + rgb), Assert.IsAssignableFrom<ISolidColorBrush>(battery.FillBrush).Color);
                Assert.Equal(Color.Parse("#99" + rgb), Assert.IsAssignableFrom<ISolidColorBrush>(battery.BaseBrush).Color);
            }
        }
        finally { window.Close(); }
    }
}
