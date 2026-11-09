using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using DaisyOS.Core.Desktop;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Desktop;
using DaisyOS.Shell.ViewModels;
using Xunit;
using Point = Avalonia.Point;
using Rect = Avalonia.Rect;

namespace DaisyOS.ShellDemo.Tests;

public class WidgetSnapPreviewTests
{
    [AvaloniaFact]
    public async Task OutlineMovesThroughIntermediatePositionsAndRetargetsSmoothly()
    {
        var outline = new WidgetSnapOutline();
        var window = new Window { Width = 800, Height = 600, Content = outline };
        window.Show();
        try
        {
            var first = new Rect(20, 20, 200, 160);
            outline.TargetBounds = first;
            using (window.CaptureRenderedFrame()) { }
            Assert.Equal(first, outline.AnimatedBounds);
            Assert.NotNull(outline.Transitions);
            outline.TargetBounds = first.WithX(400);
            Assert.Equal(TimeSpan.FromMilliseconds(160), ((Avalonia.Animation.TransitionBase)outline.Transitions![0]).Duration);
            Assert.InRange(outline.AnimatedBounds.X, 20, 399.9);
            // CaptureRenderedFrame drains render jobs and can finish short animations.
            // Tick once to sample the live transition instead.
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(50);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.InRange(outline.AnimatedBounds.X, 20.1, 399.9);
            var intermediate = outline.AnimatedBounds;
            outline.TargetBounds = first.WithX(200);
            Assert.InRange(outline.AnimatedBounds.X, intermediate.X - 1, intermediate.X + 1);
            await Task.Delay(250);
            using (window.CaptureRenderedFrame()) { }
            Assert.Equal(first.WithX(200), outline.AnimatedBounds);
            outline.TargetBounds = null;
            outline.TargetBounds = first;
            Assert.Equal(first, outline.AnimatedBounds);
            outline.Resources["MotionDurationBase"] = TimeSpan.Zero;
            outline.TargetBounds = first.WithX(500);
            using (window.CaptureRenderedFrame()) { }
            Assert.Equal(first.WithX(500), outline.AnimatedBounds);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(3, 2)]
    [InlineData(5, 4)]
    public void OutlineTracksTheActualReleasePositionAndClampsAtEdges(int columns, int rows)
    {
        var path = Path.Combine(Path.GetTempPath(), $"widget-snap-{Guid.NewGuid():N}.json");
        var vm = new DesktopViewModel(new DesktopLayoutStore(path));
        vm.ClearItems();
        var item = vm.AddWidget("widget-test", columns, rows, initialCol: 0, initialRow: 0);
        var metrics = new DesktopGridMetrics("primary", new DaisyOS.Core.Desktop.Rect(0, 0, 800, 600), 96, 100, 100);
        var panel = new DesktopReorderPanel { ViewModel = vm, Metrics = metrics };
        panel.Children.Add(new Border { DataContext = item, Background = Brushes.Gray, Margin = new Thickness(4), CornerRadius = new CornerRadius(32) });
        var outline = new WidgetSnapOutline { IsHitTestVisible = false };
        var window = new Window { Width = 800, Height = 600, Content = new Grid { Children = { outline, panel } } };
        void Refresh() { panel.InvalidateArrange(); outline.TargetBounds = panel.WidgetSnapBounds; }
        var drag = new DesktopDragController();
        window.Show();
        try
        {
            Assert.Null(panel.WidgetSnapBounds);
            drag.StartDirectDrag(item, new Point(40, 40), new Point(40, 40), new Size(columns * 100, rows * 100), vm, metrics, Refresh);
            Assert.NotNull(panel.WidgetSnapBounds);
            drag.OnPointerMoved(new Point(10000, 10000), vm, metrics, Refresh);
            var destination = panel.WidgetSnapBounds!.Value;
            Assert.Equal(new Rect(804 - columns * 100, 604 - rows * 100, columns * 100 - 8, rows * 100 - 8), destination);
            // Move the floating card partly away from the target so the outline can be inspected.
            drag.OnPointerMoved(new Point(600, 350), vm, metrics, Refresh);
            window.UpdateLayout();
            destination = panel.WidgetSnapBounds!.Value;
            using var frame = window.CaptureRenderedFrame();
            frame!.Save($"/tmp/daisy-widget-snap-{columns}-{rows}.png");
            drag.OnPointerReleased(vm, metrics, Refresh);
            Assert.Null(panel.WidgetSnapBounds);
            Assert.Equal(destination.X - 4, item.DragX);
            Assert.Equal(destination.Y - 4, item.DragY);
        }
        finally { window.Close(); File.Delete(path); }
    }

    [AvaloniaFact]
    public void BlockedTargetKeepsTheReleaseOutlineAndCancellationHidesIt()
    {
        var vm = new DesktopViewModel(new DesktopLayoutStore(Path.Combine(Path.GetTempPath(), $"widget-snap-{Guid.NewGuid():N}.json")));
        vm.ClearItems();
        var item = vm.AddWidget("source", 3, 2, initialCol: 0, initialRow: 0);
        vm.AddWidget("other", 3, 2, initialCol: 4, initialRow: 0);
        var metrics = new DesktopGridMetrics("primary", new DaisyOS.Core.Desktop.Rect(0, 0, 800, 600), 96, 100, 100);
        var panel = new DesktopReorderPanel { ViewModel = vm, Metrics = metrics };
        var drag = new DesktopDragController();
        drag.StartDirectDrag(item, default, default, new Size(300, 200), vm, metrics, null);
        var original = panel.WidgetSnapBounds;
        drag.OnPointerMoved(new Point(200, 0), vm, metrics);
        Assert.Equal(original, panel.WidgetSnapBounds);
        Assert.Equal(200, item.DragX);
        drag.OnPointerCaptureLost(vm, null);
        Assert.Null(panel.WidgetSnapBounds);
    }
}
