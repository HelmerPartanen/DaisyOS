using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Converters;
using DaisyOS.Shell.Views.Components.Notifications;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class NotificationPresentationTests
{
    [AvaloniaFact]
    public async Task AllUrgenciesUseTheSameLifetimeAndOverflowWaitsForAFreeSlot()
    {
        var host = new NotificationToastHost();
        host.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        host.Resources["MotionDurationShellExit"] = TimeSpan.Zero;
        var window = new Window { Width = 500, Height = 900, Content = host };
        window.Show();
        try
        {
            foreach (var (urgency, timeout) in new[] { (NotificationUrgency.Low, 1), (NotificationUrgency.Normal, 60), (NotificationUrgency.Critical, 0) })
                host.Present(new NotificationItem("Test", "Body", DateTimeOffset.Now, Urgency: urgency, TimeoutSeconds: timeout));
            host.Present(new NotificationItem("Queued", "Body", DateTimeOffset.Now));
            Assert.Equal(3, host.FindControl<StackPanel>("ToastList")!.Children.Count);
            Assert.True(host.HasActiveToasts);
            await Task.Delay(1300);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, host.FindControl<StackPanel>("ToastList")!.Children.Count);
            await Task.Delay(4100);
            Dispatcher.UIThread.RunJobs();
            var remaining = Assert.Single(host.FindControl<StackPanel>("ToastList")!.Children);
            Assert.Equal("Queued", Assert.IsType<NotificationItem>(remaining.DataContext).Title);
            Assert.Empty(host.GetVisualDescendants().OfType<ProgressBar>());
        }
        finally { window.Close(); }
        Assert.False(host.HasActiveToasts);
    }

    [AvaloniaFact]
    public void ReplacementsReuseTheirSlotAndApplicationIconsWinOverGenericGlyphs()
    {
        var host = new NotificationToastHost();
        host.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        var window = new Window { Width = 500, Height = 600, Content = host };
        window.Show();
        try
        {
            var item = new NotificationItem("Before", "Body", DateTimeOffset.Now, Id: "replace", SourceAppId: "org.daisyos.notes", SystemIconGlyph: "bell");
            host.Present(item);
            var first = Assert.Single(host.FindControl<StackPanel>("ToastList")!.Children);
            host.Present(item with { Title = "After" });
            Assert.Same(first, Assert.Single(host.FindControl<StackPanel>("ToastList")!.Children));
            Assert.Equal("After", Assert.IsType<NotificationItem>(first.DataContext).Title);
            Assert.NotNull(new NotificationIconValueConverter().Convert(item, typeof(object), null, global::System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void NativeSizedSurfaceQueuesTallCardsAndKeepsTheirMaterialInsideItsBounds()
    {
        var host = new NotificationToastHost();
        host.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        var window = new Window { Width = 420, Height = 460, Content = host };
        window.Show();
        try
        {
            var item = new NotificationItem(new string('W', 120), new string('W', 800), DateTimeOffset.Now,
                SourceAppId: "org.daisyos.notes", ActionLabel: "Open", ActionId: "open",
                PreviewImage: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
            host.Present(item);
            host.Present(item with { Id = "second" });
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var view = Assert.Single(host.FindControl<StackPanel>("ToastList")!.Children);
            var bottom = view.TranslatePoint(new Avalonia.Point(0, view.Bounds.Height), window);
            Assert.NotNull(bottom);
            Assert.True(bottom.Value.Y <= window.ClientSize.Height, $"Card bottom {bottom.Value.Y} exceeds native height {window.ClientSize.Height}");
        }
        finally { window.Close(); }
    }
}
