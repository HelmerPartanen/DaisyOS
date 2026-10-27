using System.IO;
using Xunit;

namespace DaisyOS.Tests;

public sealed class QuickSettingsSliderTests
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
    public void VolumeSliderThumb_UsesSemanticSurfaceBrushes()
    {
        var repoRoot = FindRepoRoot();
        var xamlPath = Path.Combine(repoRoot, "src", "DaisyOS.Shell", "Controls", "QuickSettingsSlider.axaml");
        Assert.True(File.Exists(xamlPath), $"File not found: {xamlPath}");

        var content = File.ReadAllText(xamlPath);

        // The thumb follows theme changes instead of pinning light-theme colors.
        // It is a control surface, so it keeps its own white token rather than
        // the accent-dependent content-on-action color.
        Assert.Contains(@"x:Name=""ProgressThumb""", content);
        Assert.Contains(@"Background=""{DynamicResource SliderThumbBrush}""", content);
        Assert.Contains(@"BorderBrush=""{DynamicResource StrokeDefaultBrush}""", content);
        Assert.Contains(@"BorderThickness=""1""", content);
    }

    [Fact]
    public void QuickSettings_VolumeSliderContainer_HasActionSecondaryBrushBackground()
    {
        var repoRoot = FindRepoRoot();
        var xamlPath = Path.Combine(repoRoot, "src", "DaisyOS.Shell", "Views", "Components", "QuickSettings", "QuickSettings.axaml");
        Assert.True(File.Exists(xamlPath), $"File not found: {xamlPath}");

        var content = File.ReadAllText(xamlPath);

        // Verify volume row card border background is ActionSecondaryBrush (matches inactive QuickSetting buttons)
        Assert.Contains("VOLUME ROW CARD", content);
        Assert.Contains(@"Background=""{DynamicResource ActionSecondaryBrush}""", content);
    }

    [Fact]
    public void NativeDevelopmentSession_DisablesKWinScreenLockerAutolock()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "daisyos-native-development-session");
        Assert.True(File.Exists(scriptPath), $"File not found: {scriptPath}");

        var content = File.ReadAllText(scriptPath);

        Assert.Contains("kwriteconfig6 --file kscreenlockerrc --group Daemon --key Autolock false", content);
        Assert.Contains("kwriteconfig6 --file kscreenlockerrc --group Daemon --key Timeout 0", content);
        Assert.Contains("kwriteconfig6 --file kscreenlockerrc --group Daemon --key LockOnResume false", content);
    }
}
