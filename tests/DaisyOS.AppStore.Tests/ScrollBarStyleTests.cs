using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class ScrollBarStyleTests
{
    [AvaloniaTheory]
    [InlineData(false, false)][InlineData(false, true)][InlineData(true, false)][InlineData(true, true)]
    public void SharedScrollbarSupportsDraggingAndPageClicks(bool horizontal, bool dark)
    {
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Visible : ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Visible,
            AllowAutoHide = true, // Explicit local opt-in must still reserve a separate rail.
            Padding = new Thickness(24),
            Content = new Border { Width = horizontal ? 1800 : 300, Height = horizontal ? 200 : 1800, MaxWidth = horizontal ? double.PositiveInfinity : 320, Margin = new Thickness(20) }
        };
        var window = new Window { Width = 500, Height = 400, Content = scroller, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://DaisyOS.Shell/"))
        { Source = new Uri("avares://DaisyOS.Shell/Styles/ScrollBarStyles.axaml") });
        window.Show();
        try
        {
            Layout();
            var bar = scroller.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.IsVisible);
            Assert.Equal(horizontal ? Orientation.Horizontal : Orientation.Vertical, bar.Orientation);
            Assert.Equal(14, horizontal ? bar.Bounds.Height : bar.Bounds.Width, 1);
            var presenter = scroller.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
            var barPoint = bar.TranslatePoint(default, scroller)!.Value;
            var contentPoint = presenter.TranslatePoint(default, scroller)!.Value;
            if (horizontal)
            {
                Assert.Equal(scroller.Bounds.Height, barPoint.Y + bar.Bounds.Height, 1);
                Assert.True(contentPoint.Y + presenter.Bounds.Height <= barPoint.Y + 1, $"Content {contentPoint.Y}+{presenter.Bounds.Height}, rail {barPoint.Y}, spans {Grid.GetRowSpan(presenter)}");
            }
            else
            {
                Assert.Equal(scroller.Bounds.Width, barPoint.X + bar.Bounds.Width, 1);
                Assert.True(contentPoint.X + presenter.Bounds.Width <= barPoint.X + 1, $"Content {contentPoint.X}+{presenter.Bounds.Width}, rail {barPoint.X}, spans {Grid.GetColumnSpan(presenter)}");
            }
            var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
            Assert.NotNull(thumb.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ThumbVisual"));
            var start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
            var end = start + (horizontal ? new Vector(70, 0) : new Vector(0, 70));
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            var outside = horizontal ? end + new Vector(0, -80) : end + new Vector(-80, 0);
            window.MouseMove(outside, RawInputModifiers.LeftMouseButton); Layout();
            Assert.False(bar.IsPointerOver);
            Assert.Contains("thumb-dragging", bar.Classes);
            var rail = bar.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ScrollRail");
            Assert.NotEqual(Avalonia.Media.Colors.Transparent,
                Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(rail.Background).Color);
            window.MouseUp(end, MouseButton.Left); Layout();
            Assert.DoesNotContain("thumb-dragging", bar.Classes);
            Assert.True(horizontal ? scroller.Offset.X > 0 : scroller.Offset.Y > 0);
            scroller.Offset = default; Layout();
            var page = bar.GetVisualDescendants().OfType<RepeatButton>().Single(b => b.Name == "PART_PageDownButton");
            var point = page.TranslatePoint(new Point(page.Bounds.Width / 2, page.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout();
            Assert.True(horizontal ? scroller.Offset.X > 0 : scroller.Offset.Y > 0);
        }
        finally { window.Close(); }
    }
    private static void Layout() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
}
