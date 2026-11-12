using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DaisyOS.Shell.Controls;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class SliderThemeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlainSlidersUseDaisyPresentationAndPreserveInputAndValueBinding(bool dark)
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 25, Margin = new Thickness(20) };
        var window = new Window { Content = slider, Width = 320, Height = 100,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout();
            using (window.CaptureRenderedFrame()) { }
            var range = Assert.Single(slider.GetVisualDescendants().OfType<QuickSettingsSlider>());
            Assert.Empty(slider.GetVisualDescendants().OfType<Track>());
            Assert.Equal(25, range.Value);
            slider.Value = 80;
            Assert.Equal(80, range.Value);
            range.Value = 35;
            Assert.Equal(35, slider.Value);
            var track = range.FindControl<Border>("TrackBackground")!;
            var point = track.TranslatePoint(new Point(track.Bounds.Width * .75, track.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.InRange(slider.Value, 74, 76);
            range.Focus();
            window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.End, RawInputModifiers.None);
            Assert.Equal(100, slider.Value);
            slider.IsEnabled = false;
            point = track.TranslatePoint(new Point(track.Bounds.Width * .25, track.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal(100, slider.Value);
        }
        finally { window.Close(); }
    }
}
