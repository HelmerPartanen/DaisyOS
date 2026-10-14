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
            foreach (var category in new[] { "Clock", "Calendar", "Media", "Photos" })
            {
                library.FindControl<Button>($"{category}CategoryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                var area = library.FindControl<Grid>("PreviewArea")!;
                var panel = library.FindControl<Control>($"{category}PreviewsPanel")!;
                var cards = panel.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("PreviewCard")).ToArray();
                Assert.Equal(category is "Photos" or "Clock" ? 3 : 2, cards.Length);
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
                    Assert.InRange(cards[1].Bounds.Width / mediumWidth, 5d / 3 - 0.02, 5d / 3 + 0.02);
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
}
