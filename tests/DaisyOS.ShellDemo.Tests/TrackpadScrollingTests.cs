using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using System.Reflection;
using Pointer = Avalonia.Input.Pointer;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class TrackpadScrollingTests
{
    [AvaloniaTheory]
    [InlineData(240)]
    [InlineData(480)]
    public void PreciseScrollUsesSameDistanceAtDifferentViewportSizes(int height)
    {
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Width = 2400, Height = 2400 }
        };
        var window = new Window { Width = 500, Height = height, Content = scroll };
        window.Show();
        try
        {
            window.UpdateLayout();
            scroll.Offset = new Vector(100, 100);
            Send(scroll, window, new Vector(-2.5, -3.25));
            Assert.Equal(new Vector(110, 113), scroll.Offset);
            for (var i = 0; i < 8; i++) Send(scroll, window, new Vector(0, -0.125));
            Assert.Equal(117, scroll.Offset.Y);
            Send(scroll, window, new Vector(0, 1)); // Reversal applies immediately.
            Assert.Equal(113, scroll.Offset.Y);
            Send(scroll, window, default, end: true);
            Assert.Equal(113, scroll.Offset.Y); // Momentum begins on the next frame.
            Send(scroll, window, new Vector(0, -1), precise: false);
            Assert.Equal(163, scroll.Offset.Y); // Mouse wheels retain their 50px step.
            Send(scroll, window, new Vector(0, -2), modifiers: KeyModifiers.Shift);
            Assert.Equal(118, scroll.Offset.X);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MandatorySnapPointsDoNotFightTheFingers()
    {
        var content = new StackPanel();
        for (var i = 0; i < 20; i++) content.Children.Add(new Border { Height = 100 });
        var scroll = new ScrollViewer { Content = content, VerticalSnapPointsType = SnapPointsType.Mandatory, IsScrollInertiaEnabled = false };
        var window = new Window { Width = 500, Height = 300, Content = scroll };
        window.Show();
        try
        {
            window.UpdateLayout();
            Send(scroll, window, new Vector(0, -0.5));
            Assert.Equal(2, scroll.Offset.Y);
            Send(scroll, window, default, end: true);
            Assert.Equal(0, scroll.Offset.Y);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LogicalContentConvertsPixelsToItemUnitsWithoutUsingWheelStepSize()
    {
        var content = new LogicalContent();
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Width = 500, Height = 300, Content = scroll };
        window.Show();
        try
        {
            window.UpdateLayout();
            var itemWidth = content.Bounds.Width / content.Viewport.Width;
            var itemHeight = content.Bounds.Height / content.Viewport.Height;
            Send(scroll, window, new Vector(-itemWidth / 4, -itemHeight / 4));
            Assert.Equal(1, scroll.Offset.X, 5);
            Assert.Equal(1, scroll.Offset.Y, 5);
            Send(scroll, window, new Vector(0, -1), precise: false);
            Assert.Equal(8, scroll.Offset.Y); // The panel's wheel step remains seven items.
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReleaseContinuesScrollingOnlyWhenInertiaIsEnabled(bool enabled)
    {
        var scroll = new ScrollViewer
        {
            IsScrollInertiaEnabled = enabled,
            Content = new Border { Height = 10000 }
        };
        var window = new Window { Width = 500, Height = 300, Content = scroll };
        window.Show();
        try
        {
            window.UpdateLayout();
            for (ulong time = 16; time <= 80; time += 16)
                Send(scroll, window, new Vector(0, -4), timestamp: time);
            var released = scroll.Offset.Y;
            Send(scroll, window, new Vector(0, -1), end: true, timestamp: 88);
            released += 4; // Final motion and release can share a Wayland frame.
            Assert.Equal(released, scroll.Offset.Y);
            await Task.Delay(50);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (enabled) Assert.True(scroll.Offset.Y > released);
            else Assert.Equal(released, scroll.Offset.Y);

            // A new finger movement stops the previous fling immediately.
            Send(scroll, window, new Vector(0, 1), timestamp: 200);
            var interrupted = scroll.Offset.Y;
            await Task.Delay(50);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(interrupted, scroll.Offset.Y);

            // Holding still before lifting must not revive old momentum.
            Send(scroll, window, default, end: true, timestamp: 400);
            await Task.Delay(50);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(interrupted, scroll.Offset.Y);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void InertiaTravelIsRefreshIndependentAndStopsWhenAncestorIsHidden()
    {
        var distances = new List<double>();
        foreach (var hz in new[] { 60, 120, 180 })
        {
            var scroll = new ScrollViewer { Content = new Border { Height = 10000 } };
            var parent = new Border { Child = scroll };
            var window = new Window { Width = 500, Height = 300, Content = parent };
            window.Show();
            try
            {
                window.UpdateLayout();
                for (ulong time = 16; time <= 80; time += 16)
                    Send(scroll, window, new Vector(0, -4), timestamp: time);
                Send(scroll, window, default, end: true, timestamp: 88);
                var presenter = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
                // Drive the same render-clock entry point with exact timestamps;
                // real dispatcher timing cannot establish refresh independence.
                var advance = typeof(ScrollContentPresenter).GetMethod("AdvanceWheelInertia",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                void Frame(double ms) => advance.Invoke(presenter, [TimeSpan.FromMilliseconds(ms)]);
                Frame(0);
                for (var frame = 1; frame <= hz; frame++) Frame(frame * 1000d / hz);
                distances.Add(scroll.Offset.Y);
                parent.IsVisible = false;
                var hidden = scroll.Offset;
                Frame(1100);
                Assert.Equal(hidden, scroll.Offset);
                parent.IsVisible = true;
                Frame(1200);
                Assert.Equal(hidden, scroll.Offset); // Showing cannot resume an old fling.
            }
            finally { window.Close(); }
        }
        Assert.Equal(distances[0], distances[1], 4);
        Assert.Equal(distances[0], distances[2], 4);
        Assert.True(distances[0] > 200);
    }

    [AvaloniaFact]
    public void ReversalReleasesInTheNewDirectionAndStopsAtTheEdge()
    {
        var scroll = new ScrollViewer { Content = new Border { Height = 10000 } };
        var window = new Window { Width = 500, Height = 300, Content = scroll };
        window.Show();
        try
        {
            window.UpdateLayout();
            scroll.Offset = new Vector(0, 100);
            Send(scroll, window, new Vector(0, -4), timestamp: 16);
            Send(scroll, window, new Vector(0, -4), timestamp: 32);
            Send(scroll, window, new Vector(0, 4), timestamp: 48);
            Send(scroll, window, default, end: true, timestamp: 56);
            var presenter = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
            var advance = typeof(ScrollContentPresenter).GetMethod("AdvanceWheelInertia",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            advance.Invoke(presenter, [TimeSpan.Zero]);
            var released = scroll.Offset.Y;
            advance.Invoke(presenter, [TimeSpan.FromMilliseconds(16)]);
            Assert.True(scroll.Offset.Y < released);
            advance.Invoke(presenter, [TimeSpan.FromSeconds(10)]);
            Assert.Equal(0, scroll.Offset.Y);
            var active = typeof(ScrollContentPresenter).GetField("_wheelInertiaActive",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.False((bool)active.GetValue(presenter)!);
        }
        finally { window.Close(); }
    }

    private static void Send(Control source, Window window, Vector delta, bool precise = true,
        bool end = false, KeyModifiers modifiers = KeyModifiers.None, ulong timestamp = 0)
    {
        if (source is ScrollViewer)
            source = source.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        source.RaiseEvent(new PointerWheelEventArgs(source, pointer, window, new Point(50, 50), timestamp,
            new PointerPointProperties(), modifiers, delta) { IsPrecise = precise, IsScrollEnd = end });
    }

    private sealed class LogicalContent : Control, ILogicalScrollable
    {
        private Vector _offset;
        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public bool IsLogicalScrollEnabled => true;
        public Size Extent => new(100, 100);
        public Size Viewport => new(10, 10);
        public Size ScrollSize => new(5, 7);
        public Size PageScrollSize => Viewport;
        public Vector Offset
        {
            get => _offset;
            set { _offset = value; RaiseScrollInvalidated(EventArgs.Empty); }
        }
        public event EventHandler? ScrollInvalidated;
        public void RaiseScrollInvalidated(EventArgs e) => ScrollInvalidated?.Invoke(this, e);
        public bool BringIntoView(Control target, Rect targetRect) => false;
        public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;
        protected override Size MeasureOverride(Size availableSize) => new(500, 300);
    }
}
