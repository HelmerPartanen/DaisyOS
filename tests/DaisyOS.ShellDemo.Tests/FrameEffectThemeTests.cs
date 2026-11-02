using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using DaisyOS.Shell.Services.Theming;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class FrameEffectThemeTests
{
    [AvaloniaFact]
    public void NativeFrameRadiusUsesTheWindowThemeToken()
    {
        var app = Application.Current!;
        var hadPrevious = app.Resources.TryGetValue("AppWindowCornerRadiusValue", out var previous);
        try
        {
            app.Resources["AppWindowCornerRadiusValue"] = 19d;
            var method = typeof(WindowDecorationThemeService).GetMethod("BuildFrameEffectConfig", global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.NonPublic)!;
            var values = ((IReadOnlyList<KeyValuePair<string, string>>)method.Invoke(null, [app])!).ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.Equal("19", values["Size"]);
            Assert.Equal("19", values["InactiveCornerRadius"]);
        }
        finally
        {
            if (hadPrevious) app.Resources["AppWindowCornerRadiusValue"] = previous;
            else app.Resources.Remove("AppWindowCornerRadiusValue");
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleWindowBorderMatchesDefaultStrokeInBothThemes(bool light)
    {
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        var theme = light ? ThemeVariant.Light : ThemeVariant.Dark;
        try
        {
            app.RequestedThemeVariant = theme;
            var method = typeof(WindowDecorationThemeService).GetMethod("BuildFrameEffectConfig", global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.NonPublic)!;
            var values = ((IReadOnlyList<KeyValuePair<string, string>>)method.Invoke(null, [app])!).ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.Equal("1", values["OutlineThickness"]);
            Assert.Equal("0", values["SecondOutlineThickness"]);
            Assert.Equal("0", values["InactiveSecondOutlineThickness"]);
            Assert.Equal("0", values["ActiveSecondOutlineAlpha"]);
            Assert.Equal("0", values["InactiveSecondOutlineAlpha"]);
            Assert.Equal("DaisyOS Lock Screen,DaisyOS Lock Wallpaper", values["Exclusions"]);
            Assert.Equal("true", values["IncludeNormalWindows"]);
            Assert.Equal("true", values["IncludeDialogs"]);
            Assert.Equal("false", values["DisableOutlineTile"]);
            Assert.Equal("false", values["DisableOutlineMaximize"]);
            Assert.Equal("false", values["DisableOutlineFullScreen"]);
            foreach (var (token, rgbKey, activeAlpha, inactiveRgb, inactiveAlpha) in new[]
            {
                ("StrokeDefaultColor", "OutlineColor", "ActiveOutlineAlpha", "InactiveOutlineColor", "InactiveOutlineAlpha")
            })
            {
                Assert.True(app.TryGetResource(token, theme, out var resource));
                var color = Assert.IsType<Color>(resource);
                Assert.Equal($"#{color.R:X2}{color.G:X2}{color.B:X2}", values[rgbKey]);
                Assert.Equal(color.A.ToString(global::System.Globalization.CultureInfo.InvariantCulture), values[activeAlpha]);
                Assert.Equal(values[rgbKey], values[inactiveRgb]);
                Assert.Equal(values[activeAlpha], values[inactiveAlpha]);
            }
        }
        finally { app.RequestedThemeVariant = previous; }
    }
}
