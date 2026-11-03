using System.Text.Json;

namespace DaisyOS.Shell.Services;

/// <summary>
/// Small, resilient persistence boundary for shell-owned preferences. Hardware state is never
/// mirrored here: a successful system operation is required before its corresponding preference
/// changes.
/// </summary>
public sealed class ShellSessionState
{
    public static readonly IReadOnlyList<string> DefaultDockOrder =
    [
        "org.daisyos.files", "org.daisyos.mail", "org.daisyos.notes", "org.daisyos.calendar",
        "org.daisyos.calculator", "org.daisyos.settings", "org.daisyos.appstore",
        "org.daisyos.taskmanager", "org.daisyos.diskmanager", "org.daisyos.uikit"
    ];

    private readonly string _path;
    private PersistedShellState _state;
    private readonly object _sync = new();

    public event EventHandler? Changed;
    public Action<string, string[]>? RemoteWrite { get; set; }
    private DateTime _readTime;

    /// <summary>Refreshes an application's read-only snapshot without persisting it.</summary>
    public void Reload()
    {
        var time = File.GetLastWriteTimeUtc(_path);
        if (time == _readTime) return;
        _readTime = time;
        lock (_sync) _state = Load();
        Changed?.Invoke(this, EventArgs.Empty);
    }


    public ShellSessionState(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetEnvironmentVariable("XDG_STATE_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"),
            "DaisyOS", "shell-state.json");
        _state = Load();
        if (_state.DockDefaultsInitialized != true)
        {
            _state.DockOrder = DefaultDockOrder.ToList();
            _state.DockDefaultsInitialized = true;
            Save(_state);
        }
    }

    public bool DoNotDisturb { get => _state.DoNotDisturb; set { if (RemoteWrite is { } remote) remote("dnd", [value.ToString()]); else Update(s => s.DoNotDisturb = value); } }
    public bool GamingMode { get => _state.GamingMode; set { if (RemoteWrite is { } remote) remote("gaming", [value.ToString()]); else Update(s => s.GamingMode = value); } }
    public bool ShowDateInSystemBar { get => _state.ShowDateInSystemBar; set { if (RemoteWrite is { } remote) remote("show-date", [value.ToString()]); else Update(s => s.ShowDateInSystemBar = value); } }
    public double? LastKnownVolume { get => _state.LastKnownVolume; set { if (RemoteWrite is { } remote) remote("volume", [value?.ToString(global::System.Globalization.CultureInfo.InvariantCulture) ?? ""]); else Update(s => s.LastKnownVolume = value is null ? null : Math.Clamp(value.Value, 0, 100)); } }
    public IReadOnlyList<string> DockOrder
    {
        get
        {
            lock (_sync)
            {
                return _state.DockOrder
                    .Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("kwin:", StringComparison.Ordinal))
                    .Select(id => id.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? id[..^8] : id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Stores lightweight, shell-owned settings without treating hardware state as a preference.
    /// Values are strings so new settings can be introduced without making an older state file invalid.
    /// </summary>
    public string GetPreference(string key, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(key)) return fallback;
        lock (_sync)
        {
            return _state.Preferences.TryGetValue(key, out var value) ? value : fallback;
        }
    }

    public void SetPreference(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (RemoteWrite is { } remote) { remote("preference", [key, value ?? string.Empty]); return; }
        Update(s => s.Preferences[key] = value ?? string.Empty);
    }

    public void SetDockOrder(IEnumerable<string> order) => Update(s => s.DockOrder = order
        .Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("kwin:", StringComparison.Ordinal))
        .Select(id => id.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? id[..^8] : id)
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList());

    /// <summary>Returns application ids ordered by real launch frequency, then most recent use.</summary>
    public IReadOnlyList<string> GetMostUsedApplicationIds(int maximumCount)
    {
        if (maximumCount <= 0)
        {
            return [];
        }

        lock (_sync)
        {
            return _state.ApplicationUsage
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.Value.LaunchCount > 0)
                .OrderByDescending(entry => entry.Value.LaunchCount)
                .ThenByDescending(entry => entry.Value.LastLaunchedUtc)
                .Take(maximumCount)
                .Select(entry => entry.Key)
                .ToArray();
        }
    }

    /// <summary>Returns launch count and last-used timestamp for ranking search results.</summary>
    public (int LaunchCount, DateTimeOffset LastLaunchedUtc) GetApplicationUsage(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return (0, DateTimeOffset.MinValue);
        }

        lock (_sync)
        {
            if (_state.ApplicationUsage.TryGetValue(applicationId, out var direct))
            {
                return (direct.LaunchCount, direct.LastLaunchedUtc);
            }

            var alternate = applicationId.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
                ? applicationId[..^8]
                : applicationId + ".desktop";
            if (_state.ApplicationUsage.TryGetValue(alternate, out var fallback))
            {
                return (fallback.LaunchCount, fallback.LastLaunchedUtc);
            }

            return (0, DateTimeOffset.MinValue);
        }
    }

    /// <summary>Records a launch only after the system launcher accepted it.</summary>
    public void RecordApplicationLaunch(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return;
        }

        Update(state =>
        {
            if (!state.ApplicationUsage.TryGetValue(applicationId, out var usage))
            {
                usage = new ApplicationUsageState();
                state.ApplicationUsage[applicationId] = usage;
            }

            usage.LaunchCount++;
            usage.LastLaunchedUtc = DateTimeOffset.UtcNow;
        });
    }

    private void Update(Action<PersistedShellState> update)
    {
        lock (_sync)
        {
            update(_state);
            Save(_state);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private PersistedShellState Load()
    {
        try
        {
            if (!File.Exists(_path)) return new PersistedShellState();
            return JsonSerializer.Deserialize(File.ReadAllText(_path), DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.PersistedShellState) ?? new PersistedShellState();
        }
        catch (JsonException)
        {
            return new PersistedShellState();
        }
        catch (IOException)
        {
            return new PersistedShellState();
        }
        catch (UnauthorizedAccessException)
        {
            return new PersistedShellState();
        }
    }

    private void Save(PersistedShellState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            global::DaisyOS.Core.AtomicTextFile.Write(_path, JsonSerializer.Serialize(state, DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.PersistedShellState));
        }
        catch (IOException)
        {
            // A preference write must never take down an input interaction.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public sealed class PersistedShellState
    {
        public bool DoNotDisturb { get; set; }
        public bool GamingMode { get; set; }
        public bool ShowDateInSystemBar { get; set; }
        public double? LastKnownVolume { get; set; }
        public List<string> DockOrder { get; set; } = [];
        public bool? DockDefaultsInitialized { get; set; }
        public Dictionary<string, string> Preferences { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, ApplicationUsageState> ApplicationUsage { get; set; } = new(StringComparer.Ordinal);
    }

    public sealed class ApplicationUsageState
    {
        public int LaunchCount { get; set; }
        public DateTimeOffset LastLaunchedUtc { get; set; }
    }
}
