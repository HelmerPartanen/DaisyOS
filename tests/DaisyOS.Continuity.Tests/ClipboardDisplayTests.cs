using DaisyOS.Continuity;

namespace DaisyOS.Continuity.Tests;

public sealed class ClipboardDisplayTests
{
    [Fact]
    public void ClipboardRecoversFromAnOldServiceDisplayAfterCompositorRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-clipboard-display-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "daisyos-native-development"));
            File.WriteAllText(Path.Combine(root, "current-display"), "");
            File.WriteAllText(Path.Combine(root, "daisyos-native-development", "wayland-display"), "current-display\n");
            Assert.Equal("current-display", ClipboardBridge.ResolveWaylandDisplay("old-display", root));
            Assert.Equal("current-display", ClipboardBridge.ResolveWaylandDisplay(null, root));
            File.WriteAllText(Path.Combine(root, "valid-display"), "");
            Assert.Equal("valid-display", ClipboardBridge.ResolveWaylandDisplay("valid-display", root));
            File.Delete(Path.Combine(root, "current-display"));
            Assert.Equal("old-display", ClipboardBridge.ResolveWaylandDisplay("old-display", root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
