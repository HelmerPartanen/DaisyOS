using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace DaisyOS.Tests;

public sealed class DesignTokenContractTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void CanonicalTokenGroupsExistInTheThemeDictionary()
    {
        var source = File.ReadAllText(ThemePath());
        var required = new[]
        {
            "Space1", "Space2", "Space3", "Space4", "Space6", "Space8",
            "TypeSizeCaption", "TypeSizeBody", "TypeSizeTitle", "TypeSizeHeading", "TypeSizeDisplay",
            "TypeLineCaption", "TypeLineBody", "TypeLineTitle", "TypeLineHeading", "TypeLineDisplay",
            "RadiusControl", "RadiusSurface", "RadiusDialog", "RadiusPill",
            "RadiusSurfaceInnerValue",
            "BorderThicknessDefault", "BorderThicknessFocus",
            "HitTargetCompact", "HitTargetStandard", "HitTargetComfortable",
            "ControlHeightCompact", "ControlHeightStandard", "ControlHeightComfortable",
            "SearchHeightStandard", "SearchHeightProminent",
            "IconSizeSmall", "IconSizeStandard", "IconSizeMedium", "IconSizeLarge", "IconSizeHero",
            "MotionDurationInstant", "MotionDurationFast", "MotionDurationBase", "MotionDurationSlow"
        };

        Assert.All(required, key => Assert.Contains($"x:Key=\"{key}\"", source));
    }

    [Fact]
    public void CustomDoubleRadiusPropertiesUseScalarTokens()
    {
        var root = RepoRoot();
        var popover = File.ReadAllText(Path.Combine(
            root, "src/DaisyOS.Shell/Controls/ToolbarSearchPopover.axaml"));

        Assert.Contains("CornerRadius=\"{DynamicResource RadiusSurfaceInnerValue}\"", popover);
        Assert.DoesNotContain("controls:CartoucheBorder Width=\"280\" Height=\"50\" Margin=\"16\"\n                                TailHeight=\"6\" TailOnTop=\"True\" CornerRadius=\"{DynamicResource RadiusSurfaceInner}\"", popover);
    }

    [Fact]
    public void SemanticForegroundContainerPairsMeetWcagAa()
    {
        var document = XDocument.Load(ThemePath());
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var dictionary = document.Descendants()
                .Single(element => (string?)element.Attribute(X + "Key") == theme);
            var colors = dictionary.Descendants()
                .Where(element => element.Name.LocalName == "Color")
                .ToDictionary(element => (string)element.Attribute(X + "Key")!, element => ParseColor(element.Value));

            AssertContrast(colors["ActionPrimaryColor"], colors["ContentOnActionColor"], theme, "primary action");
            AssertContrast(colors["StatusSuccessColor"], colors["StatusSuccessContainerColor"], theme, "success");
            AssertContrast(colors["StatusWarningColor"], colors["StatusWarningContainerColor"], theme, "warning");
            AssertContrast(colors["StatusDangerColor"], colors["StatusDangerContainerColor"], theme, "danger");
            AssertContrast(colors["StatusInfoColor"], colors["StatusInfoContainerColor"], theme, "info");
            AssertContrast(colors["ContentPrimaryColor"], colors["SurfaceRaisedColor"], theme, "primary content");
        }
    }

    [Fact]
    public void SharedComponentsConsumeSizingTypographyAndMotionTokens()
    {
        var root = RepoRoot();
        var components = File.ReadAllText(Path.Combine(root, "src/DaisyOS.Shell/Styles/ComponentStyles.axaml"));
        var search = File.ReadAllText(Path.Combine(root, "src/DaisyOS.Shell/Controls/SearchBar.axaml"));

        Assert.Contains("ControlHeightStandard", components);
        Assert.Contains("HitTargetStandard", components);
        Assert.Contains("TypeSizeBody", components);
        Assert.Contains("RadiusControl", components);
        Assert.Contains("MotionDurationFast", components);
        Assert.Contains(":focus-visible", components);
        Assert.Contains("SearchHeightStandard", search);
        Assert.Contains("MotionDurationBase", search);
    }

    [Fact]
    public void SharedComponentStateMatrixIsCentralized()
    {
        var root = RepoRoot();
        var components = File.ReadAllText(Path.Combine(root, "src/DaisyOS.Shell/Styles/ComponentStyles.axaml"));
        var menus = File.ReadAllText(Path.Combine(root, "src/DaisyOS.Shell/Styles/DropdownMenuStyles.axaml"));

        foreach (var state in new[] { ":pointerover", ":pressed", ":focus-visible", ":disabled", ".Loading", ".Error", ".Selected" })
            Assert.Contains(state, components);

        foreach (var family in new[] { "Button.Primary", "Button.Secondary", "Button.Ghost", "Button.Danger", "TextBox", "ListBoxItem", "TabItem", "ToggleSwitch", "ToolTip", "ProgressBar", "EmptyState", "ErrorState" })
            Assert.Contains(family, components);

        Assert.Contains("ComboBox:focus-visible", menus);
        Assert.Contains("ComboBoxItem:focus-visible", menus);
    }

    [Fact]
    public void RenderedViewsUseTypographyAndDaisyIconSizeTokens()
    {
        var shell = Path.Combine(RepoRoot(), "src/DaisyOS.Shell");
        var files = Directory.EnumerateFiles(shell, "*.axaml", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("DaisyTheme.axaml", StringComparison.Ordinal));

        foreach (var path in files)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotMatch("FontSize=\"[0-9]", source);

            var iconTags = Regex.Matches(
                source,
                "<(?:controls:)?DaisyIcon\\b[^>]*>",
                RegexOptions.Singleline);
            Assert.All(iconTags.Cast<Match>(), tag =>
                Assert.DoesNotMatch("Size=\"[0-9]", tag.Value));
        }
    }

    private static void AssertContrast(Rgb foreground, Rgb background, string theme, string role) =>
        Assert.True(Contrast(foreground, background) >= 4.5,
            $"{theme} {role} contrast was {Contrast(foreground, background):F2}:1; expected at least 4.5:1.");

    private static double Contrast(Rgb first, Rgb second)
    {
        var light = Math.Max(Luminance(first), Luminance(second));
        var dark = Math.Min(Luminance(first), Luminance(second));
        return (light + 0.05) / (dark + 0.05);
    }

    private static double Luminance(Rgb color) =>
        (0.2126 * Linearize(color.R)) + (0.7152 * Linearize(color.G)) + (0.0722 * Linearize(color.B));

    private static double Linearize(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static Rgb ParseColor(string value)
    {
        var hex = value.Trim().TrimStart('#');
        if (hex.Length == 8) hex = hex[2..];
        Assert.Equal(6, hex.Length);
        return new Rgb(
            byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static string ThemePath() => Path.Combine(RepoRoot(), "src/DaisyOS.Shell/Themes/DaisyTheme.axaml");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DaisyOS.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }

    private readonly record struct Rgb(byte R, byte G, byte B);
}
