using System.Xml.Linq;
using DaisyOS.Core.Models;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class UIKitContractTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DaisyOS.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }

    [Fact]
    public void ComponentStylesExposeSemanticButtonVariants()
    {
        var path = Path.Combine(RepoRoot(), "src/DaisyOS.Shell/Styles/ComponentStyles.axaml");
        var source = File.ReadAllText(path);

        Assert.Contains("Button.Primary", source);
        Assert.Contains("Button.Subtle", source);
        Assert.Contains("Button.Ghost", source);
        Assert.Contains("Button.Danger", source);
        Assert.Contains("Button.IconButton", source);
    }

    [Fact]
    public void GhostHoverAndQuietRaisedSurfacesShareOneThemeColor()
    {
        var theme = File.ReadAllText(Path.Combine(
            RepoRoot(), "src/DaisyOS.Shell/Themes/DaisyTheme.axaml"));
        var components = File.ReadAllText(Path.Combine(
            RepoRoot(), "src/DaisyOS.Shell/Styles/ComponentStyles.axaml"));
        var app = File.ReadAllText(Path.Combine(
            RepoRoot(), "src/DaisyOS.Shell/App.axaml"));

        Assert.Equal(2, theme.Split(
            "x:Key=\"StateHoverBrush\" Color=\"{StaticResource SurfaceRaisedSubtleColor}\"",
            StringSplitOptions.None).Length - 1);
        Assert.Equal(2, theme.Split(
            "x:Key=\"ActionSecondaryBrush\" Color=\"{StaticResource SurfaceRaisedSubtleColor}\"",
            StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("x:Key=\"StateHoverColor\"", theme);
        Assert.DoesNotContain("x:Key=\"ActionSecondaryColor\"", theme);
        Assert.Contains("Button.Ghost:pointerover", components);
        Assert.Contains("Value=\"{DynamicResource StateHoverBrush}\"", components);
        Assert.Contains("Button.ShellButton:not(.Active)", app);
        Assert.Contains("Value=\"{DynamicResource StateHoverBrush}\"", app);
        Assert.Contains("x:Key=\"DesktopHoverBrush\"", theme);
    }

    [Fact]
    public void ApplicationLoadsTheSharedComponentStylesOnce()
    {
        var app = XDocument.Load(Path.Combine(RepoRoot(), "src/DaisyOS.Shell/App.axaml"));
        var includes = app.Descendants()
            .Where(element => element.Name.LocalName == "StyleInclude")
            .Select(element => (string?)element.Attribute("Source"))
            .Where(source => source?.EndsWith("/Styles/ComponentStyles.axaml", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Single(includes);
    }

    [Fact]
    public void UIKitCatalogUsesProductionComponentsAndVariants()
    {
        var path = Path.Combine(RepoRoot(), "src/DaisyOS.Shell/Apps/UIKit/UIKitWindow.axaml");
        var source = File.ReadAllText(path);

        Assert.Contains("controls:SearchBar", source);
        Assert.Contains("controls:QuickSettingTile", source);
        Assert.Contains("controls:QuickSettingsSlider", source);
        Assert.Contains("controls:ShellListItem", source);
        Assert.Contains("Classes=\"Primary\"", source);
        Assert.Contains("Classes=\"Subtle\"", source);
        Assert.Contains("Classes=\"Ghost\"", source);
    }

    [Fact]
    public void SearchBarUsesOneClippedFrostedSurfaceWithoutRectangularBackdrop()
    {
        var search = File.ReadAllText(Path.Combine(
            RepoRoot(), "src/DaisyOS.Shell/Controls/SearchBar.axaml"));
        var theme = File.ReadAllText(Path.Combine(
            RepoRoot(), "src/DaisyOS.Shell/Themes/DaisyTheme.axaml"));

        Assert.DoesNotContain("Selector=\"controls|SearchBar\"", search);
        Assert.DoesNotContain("FocusOutline", search);
        Assert.DoesNotContain("InputFocused", search);
        Assert.DoesNotContain("Margin=\"3\"", search);
        Assert.Contains("SearchBar:focus-within Border#FocusedFrame", search);
        Assert.Contains("BorderThickness=\"{DynamicResource BorderThicknessFocus}\"", search);
        Assert.Contains("DoubleTransition Property=\"Opacity\" Duration=\"{DynamicResource MotionDurationBase}\"", search);
        Assert.Contains("TextBox.SearchInput:focus-within", search);
        Assert.Contains("x:Name=\"SearchFrame\"", search);
        Assert.Contains("ClipToBounds=\"True\"", search);
        Assert.Contains("ShellFrameOuterEdgeBrush", search);
        Assert.Contains("ShellFrameInnerHighlightBrush", search);
        Assert.Equal(2, theme.Split(
            "x:Key=\"SearchFieldBackgroundBrush\" Color=\"{StaticResource SurfaceRaisedSubtleColor}\"",
            StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void LauncherIncludesUIKit()
    {
        var app = new LinuxAppLauncherService().GetAvailableApps()
            .SingleOrDefault(candidate => candidate.Id == "org.daisyos.uikit.desktop");

        Assert.NotNull(app);
        Assert.Equal("UI Kit", app.Name);
        Assert.Equal(AppLaunchKind.Internal, app.LaunchKind);
    }
}
