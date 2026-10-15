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

    public bool IsGameRunning()
    {
        IEnumerable<string> processes;
        try { processes = Directory.EnumerateDirectories(_procRoot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }

        foreach (var process in processes)
        {
            var pid = Path.GetFileName(process);
            if (pid.Length == 0 || !pid.All(char.IsAsciiDigit)) continue;
            string cmdline;
            try { cmdline = File.ReadAllText(Path.Combine(process, "cmdline")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if (IsGameLauncherCommand(cmdline)) return true;
        }
        return false;
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
