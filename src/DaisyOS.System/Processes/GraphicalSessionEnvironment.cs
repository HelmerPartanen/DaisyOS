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
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }
}
