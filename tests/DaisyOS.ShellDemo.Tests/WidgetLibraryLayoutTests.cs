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
    public void EveryCategoryFitsWithoutScrollingOrOverlapping(double width, double height, bool dark)
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
            Assert.Contains(library.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "4 widget families");
            foreach (var category in new[] { "Clock", "Calendar", "Media", "Photos" })
            {
                library.FindControl<Button>($"{category}CategoryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                var area = library.FindControl<Grid>("PreviewArea")!;
                var panel = library.FindControl<Control>($"{category}PreviewsPanel")!;
                var cards = panel.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("PreviewCard")).ToArray();
                Assert.Equal(category is "Photos" or "Clock" or "Media" ? 3 : 2, cards.Length);
                Assert.Equal(cards.Length, panel.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("SizeBadge")));
                var rectangles = cards.Select(card =>
                {
                    Assert.True(card.Bounds.Width > 50 && card.Bounds.Height > 50);
                    var rectangle = new Rect(card.TranslatePoint(default, area)!.Value, card.Bounds.Size);
                    Assert.True(rectangle.Left >= -0.1 && rectangle.Top >= -0.1 && rectangle.Right <= area.Bounds.Width + 0.1 && rectangle.Bottom <= area.Bounds.Height + 0.1,
                        $"{category}: {rectangle} exceeds {area.Bounds.Size}");
                    return rectangle;
                }).ToArray();
                for (var i = 0; i < rectangles.Length; i++)
                    for (var j = i + 1; j < rectangles.Length; j++) Assert.False(rectangles[i].Intersects(rectangles[j]));
                Assert.Empty(panel.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.All(cards, card => Assert.Equal(Avalonia.Media.Colors.Transparent,
                    Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(card.Background).Color));
                double mediumWidth = cards[0].Bounds.Width;
                double mediumHeight = cards[0].Bounds.Height;
                Assert.InRange(mediumWidth / mediumHeight, 222d / 176 - 0.02, 222d / 176 + 0.02);
                if (category == "Calendar")
                    Assert.InRange(cards[1].Bounds.Width / mediumWidth, 4d / 3 - 0.02, 4d / 3 + 0.02);
                if (category == "Media")
                {
                    Assert.Equal("5,6", cards[2].Tag);
                    Assert.InRange(cards[1].Bounds.Width / mediumWidth, 5d / 3 - 0.02, 5d / 3 + 0.02);
                    Assert.InRange(cards[2].Bounds.Height / mediumHeight, 2.98, 3.02);
                }
                if (category == "Photos")
                {
                    Assert.Empty(panel.GetVisualDescendants().OfType<Image>());
                    Assert.InRange(cards[1].Bounds.Width / mediumWidth, 4d / 3 - 0.02, 4d / 3 + 0.02);
                    Assert.InRange(cards[2].Bounds.Width / mediumWidth, 5d / 3 - 0.02, 5d / 3 + 0.02);
                    Assert.InRange(cards[2].Bounds.Height / mediumHeight, 1.98, 2.02);
                }
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save($"/tmp/daisy-widget-{category}-{width}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
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
