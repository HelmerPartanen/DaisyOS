using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using DaisyOS.Shell.Services.Theming;
using Xunit;

namespace DaisyOS.Tests;

public sealed class WindowDecorationThemeServiceTests
{
    [Fact]
    public void DisabledPanelBordersExportTransparentEdgesToNativeWindows()
    {
        var application = new Application();
        PanelBorderPolicy.Apply(application.Resources, true);
        var config = WindowDecorationThemeService.BuildConfig(application);
        Assert.Contains("ShellFrameOuterEdge=#00000000", config);
        Assert.Contains("ShellFrameInnerHighlight=#00000000", config);
        var frame = WindowDecorationThemeService.BuildFrameEffectConfig(application).ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal("0", frame["ActiveOutlineAlpha"]);
        Assert.Equal("0", frame["InactiveOutlineAlpha"]);
        Assert.Contains("WindowBorder=#00000000", config);
    }

    [Fact]
    public void OverviewPaletteFollowsThemeChanges()
    {
        var application = new Application();
        application.Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
        {
            ["SurfaceBaseSolidBrush"] = new SolidColorBrush(Colors.White),
            ["ContentPrimaryBrush"] = new SolidColorBrush(Colors.Black)
        };
        application.Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
        {
            ["SurfaceBaseSolidBrush"] = new SolidColorBrush(Colors.Black),
            ["ContentPrimaryBrush"] = new SolidColorBrush(Colors.White)
        };

        application.RequestedThemeVariant = ThemeVariant.Light;
        var light = WindowDecorationThemeService.BuildOverviewConfig(application).ToDictionary(pair => pair.Key, pair => pair.Value);
        application.RequestedThemeVariant = ThemeVariant.Dark;
        var dark = WindowDecorationThemeService.BuildOverviewConfig(application).ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Equal("#FFFFFFFF", light["SurfaceBase"]);
        Assert.Equal("#FF000000", light["ContentPrimary"]);
        Assert.Equal("#FF000000", dark["SurfaceBase"]);
        Assert.Equal("#FFFFFFFF", dark["ContentPrimary"]);
    }

    [Fact]
    public void TranslucentShellMaterialsNeverMakeNativeDecorationsTranslucent()
    {
        var application = new Application();
        application.Resources["SurfaceBaseBrush"] = new SolidColorBrush(Color.Parse("#66282828"));
        application.Resources["SurfaceRaisedBrush"] = new SolidColorBrush(Color.Parse("#801A1A1D"));
        var config = WindowDecorationThemeService.BuildConfig(application);
        Assert.Contains("SurfaceBase=#FF282828", config);
        Assert.Contains("SurfaceRaised=#FF1A1A1D", config);
        Assert.Contains("ShellFrameInnerHighlight=#26FFFFFF", config);
    }

    [Fact]
    public void DecorationsPreferDedicatedSolidSurfaceColor()
    {
        var application = new Application();
        application.Resources["SurfaceBaseBrush"] = new SolidColorBrush(Color.Parse("#66121214"));
        application.Resources["SurfaceBaseSolidBrush"] = new SolidColorBrush(Color.Parse("#FF282828"));
        Assert.Contains("SurfaceBase=#FF282828", WindowDecorationThemeService.BuildConfig(application));
    }

    [Fact]
    public void BuildConfigExportsSemanticFrameRolesAndMetrics()
    {
        var application = new Application();
        application.Resources["SurfaceBaseBrush"] = new SolidColorBrush(Color.Parse("#FF121214"));
        application.Resources["SurfaceRaisedBrush"] = new SolidColorBrush(Color.Parse("#FF1A1A1D"));
        application.Resources["ShellFrameOuterEdgeBrush"] = new SolidColorBrush(Color.Parse("#99000000"));
        application.Resources["ShellFrameInnerHighlightBrush"] = new SolidColorBrush(Color.Parse("#26FFFFFF"));
        application.Resources["ContentPrimaryBrush"] = new SolidColorBrush(Color.Parse("#FFF5F5F7"));
        application.Resources["ContentDisabledBrush"] = new SolidColorBrush(Color.Parse("#FF98989F"));
        application.Resources["StateHoverBrush"] = new SolidColorBrush(Color.Parse("#14FFFFFF"));
        application.Resources["StatePressedBrush"] = new SolidColorBrush(Color.Parse("#24FFFFFF"));
        application.Resources["ActionDangerHoverBrush"] = new SolidColorBrush(Color.Parse("#FFFF383C"));
        application.Resources["ContentOnActionBrush"] = new SolidColorBrush(Color.Parse("#FFFFFFFF"));
        application.Resources["KWinDecorationTitleBarHeight"] = 32d;
        application.Resources["KWinDecorationCaptionButtonWidth"] = 46d;
        application.Resources["KWinDecorationCaptionButtonHeight"] = 32d;
        application.Resources["KWinDecorationCaptionGlyphSize"] = 10d;
        application.Resources["KWinDecorationTitleFontPixels"] = 13d;
        application.Resources["RoundedLGValue"] = 12d;
        application.Resources["AppWindowCornerRadiusValue"] = 19d;
        application.Resources["ActionPrimaryBrush"] = new SolidColorBrush(Color.Parse("#FF764AC9"));

        var config = WindowDecorationThemeService.BuildConfig(application);

        Assert.StartsWith("[Decoration]", config);
        Assert.Contains("SurfaceBase=#FF121214", config);
        Assert.Contains("ShellFrameOuterEdge=#99000000", config);
        Assert.Contains("ShellFrameInnerHighlight=#26FFFFFF", config);
        Assert.Contains("StatusDanger=#FFFF383C", config);
        Assert.Contains("TitleBarHeight=32", config);
        Assert.Contains("CaptionButtonWidth=46", config);
        Assert.Contains("CaptionButtonHeight=32", config);
        Assert.Contains("CaptionGlyphSize=10", config);
        Assert.Contains("TitleFontPixels=13", config);
        Assert.Contains("CornerRadius=19", config);
        Assert.Contains("ActionPrimary=#FF764AC9", config);
        Assert.Contains("ReduceMotion=false", config);
    }

    [Fact]
    public void BuildOverviewConfigExportsDaisySemanticTokens()
    {
        var application = new Application();
        application.Resources["SurfaceBaseBrush"] = new SolidColorBrush(Color.Parse("#66282828"));
        application.Resources["SurfaceRaisedBrush"] = new SolidColorBrush(Color.Parse("#CC303034"));
        application.Resources["ContentPrimaryBrush"] = new SolidColorBrush(Color.Parse("#FFF5F5F5"));
        application.Resources["ContentSecondaryBrush"] = new SolidColorBrush(Color.Parse("#FFB3B3BA"));
        application.Resources["ContentDisabledBrush"] = new SolidColorBrush(Color.Parse("#FF98989F"));
        application.Resources["ContentOnActionBrush"] = new SolidColorBrush(Color.Parse("#FFFFFFFF"));
        application.Resources["ActionDangerHoverBrush"] = new SolidColorBrush(Color.Parse("#FFFF383C"));
        application.Resources["ShellFrameOuterEdgeBrush"] = new SolidColorBrush(Color.Parse("#99000000"));
        application.Resources["ShellFrameInnerHighlightBrush"] = new SolidColorBrush(Color.Parse("#26FFFFFF"));
        application.Resources["RoundedLGValue"] = 12d;
        application.Resources["AppWindowCornerRadiusValue"] = 19d;

        var config = WindowDecorationThemeService.BuildOverviewConfig(application)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Equal("#FF282828", config["SurfaceBase"]);
        Assert.Equal("#FF303034", config["SurfaceRaised"]);
        Assert.Equal("#FF98989F", config["ContentDisabled"]);
        Assert.Equal("#FFFFFFFF", config["ContentOnAction"]);
        Assert.Equal("#FFFF383C", config["StatusDanger"]);
        Assert.Equal("19", config["CornerRadius"]);
        Assert.Equal("1", config["MaxPreviewScale"]);
        Assert.Equal("32", config["TitleBarHeight"]);
        Assert.Equal("46", config["CaptionButtonWidth"]);
        Assert.Equal("10", config["CaptionGlyphSize"]);
        Assert.Equal("13", config["TitleFontPixels"]);
        Assert.Equal("1.012", config["HoverScale"]);
        Assert.Equal("280", config["AnimationDuration"]);
    }
}
