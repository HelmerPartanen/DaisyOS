using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DaisyOS.System.Gaming;

/// <summary>
/// Keeps Feral GameMode active while Gaming Mode is on by holding a
/// `gamemoderun sleep infinity` client. A parent-death signal also releases
/// the request if the shell crashes or is forcibly restarted.
/// </summary>
public sealed class GameModeController : IDisposable
{
    private readonly object _sync = new();
    private readonly string? _gamemoderun;
    private readonly string? _parentDeathWrapper;
    private Process? _holder;
    private bool _enabled;
    private readonly HashSet<int> _registeredGames = [];
    private readonly Func<int, int> _registerGame;
    private readonly Func<int, int> _unregisterGame;

    public GameModeController(string? gamemoderunPath = null, Func<int, int>? registerGame = null, Func<int, int>? unregisterGame = null)
    {
        _gamemoderun = gamemoderunPath ?? FindOnPath("gamemoderun");
        _parentDeathWrapper = FindOnPath("setpriv");
        _registerGame = registerGame ?? Native.Register;
        _unregisterGame = unregisterGame ?? Native.Unregister;
    }

    /// <summary>Whether the GameMode launcher is installed.</summary>
    public bool IsAvailable => _gamemoderun is not null;

    public bool IsActive
    {
        get { lock (_sync) return IsHolderRunning(); }
    }

    public void SetActive(bool active)
    {
        lock (_sync)
        {
            _enabled = active;
            if (active) Start();
            else
            {
                UpdateGameProcesses([]);
                Stop();
            }
        }
    }

    // Register the actual game tree so per-process priority and I/O tuning
    // apply to games, rather than only to the system-wide holder.
    public void UpdateGameProcesses(IEnumerable<int> processIds)
    {
        lock (_sync)
        {
            var desired = _enabled ? processIds.ToHashSet() : [];
            foreach (var pid in _registeredGames.Except(desired).ToArray())
            {
                TryRequest(_unregisterGame, pid);
                _registeredGames.Remove(pid);
            }
            foreach (var pid in desired.Except(_registeredGames))
                if (TryRequest(_registerGame, pid)) _registeredGames.Add(pid);
        }
    }

    private static bool TryRequest(Func<int, int> request, int pid)
    {
        try { return request(pid) == 0; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { return false; }
    }

    private static class Native
    {
        [DllImport("libgamemode.so.0", EntryPoint = "real_gamemode_request_start_for", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Register(int pid);

        [DllImport("libgamemode.so.0", EntryPoint = "real_gamemode_request_end_for", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Unregister(int pid);
    }

    public void Dispose() => SetActive(false);

    private bool IsHolderRunning()
    {
        try { return _holder is { HasExited: false }; }
        catch (InvalidOperationException) { return false; }
    }

    private void Start()
    {
        if (_gamemoderun is null || _parentDeathWrapper is null || !File.Exists(_gamemoderun) || IsHolderRunning()) return;
        Stop();
        var start = new ProcessStartInfo(_parentDeathWrapper)
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        // setpriv execs the client with PR_SET_PDEATHSIG. The helper must
        // disappear even when SIGKILL prevents managed Dispose from running.
        start.ArgumentList.Add("--pdeathsig");
        start.ArgumentList.Add("TERM");
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(_gamemoderun);
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
