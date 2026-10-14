using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class CustomToggleTests
{
    [AvaloniaFact]
    public async Task ThumbMovesThroughIntermediatePositionsAndTrackSettlesPromptly()
    {
        var toggle = new ToggleSwitch { OnContent = null, OffContent = null };
        var window = new Window { Content = toggle, Width = 180, Height = 100 };
        window.Show();
        try
        {
            await Task.Delay(30);
            window.CaptureRenderedFrame()!.Dispose();
            var moving = toggle.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_MovingKnobs");
            var track = toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ToggleTrack");
            Assert.NotNull(moving.Transitions);
            Assert.Equal(TimeSpan.FromMilliseconds(100), ((Avalonia.Animation.DoubleTransition)moving.Transitions![0]).Duration);
            Assert.Equal(TimeSpan.FromMilliseconds(100), ((Avalonia.Animation.BrushTransition)track.Transitions![0]).Duration);
            toggle.IsChecked = true;
            // Capturing a frame can drain short animations; sample timer ticks instead.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(20);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.InRange(Canvas.GetLeft(moving), 0.1, 23.9);
            await Task.Delay(200);
            window.CaptureRenderedFrame()!.Dispose();
            Assert.Equal(24, Canvas.GetLeft(moving));
            Assert.True(toggle.TryFindResource("ActionPrimaryBrush", toggle.ActualThemeVariant, out var onBrush));
            Assert.Equal(((ISolidColorBrush)onBrush!).Color, ((ISolidColorBrush)track.Background!).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PartialDragReleaseAnimatesFromTheDraggedPosition()
    {
        var toggle = new ToggleSwitch { OnContent = null, OffContent = null };
        var window = new Window { Content = toggle, Width = 180, Height = 100 };
        window.Show();
        try
        {
            await Task.Delay(30);
            window.CaptureRenderedFrame()!.Dispose();
            var moving = toggle.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_MovingKnobs");
            var start = moving.TranslatePoint(new Point(14, 10), window)!.Value;
            var end = start + new Vector(14, 0);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            Assert.True(toggle.IsChecked);
            Assert.InRange(Canvas.GetLeft(moving), 14, 23.9);
            await Task.Delay(150);
            window.CaptureRenderedFrame()!.Dispose();
            Assert.Equal(24, Canvas.GetLeft(moving));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReducedMotionResourcesSettleThumbAndColorImmediately()
    {
        var toggle = new ToggleSwitch { OnContent = null, OffContent = null };
        toggle.Resources["ToggleThumbTransitions"] = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.DoubleTransition { Property = Canvas.LeftProperty, Duration = TimeSpan.Zero }
        };
        toggle.Resources["ToggleTrackTransitions"] = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.Zero }
        };
        var window = new Window { Content = toggle, Width = 180, Height = 100 };
        window.Show();
        try
        {
            await Task.Delay(30);
            var moving = toggle.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_MovingKnobs");
            var track = toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ToggleTrack");
            toggle.IsChecked = true;
            window.CaptureRenderedFrame()!.Dispose();
            Assert.Equal(24, Canvas.GetLeft(moving));
            Assert.True(toggle.TryFindResource("ActionPrimaryBrush", toggle.ActualThemeVariant, out var onBrush));
            Assert.Equal(((ISolidColorBrush)onBrush!).Color, ((ISolidColorBrush)track.Background!).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PillToggleSupportsClickKeyboardAndDragInBothThemes(bool dark)
    {
        var toggle = new ToggleSwitch { OnContent = null, OffContent = null };
        var window = new Window { Width = 220, Height = 100, Padding = new Thickness(16), Content = toggle,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout();
            var track = toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ToggleTrack");
            var thumb = toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ToggleThumb");
            Assert.Equal(new Size(56, 24), track.Bounds.Size);
            Assert.Equal(new Size(28, 20), thumb.Bounds.Size);
            Assert.Equal(Color.Parse(dark ? "#4B4B4B" : "#B6B6B6"), ((ISolidColorBrush)track.Background!).Color);
            var point = track.TranslatePoint(new Point(34, 12), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(toggle.IsChecked);
            toggle.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.False(toggle.IsChecked);
            var start = thumb.TranslatePoint(new Point(14, 10), window)!.Value;
            var end = start + new Vector(24, 0);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            Assert.True(toggle.IsChecked);
            toggle.IsEnabled = false;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(toggle.IsChecked);
            toggle.IsEnabled = true;
            toggle.IsChecked = false;
            window.UpdateLayout();
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save($"/tmp/daisy-toggle-{dark}.png");
        }
        finally { window.Close(); }
    }
}
