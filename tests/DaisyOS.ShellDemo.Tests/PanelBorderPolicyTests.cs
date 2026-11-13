using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using DaisyOS.Shell.Services.Theming;
using Xunit;
namespace DaisyOS.ShellDemo.Tests;
public sealed class PanelBorderPolicyTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task ExistingWidgetFramesLoseTheirEdgesWhenPreferenceChanges()
    {
        var widget = new DaisyOS.Shell.Controls.WidgetContainer { Width = 360, Height = 200, Background = Brushes.Red };
        var resources = new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://DaisyOS.Shell/"))
        { Source = new Uri("avares://DaisyOS.Shell/Themes/WidgetContainerTheme.axaml") };
        widget.Theme = (ControlTheme)resources.Loaded[typeof(DaisyOS.Shell.Controls.WidgetContainer)]!;
        var window = new Window { Content = widget };
        window.Show();
        try
        {
            window.UpdateLayout();
            PanelBorderPolicy.Apply(Avalonia.Application.Current!.Resources, true);
            await Task.Delay(250);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var frame = widget.GetVisualDescendants().OfType<Border>().First(b => b.Name == "OuterFrame");
            Assert.Equal((byte)0, Assert.IsAssignableFrom<ISolidColorBrush>(frame.BorderBrush).Color.A);
            Assert.Equal(new Avalonia.Thickness(0), frame.BorderThickness);
            var surface = widget.GetVisualDescendants().OfType<Border>().First(b => b.Name == "InnerSurface");
            Assert.Equal(new Avalonia.Thickness(0), surface.Margin);
            Assert.Equal(frame.CornerRadius, surface.CornerRadius);
            window.UpdateLayout();
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(360, 200));
            bitmap.Render(widget);
            bitmap.Save("/tmp/daisy-borderless-widget.png");
            using var encoded = new MemoryStream();
            bitmap.Save(encoded);
            using var pixels = SkiaSharp.SKBitmap.Decode(encoded.ToArray());
            Assert.Equal(SkiaSharp.SKColors.Red, pixels.GetPixel(180, 5));
            PanelBorderPolicy.Apply(Avalonia.Application.Current.Resources, false);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Avalonia.Thickness(1), frame.BorderThickness);
            Assert.Equal(new Avalonia.Thickness(1), surface.Margin);
        }
        finally
        {
            PanelBorderPolicy.Apply(Avalonia.Application.Current!.Resources, false);
            window.Close();
        }
    }

    [Fact]
    public void DisablingBothFrameEdgesPreservesFocusAndRestoresTheCurrentTheme()
    {
        var light = new ResourceDictionary(); var dark = new ResourceDictionary();
        foreach (var key in PanelBorderPolicy.BrushKeys) { light[key] = Brushes.Black; dark[key] = Brushes.White; }
        var defaults = new ResourceDictionary();
        defaults.ThemeDictionaries[ThemeVariant.Light] = light; defaults.ThemeDictionaries[ThemeVariant.Dark] = dark;
        var resources = new ResourceDictionary(); resources.MergedDictionaries.Add(defaults);
        resources["FocusRingBrush"] = Brushes.Blue;
        PanelBorderPolicy.Apply(resources, true);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var key in PanelBorderPolicy.BrushKeys)
        {
            Assert.True(resources.TryGetResource(key, theme, out var value));
            Assert.Equal((byte)0, Assert.IsAssignableFrom<ISolidColorBrush>(value).Color.A);
        }
        Assert.Same(Brushes.Blue, resources["FocusRingBrush"]);
        PanelBorderPolicy.Apply(resources, false);
        foreach (var key in PanelBorderPolicy.BrushKeys)
        {
            Assert.True(resources.TryGetResource(key, ThemeVariant.Light, out var a)); Assert.Same(Brushes.Black, a);
            Assert.True(resources.TryGetResource(key, ThemeVariant.Dark, out var b)); Assert.Same(Brushes.White, b);
        }
    }
}
