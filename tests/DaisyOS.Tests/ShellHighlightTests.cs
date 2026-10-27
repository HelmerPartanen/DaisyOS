using System.Xml.Linq;
using Xunit;

namespace DaisyOS.Tests;

public sealed class ShellHighlightTests
{
    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DaisyOS.sln")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "src/DaisyOS.Shell", relative);
    }

    [Fact]
    public void BothThemeHighlightsUseTheirSemanticThemeColor()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var theme = XDocument.Load(Source("Themes/DaisyTheme.axaml"));
        var brushes = theme.Descendants().Where(element => (string?)element.Attribute(x + "Key") == "ShellSurfaceInnerBorderBrush").ToArray();
        Assert.Equal(2, brushes.Length);
        Assert.All(brushes, brush =>
        {
            Assert.Equal("{StaticResource ShellFrameInnerHighlightColor}", (string?)brush.Attribute("Color"));
            var color = brush.Parent!.Elements().Single(element => (string?)element.Attribute(x + "Key") == "ShellFrameInnerHighlightColor");
            // Light and dark materials use different highlight strengths.
            // The frame and surface must still resolve through the same token.
            var frame = brush.Parent!.Elements().Single(element => (string?)element.Attribute(x + "Key") == "ShellFrameInnerHighlightBrush");
            Assert.Equal("{StaticResource ShellFrameInnerHighlightColor}", (string?)frame.Attribute("Color"));
            var parsed = Avalonia.Media.Color.Parse(color.Value);
            Assert.InRange(parsed.A, (byte)1, (byte)254);
            Assert.Equal((byte)255, parsed.R);
            Assert.Equal((byte)255, parsed.G);
            Assert.Equal((byte)255, parsed.B);
        });
    }

    [Theory]
    [InlineData("Themes/WidgetContainerTheme.axaml")]
    [InlineData("Views/Components/Taskbar/TaskbarView.axaml")]
    [InlineData("Views/Components/SystemBar/SystemBarView.axaml")]
    [InlineData("Views/Components/Launcher/LauncherView.axaml")]
    [InlineData("Views/Components/Widgets/MediaWidget.axaml")]
    public void HighlightFramesDoNotStackASecondWhiteBackground(string relative)
    {
        var frames = XDocument.Load(Source(relative)).Descendants()
            .Where(element => (string?)element.Attribute("BorderBrush") == "{DynamicResource ShellSurfaceInnerBorderBrush}").ToArray();
        Assert.NotEmpty(frames);
        Assert.All(frames, frame =>
        {
            Assert.Equal("Transparent", (string?)frame.Attribute("Background"));
            Assert.Equal("1", (string?)frame.Attribute("BorderThickness"));
        });
    }

    [Fact]
    public void ActiveQuickSettingsUseWhiteSurfacesWithAccentIcons()
    {
        var theme = File.ReadAllText(Source("Themes/DaisyTheme.axaml"));
        var app = File.ReadAllText(Source("App.axaml"));
        var tile = File.ReadAllText(Source("Controls/QuickSettingTile.axaml"));

        Assert.Equal(2, theme.Split("<Color x:Key=\"QuickSettingActiveSurfaceColor\">#FFFFFFFF</Color>").Length - 1);
        Assert.Contains("Background\" Value=\"{DynamicResource QuickSettingActiveSurfaceBrush}", app, StringComparison.Ordinal);
        Assert.Contains("Foreground\" Value=\"{DynamicResource ContentSecondaryBrush}", app, StringComparison.Ordinal);
        Assert.Contains("Background\" Value=\"{DynamicResource StrokeDefaultBrush}", tile, StringComparison.Ordinal);
        Assert.Contains("Border.QuickSettingSplitDivider.Active", tile, StringComparison.Ordinal);
        Assert.Contains("Background\" Value=\"{DynamicResource QuickSettingActiveDividerBrush}", tile, StringComparison.Ordinal);
        Assert.Contains("Foreground\" Value=\"{DynamicResource ActionPrimaryBrush}", tile, StringComparison.Ordinal);
    }
}
