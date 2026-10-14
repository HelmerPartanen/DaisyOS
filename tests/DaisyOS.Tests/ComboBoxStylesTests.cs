using System.IO;
using Xunit;

namespace DaisyOS.Tests;

public sealed class ComboBoxStylesTests
{
    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "DaisyOS.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return Directory.GetCurrentDirectory();
    }

    [Fact]
    public void DropdownMenuStyles_ComboBoxPopup_ContainsThreeTierGlassHierarchy()
    {
        var repoRoot = FindRepoRoot();
        var xamlPath = Path.Combine(repoRoot, "src", "DaisyOS.Shell", "Styles", "DropdownMenuStyles.axaml");
        Assert.True(File.Exists(xamlPath), $"File not found: {xamlPath}");

        var content = File.ReadAllText(xamlPath);

        // Verify ComboBox selector exists
        Assert.Contains(@"<Style Selector=""ComboBox"">", content);

        // Verify 3-tier container border hierarchy inside ComboBox
        Assert.Contains(@"x:Name=""MenuBlurHost""", content);
        Assert.Contains(@"Background=""{DynamicResource ShellFrameOuterEdgeBrush}""", content);
        Assert.Contains(@"CornerRadius=""{DynamicResource RoundedLG}""", content);
        Assert.Contains(@"BoxShadow=""{DynamicResource ElevationFlyoutShadow}""", content);

        Assert.Contains(@"Background=""{DynamicResource ShellFrameInnerHighlightBrush}""", content);
        Assert.Contains(@"CornerRadius=""{DynamicResource RoundedLGInset}""", content);

        Assert.Contains(@"Background=""{DynamicResource ContextMenuBackgroundBrush}""", content);
        Assert.Contains(@"CornerRadius=""{DynamicResource RoundedMD}""", content);
        Assert.Contains(@"Padding=""4""", content);

        // Native popup surfaces stay out of the Avalonia overlay so KWin can blur and
        // position the full three-tier flyout without an extra in-window popup layer.
        Assert.Contains(@"ShouldUseOverlayLayer=""False""", content);
        Assert.Contains(@"VerticalOffset=""-12""", content);
    }

    [Fact]
    public void DropdownMenuStyles_ComboBoxItem_MatchesMenuItemGeometry()
    {
        var repoRoot = FindRepoRoot();
        var xamlPath = Path.Combine(repoRoot, "src", "DaisyOS.Shell", "Styles", "DropdownMenuStyles.axaml");
        var content = File.ReadAllText(xamlPath);

        Assert.Contains(@"<Style Selector=""ComboBoxItem"">", content);
        Assert.Contains(@"Property=""CornerRadius"" Value=""{DynamicResource RadiusControlInner}""", content);
        Assert.Contains(@"Property=""Padding"" Value=""{DynamicResource MenuItemPadding}""", content);
        Assert.Contains(@"Property=""MinHeight"" Value=""{DynamicResource HitTargetCompact}""", content);
        Assert.Contains(@"Property=""Margin"" Value=""0,1""", content);
        Assert.Contains(@"Property=""FontSize"" Value=""{DynamicResource TypeSizeBodySmall}""", content);
    }

    [Fact]
    public void SettingsView_DoesNotOverridePopupBorderToFlatCard()
    {
        var repoRoot = FindRepoRoot();
        var xamlPath = Path.Combine(repoRoot, "src", "DaisyOS.Shell", "Apps", "System", "Settings", "SettingsView.axaml");
        var content = File.ReadAllText(xamlPath);

        // Ensure flat single-border override is removed so it inherits 3-tier glass hierarchy
        Assert.DoesNotContain(@"<Style Selector=""ComboBox.ThemeModePicker /template/ Border#PopupBorder"">", content);

        // Ensure KeyboardLayoutOption stretches rather than overflowing fixed 280px popup width
        Assert.Contains(@"<Style Selector=""ComboBoxItem.KeyboardLayoutOption"">", content);
        Assert.Contains(@"Property=""HorizontalAlignment"" Value=""Stretch""", content);
    }
}
