using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using DaisyOS.Shell.Services.Theming;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class WindowMenuThemeTests
{
    [AvaloniaTheory]
    [InlineData(false, 100)]
    [InlineData(true, 100)]
    [InlineData(false, 150)]
    [InlineData(true, 150)]
    public void NativeMenusReceiveCurrentThemeAndAccessibleTypography(bool dark, int scale)
    {
        var app = Application.Current!;
        var previousTheme = app.RequestedThemeVariant;
        try
        {
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            TypographyPolicy.Apply(app.Resources, TypographyPolicy.DefaultFamilyName, scale);
            var build = typeof(WindowDecorationThemeService).GetMethod("BuildConfig", BindingFlags.Static | BindingFlags.NonPublic)!;
            var config = (string)build.Invoke(null, [app])!;
            var menu = config.Split("[Menus]\n")[1].Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts[1]);
            Assert.Equal(TypographyPolicy.DefaultFamilyName, menu["FontFamily"]);
            Assert.Equal(app.Resources["TypeSizeBodySmall"]!.ToString(), menu["FontPixels"]);
            Assert.Equal(app.Resources["HitTargetCompact"]!.ToString(), menu["ItemHeight"]);
            Assert.Equal("14", menu["CornerRadius"]);
            Assert.Equal("8", menu["ItemCornerRadius"]);
            Assert.StartsWith("#FF", menu["Surface"]);
            Assert.NotEqual(menu["Surface"], menu["ContentPrimary"]);
            File.WriteAllText($"/tmp/daisy-native-menu-{(dark ? "dark" : "light")}-{scale}.ini", config);
        }
        finally
        {
            app.RequestedThemeVariant = previousTheme;
            TypographyPolicy.Apply(app.Resources, TypographyPolicy.DefaultFamilyName, 100);
        }
    }
}
