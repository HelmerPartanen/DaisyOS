using Xunit;

namespace DaisyOS.Tests;

public sealed class CursorAndQuickSettingsContractTests
{
    [Fact]
    public void NewUsersUseDaisyCursorAnimationWithoutStockBouncingIcon()
    {
        var root = RepoRoot();
        var cursor = File.ReadAllText(Path.Combine(root,
            "os/archiso/DaisyOS/airootfs/etc/skel/.config/kcminputrc"));
        var feedback = File.ReadAllText(Path.Combine(root,
            "os/archiso/DaisyOS/airootfs/etc/skel/.config/klaunchrc"));
        var staging = File.ReadAllText(Path.Combine(root, "scripts/copy-shell-to-iso.sh"));

        Assert.Contains("cursorTheme=DaisyOS-Dark", cursor);
        Assert.DoesNotContain("DaisyOS-Cursors-Dark", cursor);
        foreach (var setting in new[] { "BusyCursor=false", "Bouncing=false", "Blinking=false", "TaskbarButton=false" })
            Assert.Contains(setting, feedback);
        Assert.Contains("daisyos-busy-cursor-v1Enabled --type bool true", staging);
        Assert.Contains("--key Bouncing --type bool false", staging);
        Assert.Contains("--key TaskbarButton --type bool false", staging);
    }

    [Fact]
    public void QuickSettingsMainPageSizesToContentWhileConnectionPagesStayStable()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(),
            "src/DaisyOS.Shell/Views/Components/QuickSettings/QuickSettings.axaml"));

        Assert.Contains("<Grid x:Name=\"MotionContent\">", xaml);
        Assert.DoesNotContain("x:Name=\"MotionContent\"\n            Height=", xaml);
        Assert.DoesNotContain("Height=\"{DynamicResource QuickSettingsPanelContentHeight}\"", xaml);
        Assert.DoesNotContain("QuickSettingsConnectionListHeight", xaml);
        var code = File.ReadAllText(Path.Combine(RepoRoot(),
            "src/DaisyOS.Shell/Views/Components/QuickSettings/QuickSettings.axaml.cs"));
        Assert.Contains("this.FindResource(\"QuickSettingsPanelContentHeight\")", code);
    }

    private static string RepoRoot()
    {
        var directory = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "DaisyOS.sln"))) return directory;
            directory = Path.GetDirectoryName(directory);
        }
        throw new DirectoryNotFoundException("Could not find the DaisyOS repository root.");
    }
}
