using System.IO;
using Xunit;

namespace DaisyOS.Tests;

public sealed class NativeSystemAppLifecycleContractTests
{
    [Fact]
    public void NativeSession_DoesNotReuseClosedWaylandToplevels()
    {
        var root = FindRepoRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "App.axaml.cs"));
        var cache = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Services", "Windows", "LightweightWindowCache.cs"));

        Assert.Contains("new(retainWindows: !IsNativeShellSession)", app);
        Assert.Contains("if (!_retainWindows", cache);
        Assert.Contains("window.Hide()", cache);
    }

    [Fact]
    public void AppStoreIsRecognizedByBothCompositorDiscoveryPathsAndNativeTaskbar()
    {
        var root = FindRepoRoot();
        var persistent = File.ReadAllText(Path.Combine(root, "packaging", "kwin", "daisyos-window-bridge", "contents", "code", "main.js"));
        var fallback = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Services", "Compositor", "KWinCommandDispatcher.cs"));
        Assert.Contains("\"org.daisyos.appstore\": \"appstore\"", persistent);
        Assert.Contains("'org.daisyos.appstore': 'appstore'", fallback);
        Assert.Contains("\"sovelluskauppa\": \"appstore\"", persistent);
        Assert.Contains("sovelluskauppa: 'appstore'", fallback);
        var app = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "App.axaml.cs"));
        var nativeIds = app.Substring(app.IndexOf("private static readonly string[] NativeAppIds"));
        Assert.Contains("\"appstore\"", nativeIds.Substring(0, nativeIds.IndexOf(';')));
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
