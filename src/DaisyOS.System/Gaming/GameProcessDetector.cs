namespace DaisyOS.System.Gaming;

/// <summary>
/// Detects running games by inspecting process command lines under /proc.
/// Uses launcher signals rather than fullscreen heuristics so video players and
/// presentations never trigger Gaming Mode.
/// </summary>
public sealed class GameProcessDetector
{
    private readonly string _procRoot;

    public GameProcessDetector(string procRoot = "/proc")
    {
        _procRoot = procRoot;
    }

    public bool IsGameRunning() => FindGameProcessIds().Count > 0;

    public IReadOnlySet<int> FindGameProcessIds()
    {
        var result = new HashSet<int>();
        IEnumerable<string> processes;
        try { processes = Directory.EnumerateDirectories(_procRoot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return result; }

        foreach (var process in processes)
        {
            var pid = Path.GetFileName(process);
            if (pid.Length == 0 || !pid.All(char.IsAsciiDigit)) continue;
            string cmdline;
            try { cmdline = File.ReadAllText(Path.Combine(process, "cmdline")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if (IsGameLauncherCommand(cmdline) && int.TryParse(pid, out var processId))
                AddProcessTree(processId, result);
        }
        return result;
    }

    private void AddProcessTree(int pid, HashSet<int> result)
    {
        if (!result.Add(pid)) return;
        try
        {
            var children = File.ReadAllText(Path.Combine(_procRoot, pid.ToString(), "task", pid.ToString(), "children"));
            foreach (var child in children.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(child, out var childPid)) AddProcessTree(childPid, result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Recognises a NUL-separated /proc cmdline that hosts a running game.</summary>
    public static bool IsGameLauncherCommand(string cmdline)
    {
        var args = cmdline.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0) return false;

        // Steam starts every game, native or Proton, as `reaper SteamLaunch AppId=<id> ...`.
        if (Path.GetFileName(args[0]) == "reaper" && args.Contains("SteamLaunch"))
            return args.Any(arg => arg.StartsWith("AppId=", StringComparison.Ordinal) && arg != "AppId=0");

        // Lutris wraps each running game; it is a Python script, so check the interpreter's first argument too.
        return args.Take(2).Any(arg => Path.GetFileName(arg) == "lutris-wrapper");
    }
}
