using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Taskbar;
using DaisyOS.System.Diagnostics;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

public sealed class TaskManagerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMonitorSamplingService _sampling;
    private readonly MonitorIconResolver _iconResolver = new();
    private IReadOnlyDictionary<int, IImage?> _processIcons = new Dictionary<int, IImage?>();
    private bool _showCores;
    private readonly ICompositorWindowService? _windows;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private readonly Dictionary<string, HistoryPoint[]> _history = new();
    private readonly Dictionary<string, (string Name, IImage? Icon)> _appIdentity = new();
    private readonly HashSet<string> _collapsed = new();
    private readonly Dictionary<int, string?> _deviceSelections = new();
    private ProcessItemInfo[] _processes = [];
    private CompositorWindowRecord[] _windowSnapshot = [];
    private MonitorResourceSnapshot? _resources;
    private string[] _diskNames = [], _networkNames = [];
    private string[] _deviceOptions = ["All interfaces"];
    private Task? _refreshTask;
    private bool _disposed, _paused, _busy, _active;
    private int _section, _resourceIndex, _processSort = 1, _appSort = 1, _freezeVersion, _detailVersion;
    private bool _processDescending = true, _appDescending = true, _myProcesses, _tree, _advancedColumns, _detailsOpen;
    private bool _descending { get => IsApplications ? _appDescending : _processDescending; set { if (IsApplications) _appDescending = value; else _processDescending = value; } }
    private string _processSearch = "", _appSearch = "";
    private ProcessRowViewModel? _selectedProcess;
    private ApplicationRowViewModel? _selectedApplication;
    private string _commandLine = "Select a process to inspect it.";
    private string _started = "—";
    private int _topSort;

    public TaskManagerViewModel(ICompositorWindowService? windows = null, bool start = true, IMonitorSamplingService? sampling = null)
    {
        _token = _lifetime.Token;
        _windows = windows;
        _sampling = sampling ?? new MonitorSamplingService();
        _timer.Tick += OnTick;
        if (windows is not null) windows.WindowsChanged += OnWindowsChanged;
        _active = start;
        if (start) { _timer.Start(); _ = RefreshAsync(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ProcessRowViewModel> FilteredProcesses { get; } = [];
    public ObservableCollection<ApplicationRowViewModel> Applications { get; } = [];
    public ObservableCollection<TopUsageRow> TopUsers { get; } = [];
    public ObservableCollection<ResourceContributor> Contributors { get; } = [];
    public ObservableCollection<CoreUsageRow> Cores { get; } = [];
    public ObservableCollection<DetailRow> ResourceDetails { get; } = [];
    public ObservableCollection<ProcessRowViewModel> ApplicationProcesses { get; } = [];
    public ObservableCollection<DetailRow> ProcessDetails { get; } = [];
    private void UpdateProcessDetails() => ReconcileValues(ProcessDetails, SelectedProcess is not { } p ? [] :
    [new("Executable", p.ExecutablePath), new("Command line", _commandLine), new("Started", _started),
     new("Parent PID", p.ParentPid.ToString()), new("User", $"{p.User} · UID {p.Info.UserId}"), new("State", p.State),
     new("Threads", p.Threads.ToString()), new("Resident RAM", p.MemoryText), new("Read / write", $"{p.ReadText} / {p.WriteText}")], row => row.Label, (row, next) => row.Update(next));

    public bool IsOverview => _section == 0;
    public bool IsApplications => _section == 1;
    public bool IsProcesses => _section == 2;
    public bool IsResources => _section == 3;
    public bool IsList => IsApplications || IsProcesses;
    public string Heading => _section switch { 1 => "Applications", 2 => "Processes", 3 => "Resources", _ => "Overview" };
    public string Description => _section switch
    {
        1 => "Open apps, their windows, and the resources we can attribute to them.",
        2 => "Inspect running processes and background services.",
        3 => "Follow activity over time and investigate individual resources.",
        _ => "What your computer is doing, and what is using its resources."
    };
    public string ActivityLabel => _paused ? "Paused" : "Live · 2 sec";
    public string PauseLabel => _paused ? "Resume" : "Pause";
    public string Status { get; private set; } = "Reading system activity…";
    public string Error { get; private set; } = "";
    public bool HasError => Error.Length > 0;
    public int ProcessCount => _processes.Length;
    public long TotalMemory => _resources?.Memory?.Total ?? 0;
    public string CpuModel => _resources?.Cpu?.Model ?? "Processor unavailable";
    public string SystemSummary => $"{CpuModel} · {(_resources?.Cpu is { } cpu ? cpu.LogicalProcessors.Count + " logical processors" : "Processor count unavailable")}\n{(TotalMemory > 0 ? MonitorFormat.Bytes(TotalMemory) + " RAM" : "RAM unavailable")} · {ProcessCount} processes · Up {Uptime}";
    public string Uptime => _resources?.Uptime is { } time ? $"{(int)time.TotalDays}d {time.Hours}h {time.Minutes}m" : "—";
    public double CpuPercent => _resources?.Cpu?.Usage ?? 0;
    public double MemoryPercent => _resources?.Memory?.Usage ?? 0;
    public string CpuUsage => MonitorFormat.Percent(_resources?.Cpu?.Usage);
    public string MemoryUsage => MonitorFormat.Percent(_resources?.Memory?.Usage);
    public string CpuDetail => _resources?.Cpu is { } cpu ? $"{cpu.LogicalProcessors.Count} logical processors · Total capacity" : "Processor information unavailable";
    public string MemoryDetail => _resources?.Memory is { } m ? $"{MonitorFormat.Bytes(m.Used)} / {MonitorFormat.Bytes(m.Total)} · {MonitorFormat.Bytes(m.Available)} available" : "Memory information unavailable";
    public string CpuAssessment => "Total processor utilization";
    public string MemoryAssessment => "RAM in use · Excludes reclaimable memory";
    public string DiskRead => MonitorFormat.Rate(Sum(_resources?.Disks, d => d.ReadRate));
    public string DiskWrite => MonitorFormat.Rate(Sum(_resources?.Disks, d => d.WriteRate));
    public string NetworkReceive => MonitorFormat.Rate(Sum(_resources?.Networks, n => n.ReceiveRate));
    public string NetworkSend => MonitorFormat.Rate(Sum(_resources?.Networks, n => n.SendRate));
    public string DiskUsage => DiskRead + " read";
    public string NetworkUsage => NetworkReceive + " received";
    public string SwapDetail => _resources?.Memory is not { } m ? "Unavailable" : m.SwapTotal == 0 ? "No swap configured" : $"{MonitorFormat.Bytes(m.SwapUsed)} / {MonitorFormat.Bytes(m.SwapTotal)}";
    public HistoryPoint[] CpuHistory => History("cpu");
    public HistoryPoint[] MemoryHistory => History("memory");
    public HistoryPoint[] DiskHistory => History("disk:all:read");
    public HistoryPoint[] DiskWriteHistory => History("disk:all:write");
    public HistoryPoint[] NetworkHistory => History("network:all:receive");
    public HistoryPoint[] NetworkSendHistory => History("network:all:send");
    public DateTimeOffset HistoryEnd => _resources?.Timestamp ?? DateTimeOffset.UtcNow;
    private string Header(int index, string label) => label + (SortIndex == index ? _descending ? " ↓" : " ↑" : "");
    public string NameHeader => Header(0, IsApplications ? "Application" : "Name");
    public string CpuHeader => Header(1, "CPU %");
    public string MemoryHeader => Header(2, "Memory");
    public string PidHeader => Header(3, "PID");
    public string UserHeader => Header(4, "User");
    public string StateHeader => Header(5, "State");
    public string ParentHeader => Header(6, "Parent");
    public string ThreadsHeader => Header(7, "Threads");
    public string ReadHeader => Header(8, "Read/s");
    public string WriteHeader => Header(9, "Write/s");
    public string[] SortOptions { get; } = ["Name", "CPU", "Memory", "PID", "User", "State", "Parent PID", "Threads", "Read", "Write"];
    public string[] AppSortOptions { get; } = ["Name", "CPU", "Memory"];
    public int SortIndex { get => IsApplications ? _appSort : _processSort; set { if (IsApplications) _appSort = value; else _processSort = value; _descending = value is not (0 or 3 or 4 or 5 or 6); RebuildLists(); NotifyAll(); } }
    public string SearchFilter { get => IsApplications ? _appSearch : _processSearch; set { if (IsApplications) _appSearch = value; else _processSearch = value; RebuildLists(); NotifyAll(); } }
    public bool MyProcesses { get => _myProcesses; set { _myProcesses = value; ApplyProcessFilter(); NotifyAll(); } }
    public bool TreeMode { get => _tree; set { _tree = value; ApplyProcessFilter(); NotifyAll(); } }
    public double ProcessTableMinWidth => (AdvancedColumns ? 1160 : 752) + (TreeMode ? Math.Min(12, FilteredProcesses.Select(p => p.Depth).DefaultIfEmpty().Max()) * 16 : 0);
    public bool HasTopUsers => TopUsers.Count > 0;
    public bool AdvancedColumns { get => _advancedColumns; set { _advancedColumns = value; NotifyAll(); } }
    public int TopSort { get => _topSort; set { _topSort = value; RebuildTop(); NotifyAll(); } }
    public string[] TopSortOptions { get; } = ["CPU", "Memory"];
    public string TopUsersNote => _windows?.IsAvailable == true ? "Attributed app usage · Shared or inaccessible processes may be excluded" : "Processes · App windows unavailable outside the native desktop";
    public string ListSummary => IsApplications ? $"{Applications.Count} apps · Attributed usage" : $"{FilteredProcesses.Count} processes · CPU is % of total capacity · Memory is resident RAM";
    public bool IsEmpty => IsApplications ? Applications.Count == 0 : FilteredProcesses.Count == 0;
    public string EmptyMessage => IsApplications && _windows?.IsAvailable != true ? "Application windows are unavailable outside the native desktop session." : "No matching items. Try a different name, user, or PID.";

    public ProcessRowViewModel? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (ReferenceEquals(_selectedProcess, value)) return;
            _selectedProcess = value; _detailsOpen = value is not null;
            _commandLine = value is null ? "—" : "Reading…"; _started = "—";
            var version = ++_detailVersion;
            UpdateProcessDetails();
            NotifyAll();
            if (value is not null) _ = LoadDetailsAsync(value, version);
        }
    }
    public ApplicationRowViewModel? SelectedApplication { get => _selectedApplication; set { _selectedApplication = value; _detailsOpen = value is not null; UpdateApplicationProcesses(); NotifyAll(); } }
    public bool DetailsOpen => _detailsOpen && IsList && (IsProcesses ? SelectedProcess is not null : SelectedApplication is not null);
    public string DetailsHeading => IsApplications ? SelectedApplication?.Name ?? "Application" : SelectedProcess?.Name ?? "Process";
    public bool CanEnd => !_busy && SelectedProcess is { Pid: > 1, StartTimeTicks: > 0 } p && p.Pid != Environment.ProcessId;
    public bool CanCloseApp => !_busy && SelectedApplication?.Group.Windows.Any(w => w.CanClose) == true;
    public string ProcessProtection => SelectedProcess is { } p && (p.Pid <= 1 || p.Pid == Environment.ProcessId) ? "Protected system process. Ending it would close your desktop session." : "Resident RAM includes shared pages; process totals can exceed physical RAM use. A dash means unavailable or awaiting a rate sample.";

    public int ResourceIndex { get => _resourceIndex; set { _resourceIndex = Math.Clamp(value, 0, 3); RebuildResource(); NotifyAll(); } }
    public string[] ResourceOptions { get; } = ["CPU", "Memory", "Disk", "Network"];
    public bool IsMemoryResource => ResourceIndex == 1;
    public bool IsDiskResource => ResourceIndex == 2;
    public bool IsNetworkResource => ResourceIndex == 3;
    public bool IsCpuResource => ResourceIndex == 0;
    public bool HasDeviceSelector => ResourceIndex >= 2;
    public bool HasContributorRows => Contributors.Count > 0;
    public string ContributorNote => ResourceIndex switch { 0 => "CPU is a percentage of total system capacity.", 1 => "Resident RAM includes shared pages. Process totals may exceed physical memory use.", _ => "Process read and write rates cover all devices. Inaccessible counters are excluded." };
    public bool HasContributors => ResourceIndex < 3;
    public string[] DeviceOptions => _deviceOptions;
    public int DeviceIndex
    {
        get => Math.Max(0, Array.IndexOf(DeviceOptions, SelectedDevice));
        set { if (value >= 0 && value < DeviceOptions.Length) SelectedDevice = DeviceOptions[value]; }
    }
    public string? SelectedDevice { get => _deviceSelections.GetValueOrDefault(ResourceIndex) ?? DeviceOptions[0]; set { if (value is null || value == SelectedDevice) return; _deviceSelections[ResourceIndex] = value; RebuildResource(); NotifyAll(); } }
    private string DeviceKey => SelectedDevice == DeviceOptions[0] ? "all" : SelectedDevice ?? "all";
    public string ResourceTitle => ResourceOptions[ResourceIndex];
    public bool ShowCoreCharts => IsCpuResource && _showCores;
    public string CoreToggleLabel => (_showCores ? "Hide" : "Show") + $" logical processors ({Cores.Count})";
    public void ToggleCores() { _showCores = !_showCores; NotifyAll(); }
    public string ResourcePrimaryLabel => ResourceIndex switch { 0 => "Total utilization", 1 => "In use", 2 => "Read", _ => "Receive" };
    public string ResourceSecondaryLabel => ResourceIndex switch { 0 => "Logical processors", 1 => "Available RAM", 2 => "Write", _ => "Send" };
    public string ResourceSecondaryValue => ResourceIndex switch { 0 => _resources?.Cpu?.LogicalProcessors.Count.ToString() ?? "—", 1 => _resources?.Memory is { } memory ? MonitorFormat.Bytes(memory.Available) : "—", 2 => MonitorFormat.Rate(SelectedDiskRate(true)), _ => MonitorFormat.Rate(SelectedNetworkRate(true)) };
    public string ResourceIcon => ResourceIndex switch { 0 => "cpu", 1 => "squares-four", 2 => "hard-drive", _ => "arrows-down-up" };
    public string ResourceValue => ResourceIndex switch { 0 => CpuUsage, 1 => MemoryUsage, 2 => MonitorFormat.Rate(SelectedDiskRate(false)), _ => MonitorFormat.Rate(SelectedNetworkRate(false)) };
    public string ResourceExplanation => ResourceIndex switch
    {
        0 => "Percentage of total processor capacity. Load averages cover 1, 5, and 15 minutes.",
        1 => "Used RAM excludes available memory. File cache can be reclaimed when apps need it.",
        2 => "Physical device throughput. Process I/O is system-wide and cannot always be assigned to a device.",
        _ => "Interface counters since boot or interface reset. Summed virtual interfaces may count the same traffic more than once."
    };
    public HistoryPoint[] ResourceHistory => History(ResourceIndex switch { 0 => "cpu", 1 => "memory", 2 => $"disk:{DeviceKey}:read", _ => $"network:{DeviceKey}:receive" });
    public HistoryPoint[] ResourceSecondaryHistory => ResourceIndex < 2 ? [] : History(ResourceIndex == 2 ? $"disk:{DeviceKey}:write" : $"network:{DeviceKey}:send");
    public double ResourceMaximum => ResourceIndex < 2 ? 100 : 0;
    public string ResourceLegend => ResourceIndex switch { 0 or 1 => "0–100% · 2-minute history", 2 => "Read ━  /  Write ┄ · Bytes/s · Automatic scale", _ => "Receive ━  /  Send ┄ · Bytes/s · Automatic scale" };
    public string ResourceScale => ResourceMaximum == 100 ? "100%" : MonitorFormat.Rate(Math.Max(1, ResourceHistory.Concat(ResourceSecondaryHistory).Select(p => p.Value ?? 0).DefaultIfEmpty().Max()));

    public void Navigate(int section)
    {
        _section = Math.Clamp(section, 0, 3);
        RebuildLists(); RebuildResource(); NotifyAll();
    }
    public void OpenResource(int index) { ResourceIndex = index; Navigate(3); }
    public void OpenDetails() { _detailsOpen = true; NotifyAll(); }
    public void CloseDetails() { _detailsOpen = false; NotifyAll(); }
    public void SortBy(int index)
    {
        if (SortIndex == index) _descending = !_descending;
        else { if (IsApplications) _appSort = index; else _processSort = index; _descending = index is not (0 or 3 or 4 or 5 or 6); }
        RebuildLists(); NotifyAll();
    }
    public void ToggleBranch(ProcessRowViewModel row)
    {
        if (!_collapsed.Add(row.Key)) _collapsed.Remove(row.Key);
        ApplyProcessFilter(); NotifyAll();
    }
    public void InspectProcess(int pid, long? startTimeTicks = null)
    {
        _processSearch = ""; _myProcesses = false; _tree = false; Navigate(2);
        SelectedProcess = FilteredProcesses.FirstOrDefault(p => p.Pid == pid && (!startTimeTicks.HasValue || p.StartTimeTicks == startTimeTicks));
        if (SelectedProcess is null) { Status = "The selected process exited or changed identity"; NotifyAll(); }
    }
    public void InspectTop(TopUsageRow row)
    {
        if (row.AppKey is { } key) { _appSearch = ""; Navigate(1); SelectedApplication = Applications.FirstOrDefault(a => a.Key == key); }
        else if (row.Pid is { } pid) InspectProcess(pid, row.StartTimeTicks);
    }
    public void RefreshAppearance() { if (_disposed) return; _appIdentity.Clear(); if (_active && !_paused) _ = RefreshAsync(); }
    public void TogglePause()
    {
        _paused = !_paused; _freezeVersion++;
        Status = _paused ? "Paused · Measurements and ordering are frozen" : "Live · Updating…";
        NotifyAll(); if (!_paused && _active) _ = ResumeSamplingAsync();
    }

    public Task RefreshAsync()
    {
        if (_disposed) return Task.CompletedTask;
        return _refreshTask is { IsCompleted: false } running ? running : _refreshTask = RefreshCoreAsync();
    }
    private async Task RefreshCoreAsync()
    {
        if (_disposed) return;
        var freeze = _freezeVersion;
        try
        {
            var sample = await _sampling.SampleAsync(_token);
            if (_disposed || freeze != _freezeVersion) return;
            var processes = sample.Processes?.ToArray() ?? _processes;
            var windows = _windows?.IsAvailable == true ? _windows.Windows.ToArray() : [];
            var icons = await _iconResolver.ResolveAsync(processes, windows, _token);
            if (_disposed || freeze != _freezeVersion) return;
            _processIcons = icons;
            _resources = sample.Resources;
            if (_resources.Disks is { } disks) _diskNames = disks.Select(d => d.Name).ToArray();
            if (_resources.Networks is { } networks) _networkNames = networks.Select(n => n.Name).ToArray();
            if (sample.Processes is not null) _processes = sample.Processes.ToArray();
            _windowSnapshot = windows;
            Error = string.Join(" · ", sample.Resources.Errors.Concat(sample.ProcessError is { } error ? new[] { error } : []));
            UpdateHistories();
            ReconcileValues(Cores, _resources.Cpu?.LogicalProcessors.Select((usage, index) =>
            {
                var name = _resources.Cpu.LogicalProcessorNames.ElementAtOrDefault(index) ?? index.ToString();
                return new CoreUsageRow(name.StartsWith("cpu", StringComparison.Ordinal) ? name.ToUpperInvariant() : "CPU " + name, usage, History("core:" + name), HistoryEnd);
            }).ToArray() ?? [], row => row.Name, (row, next) => row.Update(next));
            RebuildLists(); RebuildTop(); RebuildResource(); UpdateProcessDetails();
            Status = $"{(_paused ? "Paused" : _active ? "Live · Every 2 seconds" : "Sampling suspended")} · Updated {sample.Resources.Timestamp.ToLocalTime():T}";
            NotifyAll();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!_disposed) { Error = $"Unable to update activity: {e.Message}"; Status = "Update failed · Showing last sample"; NotifyAll(); } }
    }

    private void UpdateHistories()
    {
        if (_resources is not { } r) return;
        void Add(string key, double? value) => _history[key] = MonitorHistory.Append(History(key), r.Timestamp, value);
        Add("cpu", r.Cpu?.Usage); Add("memory", r.Memory?.Usage);
        if (r.Cpu is { } cpu) for (var i = 0; i < cpu.LogicalProcessors.Count; i++) Add("core:" + (cpu.LogicalProcessorNames.ElementAtOrDefault(i) ?? i.ToString()), cpu.LogicalProcessors[i]);
        Add("disk:all:read", Sum(r.Disks, d => d.ReadRate)); Add("disk:all:write", Sum(r.Disks, d => d.WriteRate));
        Add("network:all:receive", Sum(r.Networks, n => n.ReceiveRate)); Add("network:all:send", Sum(r.Networks, n => n.SendRate));
        foreach (var d in r.Disks ?? []) { Add($"disk:{d.Name}:read", d.ReadRate); Add($"disk:{d.Name}:write", d.WriteRate); }
        foreach (var n in r.Networks ?? []) { Add($"network:{n.Name}:receive", n.ReceiveRate); Add($"network:{n.Name}:send", n.SendRate); }
        foreach (var key in _history.Keys.Where(k => _history[k].LastOrDefault()?.Time < r.Timestamp.AddMinutes(-2)).ToArray()) _history.Remove(key);
    }
    private HistoryPoint[] History(string key) => _history.GetValueOrDefault(key) ?? [];
    private static double? Sum<T>(IReadOnlyList<T>? items, Func<T, double?> value)
    {
        if (items is null) return null;
        if (items.Count == 0) return null;
        var values = items.Select(value).ToArray();
        return values.All(v => v.HasValue) ? values.Sum(v => v!.Value) : null;
    }
    private double? SelectedDiskRate(bool write) => _resources?.Disks is not { } disks ? null : DeviceKey == "all" ? Sum(disks, d => write ? d.WriteRate : d.ReadRate)
        : disks.FirstOrDefault(d => d.Name == DeviceKey) is { } disk ? write ? disk.WriteRate : disk.ReadRate : null;
    private double? SelectedNetworkRate(bool send) => _resources?.Networks is not { } networks ? null : DeviceKey == "all" ? Sum(networks, n => send ? n.SendRate : n.ReceiveRate)
        : networks.FirstOrDefault(n => n.Name == DeviceKey) is { } network ? send ? network.SendRate : network.ReceiveRate : null;

    private void RebuildLists()
    {
        RebuildApplications();
        if (IsProcesses) ApplyProcessFilter();
    }
    private void RebuildApplications()
    {
        var groups = ApplicationAttribution.Group(_windowSnapshot, _processes);
        var selected = _selectedApplication?.Key;
        var existing = Applications.ToDictionary(a => a.Key);
        var query = _appSearch.Trim();
        var rows = groups.Select(g =>
        {
            if (!_appIdentity.TryGetValue(g.Key, out var identity))
            {
                var w = g.Windows[0];
                var file = DesktopItemLoader.FindDesktopEntryFilePath([w.DesktopFileId, w.DaisyAppId, w.AppId]);
                string? name = null;
                try { if (file is not null) name = File.ReadLines(file).FirstOrDefault(l => l.StartsWith("Name=", StringComparison.Ordinal))?[5..]; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                identity = (name ?? TaskbarAppLabel.Format(g.Identity, g.Identity), DesktopItemLoader.LoadApplicationIcon([w.IconName, w.DesktopFileId, w.DaisyAppId, w.AppId]));
                _appIdentity[g.Key] = identity;
            }
            if (existing.TryGetValue(g.Key, out var row)) { row.ApplicationIcon = identity.Icon; row.Update(g); return row; }
            var created = new ApplicationRowViewModel { Group = g, Name = identity.Name, ApplicationIcon = identity.Icon };
            created.Update(g);
            return created;
        }).Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || a.Group.Processes.Any(p => Matches(p, query))).ToArray();
        IEnumerable<ApplicationRowViewModel> sorted = _appSort switch { 1 => _appDescending ? rows.OrderByDescending(a => a.CpuPercent) : rows.OrderBy(a => a.CpuPercent), 2 => _appDescending ? rows.OrderByDescending(a => a.MemoryBytes) : rows.OrderBy(a => a.MemoryBytes), _ => rows.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase) };
        if (_appSort == 0 && _appDescending) sorted = sorted.Reverse();
        Reconcile(Applications, sorted.ToArray(), a => a.Key);
        _selectedApplication = Applications.FirstOrDefault(a => a.Key == selected);
        UpdateApplicationProcesses();
        foreach (var key in _appIdentity.Keys.Except(groups.Select(g => g.Key)).ToArray()) _appIdentity.Remove(key);
    }
    private static bool Matches(ProcessItemInfo p, string query) => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Pid.ToString().Contains(query) || p.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase) || p.User.Contains(query, StringComparison.OrdinalIgnoreCase);
    private IEnumerable<ProcessItemInfo> SortProcesses(IEnumerable<ProcessItemInfo> rows)
    {
        Func<ProcessItemInfo, object> key = _processSort switch
        {
            1 => p => p.CpuPercent, 2 => p => p.MemoryBytes, 3 => p => p.Pid, 4 => p => p.User, 5 => p => p.State,
            6 => p => p.ParentPid, 7 => p => p.ThreadCount, 8 => p => p.ReadBytesPerSecond ?? -1, 9 => p => p.WriteBytesPerSecond ?? -1, _ => p => p.Name
        };
        return _descending ? rows.OrderByDescending(key).ThenBy(p => p.Pid) : rows.OrderBy(key).ThenBy(p => p.Pid);
    }
    private void ApplyProcessFilter()
    {
        var query = _processSearch.Trim();
        var currentUid = _processes.FirstOrDefault(p => p.Pid == Environment.ProcessId)?.UserId;
        var candidates = _processes.Where(p => !_myProcesses || p.User == Environment.UserName || !string.IsNullOrEmpty(currentUid) && p.UserId == currentUid).ToDictionary(p => p.Pid);
        var matches = candidates.Values.Where(p => Matches(p, query)).Select(p => p.Pid).ToHashSet();
        // Search reveals ancestors so matching children retain meaningful context.
        if (_tree && query.Length > 0)
            foreach (var id in matches.ToArray())
            {
                var current = candidates[id]; var visited = new HashSet<int>();
                while (visited.Add(current.Pid) && candidates.TryGetValue(current.ParentPid, out var parent) && parent.StartTimeTicks <= current.StartTimeTicks)
                { matches.Add(parent.Pid); current = parent; }
            }
        var visible = candidates.Values.Where(p => matches.Contains(p.Pid)).ToDictionary(p => p.Pid);
        var ordered = new List<(ProcessItemInfo Process, int Depth)>();
        var visitedPids = new HashSet<int>();
        if (_tree)
        {
            var children = visible.Values.Where(p => p.ParentPid != p.Pid && visible.TryGetValue(p.ParentPid, out var parent) && parent.StartTimeTicks <= p.StartTimeTicks).GroupBy(p => p.ParentPid).ToDictionary(g => g.Key, g => g.ToArray());
            void Walk(ProcessItemInfo p, int depth)
            {
                if (!visitedPids.Add(p.Pid)) return;
                ordered.Add((p, depth));
                if (query.Length == 0 && _collapsed.Contains($"{p.Pid}:{p.StartTimeTicks}")) return;
                foreach (var child in SortProcesses(children.GetValueOrDefault(p.Pid) ?? [])) Walk(child, depth + 1);
            }
            foreach (var p in SortProcesses(visible.Values.Where(p => !visible.TryGetValue(p.ParentPid, out var parent) || p.ParentPid == p.Pid || parent.StartTimeTicks > p.StartTimeTicks))) Walk(p, 0);
            // Cycles have no root; render them once without losing any processes.
            foreach (var p in SortProcesses(visible.Values.Where(p => !visitedPids.Contains(p.Pid) && !HasCollapsedAncestor(p, visible)))) Walk(p, 0);
        }
        else ordered.AddRange(SortProcesses(visible.Values).Select(p => (p, 0)));
        var existing = FilteredProcesses.ToDictionary(p => p.Key);
        var selected = _selectedProcess?.Key;
        var parentIds = visible.Values.Select(p => p.ParentPid).ToHashSet();
        var rows = ordered.Select(item =>
        {
            var p = item.Process;
            var row = new ProcessRowViewModel(p, TotalMemory) { ApplicationIcon = _processIcons.GetValueOrDefault(p.Pid), Depth = item.Depth, HasChildren = _tree && parentIds.Contains(p.Pid), ExpandIcon = _collapsed.Contains($"{p.Pid}:{p.StartTimeTicks}") ? "caret-right" : "caret-down" };
            if (existing.TryGetValue(row.Key, out var previous)) { previous.Update(row); return previous; }
            return row;
        }).ToArray();
        Reconcile(FilteredProcesses, rows, p => p.Key);
        var restored = FilteredProcesses.FirstOrDefault(p => p.Key == selected);
        if (!ReferenceEquals(restored, _selectedProcess)) SelectedProcess = restored;
        _collapsed.IntersectWith(_processes.Select(p => $"{p.Pid}:{p.StartTimeTicks}"));
    }
    private bool HasCollapsedAncestor(ProcessItemInfo p, Dictionary<int, ProcessItemInfo> rows)
    {
        var visited = new HashSet<int>();
        while (visited.Add(p.Pid) && rows.TryGetValue(p.ParentPid, out var parent))
        { if (_collapsed.Contains($"{parent.Pid}:{parent.StartTimeTicks}")) return true; p = parent; }
        return false;
    }
    private static void Reconcile<T>(ObservableCollection<T> target, IReadOnlyList<T> desired, Func<T, string> key)
    {
        var keys = desired.Select(key).ToHashSet();
        for (var i = target.Count - 1; i >= 0; i--) if (!keys.Contains(key(target[i]))) target.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i])) continue;
            var old = target.IndexOf(desired[i]);
            if (old < 0) target.Insert(i, desired[i]); else if (old != i) target.Move(old, i);
        }
    }
    private static void ReconcileValues<T>(ObservableCollection<T> target, IReadOnlyList<T> desired, Func<T, string> key, Action<T, T> update)
    {
        var existing = target.ToDictionary(key);
        var rows = desired.Select(next =>
        {
            if (!existing.TryGetValue(key(next), out var previous)) return next;
            update(previous, next); return previous;
        }).ToArray();
        Reconcile(target, rows, key);
    }
    private void UpdateApplicationProcesses() => ReconcileValues(ApplicationProcesses,
        SelectedApplication?.Group.Processes.Select(p => new ProcessRowViewModel(p, TotalMemory) { ApplicationIcon = _processIcons.GetValueOrDefault(p.Pid) }).ToArray() ?? [], p => p.Key, (row, next) => row.Update(next));
    private void RebuildTop()
    {
        // Use all apps, independent of the Applications search and sort state.
        var groups = ApplicationAttribution.Group(_windowSnapshot, _processes);
        TopUsageRow[] desired = _windows?.IsAvailable == true
            ? groups.Where(g => g.HasUsage).OrderByDescending(g => TopSort == 0 ? g.Cpu : g.Memory).Take(5)
                .Select(g => new TopUsageRow(_appIdentity.GetValueOrDefault(g.Key).Name ?? TaskbarAppLabel.Format(g.Key, g.Key), g.Attribution,
                    TopSort == 0 ? MonitorFormat.Percent(g.Processes.All(p => p.CpuUsageAvailable) ? g.Cpu : null) : MonitorFormat.Bytes(g.Memory), g.Key, null)).ToArray()
            : _processes.OrderByDescending(p => TopSort == 0 ? p.CpuPercent : p.MemoryBytes).Take(5)
                .Select(p => new TopUsageRow(p.Name, $"PID {p.Pid} · {p.User}", TopSort == 0 ? MonitorFormat.Percent(p.CpuUsageAvailable ? p.CpuPercent : null) : MonitorFormat.Bytes(p.MemoryBytes), null, p.Pid, p.StartTimeTicks)).ToArray();
        foreach (var row in desired) row.ApplicationIcon = row.AppKey is { } key ? _appIdentity.GetValueOrDefault(key).Icon : _processIcons.GetValueOrDefault(row.Pid ?? 0);
        ReconcileValues(TopUsers, desired, row => row.Key, (row, next) => row.Update(next));
    }
    private void RebuildResource()
    {
        var choices = ResourceIndex == 2 ? new[] { "All physical disks" }.Concat(_diskNames).ToArray() : new[] { "All interfaces" }.Concat(_networkNames).ToArray();
        if (!choices.SequenceEqual(_deviceOptions)) _deviceOptions = choices;
        if (!DeviceOptions.Contains(SelectedDevice)) _deviceSelections[ResourceIndex] = DeviceOptions[0];
        if (_resources is not { } r) { ResourceDetails.Clear(); Contributors.Clear(); return; }
        var memory = r.Memory;
        IReadOnlyList<DetailRow> details = ResourceIndex switch
        {
            0 => [new("Processor", CpuModel), new("Logical processors", r.Cpu?.LogicalProcessors.Count.ToString() ?? "—"), new("Load · 1 / 5 / 15 min", r.Cpu?.LoadAverage ?? "Unavailable"), new("OS architecture", global::System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString())],
            1 => [new("Total RAM", memory is null ? "—" : MonitorFormat.Bytes(memory.Total)), new("In use", memory is null ? "—" : MonitorFormat.Bytes(memory.Used)), new("Available", memory is null ? "—" : MonitorFormat.Bytes(memory.Available)),
                  new("Reclaimable cache", memory is null ? "—" : MonitorFormat.Bytes(memory.Cache)), new("Swap", SwapDetail),
                  new("Memory pressure · 10 sec", memory?.Pressure is { } pressure ? $"{pressure:0.00}% time waiting for memory" : "Unavailable on this system")],
            2 => [new("Device", SelectedDevice ?? "—"), new("Read / write", $"{MonitorFormat.Rate(SelectedDiskRate(false))} / {MonitorFormat.Rate(SelectedDiskRate(true))}"),
                  new("I/O activity", DeviceKey == "all" ? "Select a disk to inspect time busy" : MonitorFormat.Percent(r.Disks?.FirstOrDefault(d => d.Name == DeviceKey)?.Activity)), new("Physical disks", r.Disks?.Count.ToString() ?? "—")],
            _ => NetworkDetails(r)
        };
        ReconcileValues(ResourceDetails, details, row => row.Label, (row, next) => row.Update(next));
        ResourceContributor[] contributors = ResourceIndex == 3 ? [] : _processes.Where(p => ResourceIndex != 2 || p.ReadBytesPerSecond.HasValue || p.WriteBytesPerSecond.HasValue)
            .OrderByDescending(p => ResourceIndex switch { 0 => p.CpuPercent, 1 => p.MemoryBytes, _ => (p.ReadBytesPerSecond ?? 0) + (p.WriteBytesPerSecond ?? 0) }).Take(5)
            .Select(p => new ResourceContributor(p.Name, ResourceIndex switch { 0 => MonitorFormat.Percent(p.CpuUsageAvailable ? p.CpuPercent : null), 1 => MonitorFormat.Bytes(p.MemoryBytes), _ => $"{MonitorFormat.Rate(p.ReadBytesPerSecond)} read · {MonitorFormat.Rate(p.WriteBytesPerSecond)} write" }, p.Pid, p.StartTimeTicks)).ToArray();
        foreach (var row in contributors) row.ApplicationIcon = _processIcons.GetValueOrDefault(row.Pid);
        ReconcileValues(Contributors, contributors, row => row.Key, (row, next) => row.Update(next));
    }
    private IReadOnlyList<DetailRow> NetworkDetails(MonitorResourceSnapshot r)
    {
        var networks = DeviceKey == "all" ? r.Networks : r.Networks?.Where(n => n.Name == DeviceKey).ToArray();
        return [new("Interface", SelectedDevice ?? "—"), new("State", networks is null ? "Unavailable" : string.Join(" · ", networks.Select(n => $"{n.Name}: {n.State}"))),
            new("Receive / send", $"{MonitorFormat.Rate(SelectedNetworkRate(false))} / {MonitorFormat.Rate(SelectedNetworkRate(true))}"),
            new("Received / sent total", networks is null ? "—" : $"{MonitorFormat.Bytes(networks.Sum(n => (double)n.Received))} / {MonitorFormat.Bytes(networks.Sum(n => (double)n.Sent))}")];
    }
    private async Task LoadDetailsAsync(ProcessRowViewModel process, int version)
    {
        try
        {
            var details = await _sampling.GetDetailsAsync(process.Pid, process.StartTimeTicks, _token);
            if (_disposed || version != _detailVersion || SelectedProcess?.Key != process.Key) return;
            _commandLine = details?.CommandLine ?? "Process exited or details are inaccessible";
            _started = details?.StartedAt?.ToLocalTime().ToString("g") ?? "—";
            UpdateProcessDetails();
        }
        catch (OperationCanceledException) { }
    }
    public async Task ShowWindowAsync(WindowRowViewModel row)
    {
        if (_windows is null) return;
        try { await _windows.ActivateOrMinimizeAsync(row.Window.Id, false, _token); }
        catch (Exception e) when (e is not OperationCanceledException) { Error = "Unable to show window: " + e.Message; NotifyAll(); }
        catch (OperationCanceledException) { }
    }
    public async Task CloseWindowAsync(WindowRowViewModel row)
    {
        if (_windows is null || !row.CanClose || _busy) return;
        await RunActionAsync(() => _windows.CloseAsync(row.Window.Id, _token));
    }
    public async Task CloseApplicationAsync()
    {
        if (!CanCloseApp || _windows is null || SelectedApplication is not { } app) return;
        var windows = app.Group.Windows.Where(w => w.CanClose).ToArray();
        await RunActionAsync(async () => { foreach (var window in windows) await _windows.CloseAsync(window.Id, _token); });
    }
    public async Task EndProcessAsync(ProcessRowViewModel process, bool force = false)
    {
        if (_busy || _disposed || process.Pid <= 1 || process.Pid == Environment.ProcessId || process.StartTimeTicks <= 0) return;
        await RunActionAsync(async () =>
        {
            if (!await _sampling.EndInstanceAsync(process.Pid, process.StartTimeTicks, force, _token))
                throw new InvalidOperationException("Process exited, changed identity, or requires another user’s permission.");
        });
    }
    private async Task RunActionAsync(Func<Task> action)
    {
        _busy = true; Error = ""; NotifyAll();
        try { await action(); Status = "Request sent · The application may need your attention"; }
        catch (OperationCanceledException) { }
        catch (Exception e) { Error = e.Message; }
        finally { _busy = false; NotifyAll(); }
    }
    private async void OnTick(object? sender, EventArgs e) { if (!_paused) await RefreshAsync(); }
    private void OnWindowsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed || _paused || !_active) return;
        _windowSnapshot = _windows?.IsAvailable == true ? _windows.Windows.ToArray() : [];
        RebuildApplications(); RebuildTop(); NotifyAll();
    });
    public void SetActive(bool active)
    {
        if (_disposed || _active == active) return;
        _active = active;
        if (active) { _timer.Start(); if (!_paused) _ = ResumeSamplingAsync(); }
        else { _freezeVersion++; _timer.Stop(); }
    }
    private async Task ResumeSamplingAsync()
    {
        var version = _freezeVersion;
        if (_refreshTask is { IsCompleted: false } pending) await pending;
        if (!_disposed && _active && !_paused && version == _freezeVersion) await RefreshAsync();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _freezeVersion++; _detailVersion++;
        _timer.Stop(); _timer.Tick -= OnTick;
        if (_windows is not null) _windows.WindowsChanged -= OnWindowsChanged;
        _lifetime.Cancel(); _lifetime.Dispose();
    }
    private void NotifyAll() => Notify(null);
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
