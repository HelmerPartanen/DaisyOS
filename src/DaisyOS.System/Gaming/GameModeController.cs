using System.Diagnostics;

namespace DaisyOS.System.Gaming;

/// <summary>
/// Keeps Feral GameMode active while Gaming Mode is on by holding a
/// `gamemoderun sleep infinity` client. Killing the client ends the request,
/// so GameMode never outlives the shell's Gaming Mode.
/// </summary>
public sealed class GameModeController : IDisposable
{
    private readonly object _sync = new();
    private readonly string? _gamemoderun;
    private Process? _holder;

    public GameModeController(string? gamemoderunPath = null)
    {
        _gamemoderun = gamemoderunPath ?? FindOnPath("gamemoderun");
    }

    /// <summary>False when GameMode is not installed; the controller then does nothing.</summary>
    public bool IsAvailable => _gamemoderun is not null;

    public bool IsActive
    {
        get { lock (_sync) return IsHolderRunning(); }
    }

    public void SetActive(bool active)
    {
        lock (_sync)
        {
            if (active) Start();
            else Stop();
        }
    }

    public void Dispose() => SetActive(false);

    private bool IsHolderRunning()
    {
        try { return _holder is { HasExited: false }; }
        catch (InvalidOperationException) { return false; }
    }

    private void Start()
    {
        if (_gamemoderun is null || IsHolderRunning()) return;
        Stop();
        var start = new ProcessStartInfo(_gamemoderun)
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        start.ArgumentList.Add("sleep");
        start.ArgumentList.Add("infinity");
        try { _holder = Process.Start(start); }
        catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception or InvalidOperationException) { _holder = null; }
    }

    private void Stop()
    {
        if (_holder is null) return;
        try
        {
            if (!_holder.HasExited) _holder.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception) { }
        _holder.Dispose();
        _holder = null;
    }

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin";
        foreach (var directory in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
