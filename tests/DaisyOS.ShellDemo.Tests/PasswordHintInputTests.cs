using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.System.Settings;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class PasswordHintInputTests
{
    [AvaloniaTheory]
    [InlineData(false, 760, 1d)]
    [InlineData(true, 760, 1.5d)]
    [InlineData(false, 1100, 1.5d)]
    [InlineData(true, 1100, 1d)]
    public async Task HoverAndFocusKeepPaintAndGeometryStableWithoutInterruptingTyping(bool dark, int width, double scale)
    {
        var view = new SettingsView();
        var window = new Window { Content = view, Width = width, Height = 800,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        window.SetRenderScaling(scale);
        try
        {
            view.SelectCategory("Password");
            await view.AccountPageReady;
            window.UpdateLayout();
            var input = view.GetVisualDescendants().OfType<TextBox>().Single(text => text.Name == "PasswordHint");
            input.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            using (window.CaptureRenderedFrame()) { }
            var surface = input.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_BorderElement");
            var background = Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color;
            var bounds = input.Bounds;
            var borderThickness = surface.BorderThickness;
            for (var focused = 0; focused < 2; focused++)
            {
                if (focused == 1)
                {
                    var center = input.TranslatePoint(new Point(bounds.Width / 2, bounds.Height / 2), window)!.Value;
                    window.MouseDown(center, MouseButton.Left);
                    window.MouseUp(center, MouseButton.Left);
                    window.KeyTextInput("Personal reminder");
                    Assert.True(input.IsFocused);
                }
                foreach (var x in new[] { 1d, bounds.Width / 2, bounds.Width - 1 })
                {
                    window.MouseMove(new Point(5, 5));
                    var point = input.TranslatePoint(new Point(x, bounds.Height / 2), window)!.Value;
                    window.MouseMove(point);
                    for (var frame = 0; frame < 8; frame++)
                    {
                        await Task.Delay(20);
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        Assert.True(input.IsPointerOver);
                        Assert.Equal(background, Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
                        Assert.Equal(bounds, input.Bounds);
                        Assert.Equal(borderThickness, surface.BorderThickness);
                    }
                }
                if (focused == 1)
                {
                    Assert.True(input.IsFocused);
                    Assert.Equal("Personal reminder", input.Text);
                }
            }
            window.CaptureRenderedFrame()!.Save($"/tmp/daisy-password-hint-{dark}-{width}.png");
        }
        finally { window.Close(); }
    }
}
