using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class SelectionControlTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeAccentPreferencesKeepPrimaryLabelsWhite(bool dark)
    {
        var app = new DaisyOS.Shell.App { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        const global::System.Reflection.BindingFlags flags = global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic;
        var settings = (DaisyOS.Shell.Services.Appearance.AppearanceSettings)typeof(DaisyOS.Shell.App)
            .GetField("_appearanceSettings", flags)!.GetValue(app)!;
        var apply = typeof(DaisyOS.Shell.App).GetMethod("ApplyAccentPreference", flags)!;
        foreach (var accent in Enum.GetValues<DaisyOS.Core.Models.AccentColor>())
        {
            settings.AccentColor = accent;
            apply.Invoke(app, null);
            Assert.Equal(Colors.White, ((ISolidColorBrush)app.Resources["ContentOnPrimaryActionBrush"]!).Color);
            Assert.Equal(Colors.White, app.Resources["ContentOnPrimaryActionColor"]);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomChoicesRenderAllStatesAndKeepNativeInput(bool dark)
    {
        var check = new CheckBox { Content = "Vain vahvistetut", IsThreeState = true };
        var first = new RadioButton { Content = "First output", GroupName = "outputs", IsChecked = true };
        var second = new RadioButton { Content = "Second output", GroupName = "outputs" };
        var stack = new StackPanel { Spacing = 8, Children =
        {
            check, new CheckBox { Content = "Selected", IsChecked = true },
            new CheckBox { Content = "Mixed selection", IsThreeState = true, IsChecked = null },
            new CheckBox { Content = "Disabled", IsChecked = true, IsEnabled = false },
            new CheckBox { Content = "A longer label wraps onto another line without crowding the indicator", Width = 240 },
            first, second, new RadioButton { Content = "Disabled radio", IsChecked = true, IsEnabled = false, GroupName = "disabled" }
        }};
        var window = new Window { Width = 300, SizeToContent = SizeToContent.Height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new Border { Padding = new Thickness(16), Child = stack,
                Background = dark ? Brushes.Black : Brushes.White } };
        window.Show();
        try
        {
            window.UpdateLayout();
            foreach (var choice in stack.Children.Cast<ToggleButton>())
            {
                var indicator = Part<Border>(choice, "SelectionIndicator");
                Assert.Equal(new Size(20, 20), indicator.Bounds.Size);
                Assert.True(choice.Bounds.Height >= 32);
                Assert.Equal(choice.IsChecked == true, Part<Control>(choice, "SelectionGlyph").IsVisible);
            }
            Assert.True(Part<Border>((CheckBox)stack.Children[2], "IndeterminateGlyph").IsVisible);
            using (var frame = window.CaptureRenderedFrame()) frame!.Save($"/tmp/daisy-choices-{dark}.png");

            var label = Part<ContentPresenter>(check, "PART_ContentPresenter");
            var click = label.TranslatePoint(new Point(20, label.Bounds.Height / 2), window)!.Value;
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Assert.True(check.IsChecked);
            window.MouseMove(new Point(window.ClientSize.Width - 1, 1));
            Assert.True(Part<Control>(check, "SelectionGlyph").IsVisible);
            await Task.Delay(200);
            using (window.CaptureRenderedFrame()) { }
            Assert.True(check.TryFindResource("ActionPrimaryBrush", check.ActualThemeVariant, out var accent));
            Assert.Equal(((ISolidColorBrush)accent!).Color, ((ISolidColorBrush)Part<Border>(check, "SelectionIndicator").Background!).Color);
            first.Focus();
            Assert.True(check.Focus(NavigationMethod.Tab));
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.Null(check.IsChecked);
            Assert.False(Part<Control>(check, "SelectionGlyph").IsVisible);
            Assert.True(Part<Border>(check, "IndeterminateGlyph").IsVisible);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.False(check.IsChecked);
            Assert.False(Part<Border>(check, "IndeterminateGlyph").IsVisible);
            Assert.Equal(new Size(26, 26), Part<Border>(check, "ChoiceFocusOutline").Bounds.Size);
            Assert.True(check.TryFindResource("FocusRingBrush", check.ActualThemeVariant, out var focus));
            Assert.Equal(((ISolidColorBrush)focus!).Color, ((ISolidColorBrush)Part<Border>(check, "ChoiceFocusOutline").BorderBrush!).Color);

            second.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(second.IsChecked);
            Assert.False(first.IsChecked);
            var disabled = (CheckBox)stack.Children[3];
            var disabledClick = disabled.TranslatePoint(new Point(10, 16), window)!.Value;
            window.MouseDown(disabledClick, MouseButton.Left);
            window.MouseUp(disabledClick, MouseButton.Left);
            Assert.True(disabled.IsChecked);
            Assert.Equal(.42, disabled.Opacity);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HoverFramesStayWithinThemeColors(bool dark)
    {
        var check = new CheckBox { Content = "Checkbox" };
        var radio = new RadioButton { Content = "Radio" };
        var window = new Window { Width = 280, Height = 150,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new StackPanel { Children = { check, radio } } };
        window.Show();
        try
        {
            window.UpdateLayout();
            foreach (var choice in new ToggleButton[] { check, radio })
            {
                var indicator = Part<Border>(choice, "SelectionIndicator");
                choice.TryFindResource(choice is CheckBox ? "ActionSecondaryBrush" : "SurfaceSunkenBrush", choice.ActualThemeVariant, out var normal);
                choice.TryFindResource(choice is CheckBox ? "ActionSecondaryHoverBrush" : "SurfaceInputHoverBrush", choice.ActualThemeVariant, out var hover);
                var a = ((ISolidColorBrush)normal!).Color;
                var b = ((ISolidColorBrush)hover!).Color;
                foreach (var over in new[] { true, false, true, false })
                {
                    window.MouseMove(over ? choice.TranslatePoint(new Point(12, 16), window)!.Value : new Point(270, 140));
                    for (var frame = 0; frame < 12; frame++)
                    {
                        await Task.Delay(15);
                        using (window.CaptureRenderedFrame()) { }
                        var color = ((ISolidColorBrush)indicator.Background!).Color;
                        Assert.InRange(color.A, Math.Min(a.A, b.A), Math.Max(a.A, b.A));
                        Assert.InRange(color.R, Math.Min(a.R, b.R), Math.Max(a.R, b.R));
                        Assert.InRange(color.G, Math.Min(a.G, b.G), Math.Max(a.G, b.G));
                        Assert.InRange(color.B, Math.Min(a.B, b.B), Math.Max(a.B, b.B));
                    }
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrimaryLabelsAreWhiteAndCheckboxSurfacesMatchComboBoxes(bool dark)
    {
        var primary = new Button { Content = "Open", Classes = { "Primary" } };
        var check = new CheckBox { Content = "Verified only" };
        var combo = new ComboBox { ItemsSource = new[] { "All applications" }, SelectedIndex = 0 };
        var window = new Window
        {
            Width = 320, Height = 240,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 12,
                Children = { primary, check, combo } }
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.Equal(Colors.White, ((ISolidColorBrush)primary.Foreground!).Color);
            Assert.Equal(Colors.White, ((ISolidColorBrush)primary.GetVisualDescendants()
                .OfType<TextBlock>().Single(text => text.Text == "Open").Foreground!).Color);
            foreach (var state in new[] { ":pointerover", ":pressed", ":disabled" })
            {
                var pseudoClasses = (IPseudoClasses)primary.Classes;
                pseudoClasses.Set(state, true);
                window.UpdateLayout();
                Assert.Equal(Colors.White, ((ISolidColorBrush)Part<ContentPresenter>(primary, "PART_ContentPresenter").Foreground!).Color);
                Assert.Equal(Colors.White, ((ISolidColorBrush)primary.GetVisualDescendants()
                    .OfType<TextBlock>().Single(text => text.Text == "Open").Foreground!).Color);
                pseudoClasses.Set(state, false);
            }
            var indicator = Part<Border>(check, "SelectionIndicator");
            var surface = Part<Border>(combo, "Background");
            window.MouseMove(new Point(310, 230));
            await Task.Delay(250);
            using (window.CaptureRenderedFrame()) { }
            Assert.Equal(((ISolidColorBrush)surface.Background!).Color, ((ISolidColorBrush)indicator.Background!).Color);
            Assert.Equal(((ISolidColorBrush)surface.BorderBrush!).Color, ((ISolidColorBrush)indicator.BorderBrush!).Color);
            Assert.Equal(surface.BorderThickness, indicator.BorderThickness);
            window.MouseMove(check.TranslatePoint(new Point(12, 16), window)!.Value);
            await Task.Delay(250);
            using (window.CaptureRenderedFrame()) { }
            var hoverBackground = ((ISolidColorBrush)indicator.Background!).Color;
            var hoverBorder = ((ISolidColorBrush)indicator.BorderBrush!).Color;
            window.MouseMove(combo.TranslatePoint(new Point(12, 16), window)!.Value);
            await Task.Delay(250);
            using (window.CaptureRenderedFrame()) { }
            Assert.Equal(((ISolidColorBrush)surface.Background!).Color, hoverBackground);
            Assert.Equal(((ISolidColorBrush)surface.BorderBrush!).Color, hoverBorder);
            check.IsChecked = true;
            window.MouseMove(new Point(310, 230));
            await Task.Delay(250);
            using (window.CaptureRenderedFrame()) { }
            Assert.True(check.TryFindResource("ActionPrimaryBrush", check.ActualThemeVariant, out var accent));
            Assert.Equal(((ISolidColorBrush)accent!).Color, ((ISolidColorBrush)indicator.Background!).Color);
            using var frame = window.CaptureRenderedFrame();
            frame!.Save($"/tmp/daisy-primary-checkbox-{dark}.png");
        }
        finally { window.Close(); }
    }

    private static T Part<T>(Control control, string name) where T : Control =>
        control.GetVisualDescendants().OfType<T>().Single(part => part.Name == name);
}
