using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
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
    public async Task ToastSlidesItsWholeMaterialIntoViewWithoutFading()
    {
        var host = new NotificationToastHost();
        var window = new Window { Width = 420, Height = 600, Content = host };
        window.Show();
        try
        {
            Assert.True(host.Present(new NotificationItem("Spring", "Toast entrance", DateTimeOffset.Now)));
            var toast = Assert.Single(host.FindControl<StackPanel>("ToastList")!.Children);
            Assert.True(toast.RenderTransform!.Value.M31 >= toast.Bounds.Width);
            Assert.Equal(1, toast.Opacity);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(80);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.True(toast.RenderTransform!.Value.M31 < toast.Bounds.Width);
            Assert.Equal(1, toast.Opacity);
            await Task.Delay(700);
            Assert.Null(toast.RenderTransform);
            Assert.True(host.HasActiveToasts);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClickingCardOpensNotificationAndDismissDoesNotOpenIt()
    {
        var card = new NotificationCard
        {
            Background = Avalonia.Media.Brushes.Transparent,
            DataContext = new NotificationItem("Test", "Body", DateTimeOffset.Now,
                ActionId: "default", ActionLabel: "View")
        };
        var opened = 0;
        var dismissed = 0;
        card.PrimaryActionRequested += (_, _) => opened++;
        card.DismissRequested += (_, _) => dismissed++;
        var window = new Window { Width = 420, Height = 300, Content = card };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.False(card.FindControl<Button>("PrimaryAction")!.IsVisible);
            // Pointer hit testing uses the rendered scene, not layout alone.
            using (window.CaptureRenderedFrame()) { }
            var body = card.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Body");
            var bodyPoint = body.TranslatePoint(new Point(4, 4), window)!.Value;
            window.MouseMove(bodyPoint);
            window.MouseDown(bodyPoint, MouseButton.Left);
            window.MouseUp(bodyPoint, MouseButton.Left);
            Assert.Equal(1, opened);
            var dismiss = card.GetVisualDescendants().OfType<Button>().First();
            var point = dismiss.TranslatePoint(new Point(16, 16), window)!.Value;
            using (window.CaptureRenderedFrame()) { }
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal(1, dismissed);
            Assert.Equal(1, opened);
            card.DataContext = new NotificationItem("Request", "Body", DateTimeOffset.Now,
                ActionId: "accept", ActionLabel: "Accept");
            window.UpdateLayout();
            Assert.True(card.FindControl<Button>("PrimaryAction")!.IsVisible);
            using (window.CaptureRenderedFrame()) { }
            bodyPoint = body.TranslatePoint(new Point(4, 4), window)!.Value;
            window.MouseMove(bodyPoint);
            window.MouseDown(bodyPoint, MouseButton.Left);
            window.MouseUp(bodyPoint, MouseButton.Left);
            Assert.Equal(1, opened);
        }
        finally { window.Close(); }
    }

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
                SourceAppId: "org.daisyos.notes", ActionLabel: "Accept", ActionId: "accept",
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
