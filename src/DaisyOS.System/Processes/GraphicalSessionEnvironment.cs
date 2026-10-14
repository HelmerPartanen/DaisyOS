using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace DaisyOS.System.Processes;

/// <summary>Finds the XWayland server belonging to this Wayland compositor, including lazy startup.</summary>
internal static class GraphicalSessionEnvironment
{
    internal sealed record XDisplay(string Display, string? Authority);

    internal static XDisplay? ResolveXWayland(string waylandDisplay, string procRoot = "/proc")
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(procRoot))
            {
                if (!int.TryParse(Path.GetFileName(directory), out _)) continue;
                string[] args;
                try { args = File.ReadAllText(Path.Combine(directory, "cmdline")).Split('\0', StringSplitOptions.RemoveEmptyEntries); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                if (args.Length == 0 || Path.GetFileName(args[0]) != "kwin_wayland") continue;
                var socketIndex = Array.IndexOf(args, "--socket");
                if (socketIndex < 0 || socketIndex + 1 >= args.Length || args[socketIndex + 1] != waylandDisplay) continue;

                var configuredDisplay = Array.IndexOf(args, "--xwayland-display");
                var configuredAuthority = Array.IndexOf(args, "--xwayland-xauthority");
                if (configuredDisplay >= 0 && configuredDisplay + 1 < args.Length)
                    return new XDisplay(args[configuredDisplay + 1],
                        configuredAuthority >= 0 && configuredAuthority + 1 < args.Length ? args[configuredAuthority + 1] : null);

                // A running server gives us both the display and its actual auth file.
                foreach (var child in Directory.EnumerateDirectories(procRoot))
                {
                    try
                    {
                        var status = File.ReadAllLines(Path.Combine(child, "status"));
                        if (!status.Any(line => line.StartsWith("PPid:", StringComparison.Ordinal) && line[5..].Trim() == Path.GetFileName(directory))) continue;
                        var command = File.ReadAllText(Path.Combine(child, "cmdline")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                        if (command.Length < 2 || Path.GetFileName(command[0]) != "Xwayland") continue;
                        var auth = Array.IndexOf(command, "-auth");
                        return new XDisplay(command[1], auth >= 0 && auth + 1 < command.Length ? command[auth + 1] : null);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                // KWin owns the listening socket before it starts XWayland on demand.
                try
                {
                    var sockets = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var fd in Directory.EnumerateFiles(Path.Combine(directory, "fd")))
                    {
                        var link = new FileInfo(fd).LinkTarget;
                        if (link is not null && link.StartsWith("socket:[", StringComparison.Ordinal)) sockets.Add(link[8..^1]);
                    }
                    foreach (var line in File.ReadLines(Path.Combine(directory, "net", "unix")))
                    {
                        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (fields.Length < 8 || !sockets.Contains(fields[6])) continue;
                        const string prefix = "/tmp/.X11-unix/X";
                        if (fields[7].StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(fields[7][prefix.Length..], out var number))
                            return new XDisplay($":{number}", null);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }

                // KWin can disable /proc fd access after acquiring DRM privileges.
                // SO_PEERCRED still identifies the owner of its lazy X11 listener.
                if (procRoot == "/proc" && ResolveListenerDisplay(int.Parse(Path.GetFileName(directory))) is { } listener)
                    return listener;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    internal static XDisplay? ResolveListenerDisplay(int compositorPid, string socketDirectory = "/tmp/.X11-unix")
    {
        if (!Directory.Exists(socketDirectory)) return null;
        foreach (var path in Directory.EnumerateFiles(socketDirectory, "X*"))
        {
            if (!int.TryParse(Path.GetFileName(path).AsSpan(1), out var number)) continue;
            try
            {
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(path));
                // Linux SOL_SOCKET / SO_PEERCRED returns pid, uid, gid as native ints.
                var credentialSize = 12u;
                if (GetSocketOption(socket.Handle.ToInt32(), 1, 17, out var credentials, ref credentialSize) == 0
                    && credentialSize == 12 && credentials.Pid == compositorPid)
                    return new XDisplay($":{number}", null);
            }
            catch (SocketException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct PeerCredentials
    {
        public int Pid;
        public uint Uid;
        public uint Gid;
    }

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int GetSocketOption(int socket, int level, int option,
        out PeerCredentials credentials, ref uint length);

}
