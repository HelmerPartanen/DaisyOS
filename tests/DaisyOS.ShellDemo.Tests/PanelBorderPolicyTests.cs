using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using DaisyOS.Shell.Services.Theming;
using Xunit;
namespace DaisyOS.ShellDemo.Tests;
public sealed class PanelBorderPolicyTests
{
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
