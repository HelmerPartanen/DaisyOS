using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class GraphicalSessionEnvironmentTests
{
    [Fact]
    public void ResolvesOnlyMatchingCompositorAndPreservesItsAuthority()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-display-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "10"));
            Directory.CreateDirectory(Path.Combine(root, "20"));
            File.WriteAllText(Path.Combine(root, "10", "cmdline"), "kwin_wayland\0--socket\0daisy-test\0");
            File.WriteAllText(Path.Combine(root, "20", "status"), "PPid:\t10\n");
            File.WriteAllText(Path.Combine(root, "20", "cmdline"), "/usr/bin/Xwayland\0:7\0-auth\0/run/test/auth\0");
            Assert.Equal(new GraphicalSessionEnvironment.XDisplay(":7", "/run/test/auth"), GraphicalSessionEnvironment.ResolveXWayland("daisy-test", root));
            Assert.Null(GraphicalSessionEnvironment.ResolveXWayland("other-session", root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ResolvesOwnedSocketBeforeLazyXWaylandStarts()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-display-" + Guid.NewGuid());
        try
        {
            var compositor = Path.Combine(root, "10");
            Directory.CreateDirectory(Path.Combine(compositor, "fd"));
            Directory.CreateDirectory(Path.Combine(compositor, "net"));
            File.WriteAllText(Path.Combine(compositor, "cmdline"), "kwin_wayland\0--socket\0daisy-test\0");
            File.CreateSymbolicLink(Path.Combine(compositor, "fd", "5"), "socket:[123]");
            File.WriteAllText(Path.Combine(compositor, "net", "unix"),
                "000: 000 000 000 000 01 999 /tmp/.X11-unix/X0\n000: 000 000 000 000 01 123 /tmp/.X11-unix/X3\n");
            Assert.Equal(":3", GraphicalSessionEnvironment.ResolveXWayland("daisy-test", root)?.Display);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ResolvesListenerOnlyWhenPeerCredentialsMatchCompositor()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "daisy-peer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var listener = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix,
                System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
            listener.Bind(new System.Net.Sockets.UnixDomainSocketEndPoint(Path.Combine(root, "X7")));
            listener.Listen(4);
            Assert.Null(GraphicalSessionEnvironment.ResolveListenerDisplay(-1, root));
            Assert.Equal(":7", GraphicalSessionEnvironment.ResolveListenerDisplay(Environment.ProcessId, root)?.Display);
        }
        finally { Directory.Delete(root, true); }
    }

}
