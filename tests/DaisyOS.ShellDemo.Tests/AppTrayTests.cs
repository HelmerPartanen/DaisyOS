using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Threading;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Views.Components.SystemBar;
using Tmds.DBus.Protocol;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class AppTrayTests
{
    [AvaloniaFact]
    public void SystemBarHugeIconsUseFilledAssets()
    {
        var bar = new SystemBarView();

        foreach (var name in new[]
                 {
                     "AppTrayChevron", "SystemBarEthernetIcon", "SystemBarOfflineIcon",
                     "SystemBarBluetoothIcon", "SystemBarVolumeIcon", "SystemBarNotificationIcon"
                 })
        {
            var icon = bar.FindControl<DaisyIcon>(name)!;
            Assert.Equal(DaisyIconWeight.Fill, icon.Weight);
            Assert.EndsWith("-fill.svg", icon.Path, StringComparison.Ordinal);
        }

        var bluetooth = bar.FindControl<DaisyIcon>("SystemBarBluetoothIcon")!;
        bluetooth.Icon = "bluetooth-slash";
        Assert.EndsWith("bluetooth-slash-fill.svg", bluetooth.Path, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TraySurvivesNativeReparentingAndClosesWhenAnotherPanelOpens()
    {
        var bar = new SystemBarView();
        var overlays = bar.DetachFlyoutsForNativeOverlay();
        var root = new Grid(); root.Children.Add(bar); root.Children.Add(overlays);
        var window = new Window { Content = root, Width = 600, Height = 400 };
        window.Show();
        try
        {
            bar.ShowAppTrayPanel(); Dispatcher.UIThread.RunJobs();
            Assert.True(bar.IsAppTrayVisible);
            var icon = bar.FindControl<DaisyIcon>("AppTrayChevron")!;
            Assert.Equal(RelativePoint.Center, icon.RenderTransformOrigin);
            // Check the destination after the smooth transition has settled separately;
            // opening must request the 180 degree rotation without moving the origin.
            Assert.Single(icon.Transitions!.OfType<Avalonia.Animation.TransformOperationsTransition>());
            bar.ShowCalendarPanel(); Dispatcher.UIThread.RunJobs();
            Assert.False(bar.IsAppTrayVisible);
            bar.ShowAppTrayPanel();
            bar.HideAppTrayPanel(immediate: true);
            Assert.False(bar.IsAppTrayVisible);
            Assert.False(bar.AppTrayMaterialFrame.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ChevronRotatesAroundItsCenterAndReducedMotionSettlesImmediately()
    {
        var bar = new SystemBarView();
        var window = new Window { Content = bar, Width = 600, Height = 400 };
        window.Show();
        try
        {
            var icon = bar.FindControl<DaisyIcon>("AppTrayChevron")!;
            using (var initial = window.CaptureRenderedFrame()) { }
            var center = icon.Bounds.Center;
            bar.ShowAppTrayPanel();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(30);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var matrix = icon.RenderTransform!.Value;
            Assert.InRange(matrix.M11, -0.9999, 0.9999);
            Assert.True(Math.Abs(matrix.M12) > 0.01);
            await Task.Delay(250);
            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.InRange(icon.RenderTransform!.Value.M11, -1.001, -0.999);
                Assert.Equal(center, icon.Bounds.Center);
                frame!.Save("/tmp/daisy-app-tray-open.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            bar.HideAppTrayPanel();
            await Task.Delay(250);
            Assert.InRange(icon.RenderTransform!.Value.M11, 0.999, 1.001);
            bar.Resources["MotionDurationBase"] = TimeSpan.Zero;
            bar.ShowAppTrayPanel();
            Assert.InRange(icon.RenderTransform!.Value.M11, -1.001, -0.999);
            bar.HideAppTrayPanel(immediate: true);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void NormalTrayEntriesPreferApplicationIdentityWhileAttentionRetainsStatusIcon()
    {
        var normal = new DaisyOS.Shell.Services.Tray.TrayItem("service", "/item", "Steam", "Active", "steam_tray_mono", "", default, null, false, "steam");
        var calls = 0;
        string ApplicationIcon(IEnumerable<string?> identities)
        {
            calls++;
            Assert.Contains("steam", identities);
            return "/application/steam.png";
        }
        var paths = AppTrayView.IconCandidates(normal, ApplicationIcon, _ => "/theme/steam_tray_mono.svg").ToArray();
        Assert.Equal(new[] { "/application/steam.png", "/theme/steam_tray_mono.svg" }, paths);
        Assert.Equal(1, calls);
        paths = AppTrayView.IconCandidates(normal with { Status = "NeedsAttention" }, ApplicationIcon, _ => "/theme/attention.svg").ToArray();
        Assert.Equal(new[] { "/theme/attention.svg" }, paths);
        Assert.Equal(1, calls);
    }

    [AvaloniaFact]
    public void TrayPixmapUsesNetworkArgbOrderAndRejectsInvalidDimensions()
    {
        var pixel = VariantValue.Struct(1, 1, VariantValue.Array(new byte[] { 255, 220, 10, 30 }));
        using var bitmap = AppTrayView.DecodePixmap(VariantValue.ArrayOfVariant(new[] { pixel }));
        Assert.NotNull(bitmap);
        using var locked = bitmap.Lock();
        var bytes = new byte[4]; global::System.Runtime.InteropServices.Marshal.Copy(locked.Address, bytes, 0, 4);
        Assert.Equal(new byte[] { 30, 10, 220, 255 }, bytes);
        Assert.Null(AppTrayView.DecodePixmap(VariantValue.ArrayOfVariant(new[] { VariantValue.Struct(0, 1, VariantValue.Array(new byte[4])) })));
    }
}
