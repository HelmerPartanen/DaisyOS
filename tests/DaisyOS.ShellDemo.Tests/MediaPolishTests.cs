using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using DaisyOS.Shell.Views.Components.Widgets;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Controls;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class MediaPolishTests
{
    [AvaloniaFact]
    public void ArtworkTintIsOpaqueAndClearingItRestoresTheExistingMaterial()
    {
        foreach (var widget in new UserControl[]
        {
            new DaisyOS.Shell.DesktopWidgets.MediaWidget(),
            new DaisyOS.Shell.Views.Components.Widgets.MediaWidget(),
            new DaisyOS.Shell.Views.Components.Console.ConsoleMediaWidget()
        })
        {
            var tint = widget.FindControl<Border>(widget is DaisyOS.Shell.Views.Components.Console.ConsoleMediaWidget
                ? "MediaFrame" : "ArtworkTint")!;
            var field = widget.GetType().GetField("_artworkTint", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var update = widget.GetType().GetMethod("UpdateTintLayer", BindingFlags.NonPublic | BindingFlags.Instance)!;
            field.SetValue(widget, Color.FromRgb(180, 80, 40));
            update.Invoke(widget, null);
            var solid = Assert.IsAssignableFrom<ISolidColorBrush>(tint.Background);
            Assert.Equal(byte.MaxValue, solid.Color.A);
            Assert.Equal(1, solid.Opacity);
            field.SetValue(widget, null);
            update.Invoke(widget, null);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(tint.Background).Color.A);
        }
    }

    [AvaloniaFact]
    public void RollingTitleAlwaysHasAVisibleCopyAcrossMultipleLoops()
    {
        var title = new MarqueeText { Text = "A long title that must repeat without an empty interval", Width = 100 };
        var window = new Window { Width = 400, Height = 100, Content = title };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var copies = title.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Equal(2, copies.Length);
            Assert.All(copies, copy => Assert.True(copy.IsVisible));
            var width = copies[0].Bounds.Width;
            var gap = copies[1].TranslatePoint(default, title)!.Value.X -
                copies[0].TranslatePoint(default, title)!.Value.X - width;
            Assert.InRange(gap, 0, 10.01);
            for (var frame = 0; frame < 1200; frame++)
            {
                title.Advance((width + 16) / 28 / 300);
                Assert.Contains(copies, copy =>
                {
                    var x = copy.TranslatePoint(default, title)!.Value.X;
                    return x < title.Bounds.Width && x + copy.Bounds.Width > 0;
                });
            }
            title.Text = "Short";
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.False(copies[1].IsVisible);
            Assert.Equal(0, Assert.IsType<TranslateTransform>(copies[0].RenderTransform).X);
            title.TextAlignment = TextAlignment.Center;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal((title.Bounds.Width - copies[0].DesiredSize.Width) / 2,
                Assert.IsType<TranslateTransform>(copies[0].RenderTransform).X, precision: 2);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LongTitleMovesLeftAndStopsWhenItFits()
    {
        var title = new MarqueeText { Text = "A very long track title which should move across its available space", Width = 100 };
        var window = new Window { Width = 400, Height = 100, Content = title };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            await Task.Delay(200);
            Dispatcher.UIThread.RunJobs();
            var text = title.GetVisualDescendants().OfType<TextBlock>().First();
            var transform = Assert.IsType<TranslateTransform>(text.RenderTransform);
            Assert.True(transform.X < 0, $"Expected leftward movement, got {transform.X}");
            title.Text = "Short";
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, transform.X);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ExtraLargeGalleryPreviewStartsTheCorrectWidgetDrag()
    {
        var library = new WidgetLibraryOverlay();
        var window = new Window { Width = 1024, Height = 768, Content = library };
        window.Show();
        try
        {
            library.Open();
            library.FindControl<Button>("MediaCategoryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            library.GoTo(2);
            window.UpdateLayout();
            var track = library.FindControl<Grid>("CarouselTrack")!;
            var preview = track.Children[2] as Control;
            Assert.NotNull(preview);
            (string type, int columns, int rows)? request = null;
            library.WidgetDragRequested += (type, columns, rows, _, _, _) => request = (type, columns, rows);
            using var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
            var press = new PointerPressedEventArgs(preview, pointer, window,
                new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2), 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None);
            preview.RaiseEvent(press);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(("media", 5, 6), request);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SpectrumBarsShareAStationaryCenter()
    {
        foreach (var widget in new UserControl[] { new DaisyOS.Shell.DesktopWidgets.MediaWidget(), new DaisyOS.Shell.Views.Components.Widgets.MediaWidget(), new DaisyOS.Shell.Views.Components.Console.ConsoleMediaWidget() })
        {
            widget.GetType().GetMethod("BuildSpectrum", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(widget, null);
            var canvas = widget.FindControl<Canvas>(widget is DaisyOS.Shell.DesktopWidgets.MediaWidget ? "LargeSpectrumCanvas" : "SpectrumCanvas")!;
            var bars = canvas.Children.OfType<Border>().ToArray();
            Assert.Equal(6, bars.Length);
            var method = widget.GetType().GetMethod("SetSpectrumBarScale", BindingFlags.NonPublic | BindingFlags.Static)!;
            for (var i = 0; i < bars.Length; i++)
            {
                var args = method.GetParameters().Length == 3 ? new object[] { bars[i], 0.2 + i * 0.1, 28d } : new object[] { bars[i], 0.2 + i * 0.1 };
                method.Invoke(null, args);
                Assert.Equal(24, bars[i].Height);
                Assert.Equal(canvas.Height / 2, Canvas.GetTop(bars[i]) + bars[i].Height / 2);
                Assert.Equal(0.5, bars[i].RenderTransformOrigin.Point.Y);
            }
        }
    }
}
