using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using DaisyOS.Shell.Views.Windows;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class LockScreenClockBlurTests
{
    [AvaloniaFact]
    public async Task ClockMaskFollowsGlyphsAnimationAndVisibility()
    {
        var window = new LockScreenWindow("solid:#2E3440", isSecondary: true) { Width = 1280, Height = 720 };
        window.Show();
        try
        {
            await Task.Delay(400);
            window.UpdateLayout();
            var clock = window.FindControl<TextBlock>("ClockText")!;
            var previous = window.FindControl<TextBlock>("PreviousClockText")!;
            var area = window.FindControl<StackPanel>("ClockArea")!;
            var original = window.GetClockBlurRegions().ToArray();
            Assert.NotEmpty(original);
            Assert.All(original, region => Assert.Equal(1, region.Height));
            var boundsArea = (original.Max(r => r.X + r.Width) - original.Min(r => r.X))
                * (original.Max(r => r.Y + r.Height) - original.Min(r => r.Y));
            Assert.True(original.Sum(r => r.Width * r.Height) < boundsArea * 0.7);
            // Both clock layers contribute during the minute crossfade.
            previous.Text = "11:11";
            previous.Opacity = 0.5;
            window.UpdateLayout();
            Assert.True(window.GetClockBlurRegions().Count() > original.Length);
            previous.Opacity = 0;
            area.Transitions = null;
            area.RenderTransform = new TranslateTransform(0, 12);
            var moved = window.GetClockBlurRegions().ToArray();
            Assert.Equal(original.Length, moved.Length);
            Assert.Equal(original[0].Y + 12, moved[0].Y);
            clock.Opacity = 0;
            Assert.Empty(window.GetClockBlurRegions());
            clock.Opacity = 1;
            area.IsVisible = false;
            Assert.Empty(window.GetClockBlurRegions());
        }
        finally { window.Close(); }
    }
}
