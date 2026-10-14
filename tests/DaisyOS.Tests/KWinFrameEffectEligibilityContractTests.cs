using System.IO;
using Xunit;

namespace DaisyOS.Tests;

public sealed class KWinFrameEffectEligibilityContractTests
{
    [Fact]
    public void EffectSeparatesUntitledShellSurfacesFromTitledSystemApps()
    {
        var root = FindRepoRoot();
        var installer = File.ReadAllText(Path.Combine(root, "scripts", "install-kwin-frame-effect.sh"));

        Assert.Contains("'DaisyOS Lock Screen,DaisyOS Lock Wallpaper'", installer);
        Assert.DoesNotContain("'DaisyOS.Shell,DaisyOS Lock Screen", installer);
        Assert.Contains("daisyos-layer-surfaces.patch", installer);
        var patch = File.ReadAllText(Path.Combine(root, "src", "native", "kwin_frame_effect", "daisyos-layer-surfaces.patch"));
        Assert.Contains("kwindow->caption().trimmed().isEmpty()", patch);
        Assert.Contains("DaisyOS.Shell", patch);
        Assert.Contains("!w->caption().trimmed().isEmpty()", patch);
        Assert.DoesNotContain("caption().contains(QStringLiteral(\"Files\")", patch);
        Assert.Contains("continue;", patch);
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
