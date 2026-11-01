using System.IO;
using Xunit;

namespace DaisyOS.Tests;

public sealed class KWinFrameEffectEligibilityContractTests
{
    [Fact]
    public void EffectExcludesLayerSurfacesByTypeAndPreservesNormalSystemApps()
    {
        var root = FindRepoRoot();
        var installer = File.ReadAllText(Path.Combine(root, "scripts", "install-kwin-frame-effect.sh"));

        Assert.Contains("'DaisyOS Lock Screen,DaisyOS Lock Wallpaper'", installer);
        Assert.DoesNotContain("'DaisyOS.Shell,DaisyOS Lock Screen", installer);
        var effect = File.ReadAllText(Path.Combine(root, "src", "native", "kwin_frame_effect", "frameeffect.cpp"));
        Assert.Contains("KWin::LayerShellV1Window", effect);
        Assert.Contains("w->isNormalWindow()", effect);
        Assert.Contains("w->isDialog()", effect);
        Assert.Contains("w->isLockScreen()", effect);
        Assert.DoesNotContain("caption().trimmed().isEmpty()", effect);
        Assert.DoesNotContain("KDE-Rounded-Corners", installer);

    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "DaisyOS.sln"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return Directory.GetCurrentDirectory();
    }
}
