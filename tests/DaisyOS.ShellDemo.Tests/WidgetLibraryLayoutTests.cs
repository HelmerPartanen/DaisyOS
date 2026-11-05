using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DaisyOS.Shell.Views.Components.Widgets;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class WidgetLibraryLayoutTests
{
    [AvaloniaFact]
    public void PhotoWidgetsStartEmptyAndIgnoreUnavailableImages()
    {
        var widget = new DaisyOS.Shell.DesktopWidgets.ImageWidget();
        Assert.Empty(widget.ImagePaths);
        widget.SetImages(["avares://DaisyOS.Shell/missing.jpg", "/nonexistent/photo.jpg"]);
        Assert.Empty(widget.ImagePaths);
        Assert.True(widget.FindControl<Border>("PlaceholderView")!.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(1024, 768, true)]
    [InlineData(640, 560, false)]
    public void EveryCategoryShowsOneLiveVariantAtATime(double width, double height, bool dark)
    {
        var library = new WidgetLibraryOverlay();
        library.Resources["MotionDurationEnter"] = TimeSpan.Zero;
        var window = new Window { Width = width, Height = height, Content = library,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        library.Open();
        try
        {
            window.UpdateLayout();
            Assert.Contains(library.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Widget Gallery");
            var expected = new Dictionary<string, (Type Widget, string[] Tags)>
            {
                ["Clock"] = (typeof(Control), ["3,2", "3,2", "3,2", "3,2"]),
                ["Calendar"] = (typeof(DaisyOS.Shell.DesktopWidgets.CalendarWidget), ["3,2", "4,2"]),
                ["Media"] = (typeof(DaisyOS.Shell.DesktopWidgets.MediaWidget), ["3,2", "5,2", "5,6"]),
                ["Photos"] = (typeof(DaisyOS.Shell.DesktopWidgets.ImageWidget), ["3,2", "4,2", "5,4"]),
            };
            var viewport = library.FindControl<Grid>("CarouselViewport")!;
            Assert.Null(library.FindControl<Button>("NextVariantButton"));
            Assert.Null(library.FindControl<Button>("PreviousVariantButton"));
            foreach (var (category, (widgetType, tags)) in expected)
            {
                library.FindControl<Button>($"{category}CategoryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                var cards = library.FindControl<Grid>("CarouselTrack")!.Children.OfType<Border>().ToArray();
                Assert.Equal(tags, cards.Select(card => (string)card.Tag!).ToArray());
                Assert.Equal(cards.Length, library.FindControl<StackPanel>("PageDots")!.Children.Count);
                Assert.Empty(library.GetVisualDescendants().OfType<ScrollViewer>());
                if (widgetType != typeof(Control))
                    Assert.All(cards, card => Assert.IsType(widgetType, ((Grid)((Viewbox)card.Child!).Child!).Children.Single()));
                Assert.All(cards, card => Assert.False(((Grid)((Viewbox)card.Child!).Child!).Children.Single().IsHitTestVisible));

                for (var i = 0; i < cards.Length; i++)
                {
                    library.GoTo(i);
                    window.UpdateLayout();
                    var active = cards[i];
                    Assert.Equal(1, active.Opacity);
                    var group = Assert.IsType<Avalonia.Media.TransformGroup>(active.RenderTransform);
                    Assert.Contains(group.Children, t => t is Avalonia.Media.Rotate3DTransform);
                    var rect = new Rect(active.TranslatePoint(default, viewport)!.Value, active.Bounds.Size);
                    Assert.True(rect.Width > 40 && rect.Height > 40);
                    Assert.True(rect.Left >= -0.5 && rect.Top >= -0.5 && rect.Right <= viewport.Bounds.Width + 0.5 && rect.Bottom <= viewport.Bounds.Height + 0.5,
                        $"{category}[{i}]: {rect} exceeds {viewport.Bounds.Size}");
                    // Every other variant is dimmed and turned away; only one is in focus.
                    Assert.All(cards.Where(card => card != active), card => Assert.True(card.Opacity < 0.5));
                }
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save($"/tmp/daisy-widget-{category}-{width}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AddWidgetReportsTheDisplayedVariant()
    {
        var library = new WidgetLibraryOverlay();
        library.Resources["MotionDurationEnter"] = TimeSpan.Zero;
        var window = new Window { Width = 1024, Height = 768, Content = library };
        window.Show();
        try
        {
            library.Open();
            (string, int, int)? request = null;
            library.WidgetAddRequested += (type, columns, rows) => request = (type, columns, rows);
            library.FindControl<Button>("PhotosCategoryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            library.GoTo(1);
            library.FindControl<Button>("AddWidgetButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(("image", 4, 2), request);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task LibraryScalesInAndReleasesInputBeforeFinishingItsExit()
    {
        var library = new WidgetLibraryOverlay();
        library.Resources["MotionDurationEnter"] = TimeSpan.FromMilliseconds(80);
        library.Resources["MotionDurationExit"] = TimeSpan.FromMilliseconds(80);
        var window = new Window { Width = 1024, Height = 768, Content = library };
        window.Show();
        try
        {
            var frame = library.FindControl<Control>("LibraryFrame")!;
            Assert.False(library.IsVisible);

            library.Open();
            Assert.True(library.IsVisible);
            Assert.True(library.IsOpen);
            Assert.True(library.IsHitTestVisible);
            Assert.NotNull(frame.RenderTransform);
            Assert.InRange(frame.RenderTransform!.Value.M11, 0.9, 0.999);
            Assert.InRange(frame.Opacity, 0, 0.999);

            await SettleAsync(() => frame.RenderTransform is null && library.IsHitTestVisible);
            Assert.Equal(1, frame.Opacity);
            Assert.Null(frame.RenderTransform);

            library.Dismiss();
            // Input is released the moment dismissal starts even though the sheet
            // is still painting its exit.
            Assert.False(library.IsHitTestVisible);
            Assert.False(library.IsOpen);
            Assert.NotNull(frame.RenderTransform);

            await SettleAsync(() => !library.IsVisible);
            Assert.False(library.IsVisible);
            Assert.Equal(1, frame.Opacity);
            Assert.Null(frame.RenderTransform);
        }
        finally { window.Close(); }

        static async Task SettleAsync(Func<bool> condition)
        {
            for (var tick = 0; tick < 60 && !condition(); tick++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(10);
            }
            Assert.True(condition());
        }
    }

    [AvaloniaFact]
    public async Task ReopeningDuringExitReportsNoDismissalAndKeepsTheSheetInteractive()
    {
        var library = new WidgetLibraryOverlay();
        library.Resources["MotionDurationEnter"] = TimeSpan.FromMilliseconds(120);
        library.Resources["MotionDurationExit"] = TimeSpan.FromMilliseconds(120);
        var window = new Window { Width = 1024, Height = 768, Content = library };
        window.Show();
        try
        {
            var frame = library.FindControl<Control>("LibraryFrame")!;
            var dismissals = 0;
            library.Dismissed += () => dismissals++;

            library.Open();
            await SettleAsync(() => library.IsHitTestVisible && frame.RenderTransform is null);

            library.Dismiss();
            library.Open();
            await SettleAsync(() => library.IsHitTestVisible && frame.RenderTransform is null);

            Assert.Equal(0, dismissals);
            Assert.True(library.IsOpen);
            Assert.True(library.IsVisible);
            Assert.Equal(1, frame.Opacity);
        }
        finally { window.Close(); }

        static async Task SettleAsync(Func<bool> condition)
        {
            for (var tick = 0; tick < 60 && !condition(); tick++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(10);
            }
            Assert.True(condition());
        }
    }
}
