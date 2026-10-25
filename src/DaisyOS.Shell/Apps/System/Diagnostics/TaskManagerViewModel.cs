using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.System.Diagnostics;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.ViewModels;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

public sealed class ProcessRowViewModel(ProcessItemInfo info, long totalMemory = 0) : INotifyPropertyChanged
{
    private ProcessItemInfo _info = info;
    private long _totalMemory = totalMemory;
    private IImage? _applicationIcon;
    public string Key => WindowId ?? $"{Pid}:{StartTimeTicks}";
    public string? WindowId { get; init; }
    public string? WindowTitle { get; private set; }
    public bool CanClose { get; private set; }
    public IImage? ApplicationIcon => _applicationIcon;
    public bool HasApplicationIcon => ApplicationIcon is not null;
    public string FallbackIcon { get; private set; } = "cube";
    public int Pid => _info.Pid;
    public long StartTimeTicks => _info.StartTimeTicks;
    public string Name => WindowTitle ?? _info.Name;
    public string ExecutablePath => _info.ExecutablePath;
    public string User => _info.User;
    public string State => _info.State;
    public double CpuPercent => _info.CpuPercent;
    public long MemoryBytes => _info.MemoryBytes;
    public double MemoryPercent => _totalMemory > 0 ? MemoryBytes * 100d / _totalMemory : 0;
    public string CpuText => WindowId is not null && StartTimeTicks == 0 ? "Unavailable" : $"{CpuPercent:0.0}%";
    public string MemoryText => WindowId is not null && StartTimeTicks == 0 ? "Unavailable" : FormatBytes(MemoryBytes);
    public string Detail => WindowId is null ? $"PID {Pid} · {State} · UID {User}" : $"PID {Pid} · {State} · Process totals (shared by its windows)";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(ProcessRowViewModel value) { _info = value._info; _totalMemory = value._totalMemory; WindowTitle = value.WindowTitle; CanClose = value.CanClose; _applicationIcon = value._applicationIcon; FallbackIcon = value.FallbackIcon; PropertyChanged?.Invoke(this, new(null)); }
    public static ProcessRowViewModel ForWindow(ProcessItemInfo info, long total, CompositorWindowRecord window) => new(info, total)
        {
            WindowId = window.Id,
            WindowTitle = window.DisplayName,
            CanClose = window.CanClose,
            FallbackIcon = "squares-four",
            _applicationIcon = DesktopItemLoader.LoadApplicationIcon([
                window.IconName, window.DesktopFileId, window.DaisyAppId, window.AppId,
                info.ExecutablePath, info.Name, window.Title])
        };
    public void ResolveProcessIcon() => _applicationIcon ??= DesktopItemLoader.LoadApplicationIcon([
        ExecutablePath, Path.GetFileNameWithoutExtension(ExecutablePath), Name]);
    public static string FormatBytes(double bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824:0.0} GiB" : bytes >= 1048576 ? $"{bytes / 1048576:0.0} MiB" : $"{bytes / 1024:0.0} KiB";
}

public sealed class TaskManagerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly LinuxProcessMonitorService _processService = new();
    private readonly LinuxSystemMetricsService _metricsService = new();
    private readonly LinuxActivitySampler _activity = new();
    private readonly ICompositorWindowService? _windows;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _lifetime = new();
    private ProcessItemInfo[] _snapshot = [];
    private bool _refreshing, _disposed, _paused, _actionBusy;
    private string _searchFilter = "";
    private int _section, _sortIndex = 1;
    private ProcessRowViewModel? _selectedProcess;
    public ObservableCollection<ProcessRowViewModel> FilteredProcesses { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public TaskManagerViewModel(ICompositorWindowService? windows = null, bool start = true)
    {
        _windows = windows;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += OnTick;
        if (start) { _timer.Start(); _ = RefreshAsync(); }
    }
    private async void OnTick(object? sender, EventArgs e) { if (!_paused) await RefreshAsync(); }
    public double CpuPercent { get; private set; }
    public double MemoryPercent { get; private set; }
    public string CpuUsage { get; private set; } = "Sampling…";
    public string MemoryUsage { get; private set; } = "—";
    public string MemoryDetail { get; private set; } = "Reading physical memory…";
    public string CpuDetail => $"{LinuxSystemMetricsService.LogicalProcessorCount} logical processors · Total capacity";
    public string DiskRead { get; private set; } = "—";
    public string DiskWrite { get; private set; } = "—";
    public string NetworkReceive { get; private set; } = "—";
    public string NetworkSend { get; private set; } = "—";
    public string SwapDetail { get; private set; } = "—";
    public double SwapPercent { get; private set; }
    public string Uptime { get; private set; } = "—";
    public string Status { get; private set; } = "Reading system activity…";
    public string Error { get; private set; } = "";
    public bool HasError => Error.Length > 0;
    public int ProcessCount => _snapshot.Length;
    public long TotalMemory { get; private set; }
    public double[] CpuHistory { get; private set; } = [];
    public double[] MemoryHistory { get; private set; } = [];
    public bool IsOverview => _section == 0;
    public bool IsApplications => _section == 1;
    public bool IsProcesses => _section == 2;
    public bool IsList => !IsOverview;
    public string Heading => IsOverview ? "Overview" : IsApplications ? "Applications" : "Processes";
    public string Description => IsOverview ? "A live picture of your computer’s performance." : IsApplications ? "Open application windows across all workspaces." : "Every running process, including background services.";
    public string ListSummary => $"{FilteredProcesses.Count} {(IsApplications ? "windows" : "processes")} shown";
    public bool IsEmpty => FilteredProcesses.Count == 0;
    public string EmptyMessage => IsApplications && _windows?.IsAvailable != true ? "Application windows are unavailable outside the native desktop session." : "No matching items. Try a different name or PID.";
    public string PauseLabel => _paused ? "Resume" : "Pause";
    public string ActionLabel => IsApplications ? "Close window" : "End process";
    public bool CanEnd => !_actionBusy && SelectedProcess is { } p && (IsApplications ? p.CanClose : p.Pid > 1 && p.Pid != Environment.ProcessId);
    public bool CanForce => CanEnd && IsProcesses;
    public string SelectionDetail => SelectedProcess is not { } p
        ? "Select an item to see its executable and available actions."
        : p.WindowId is null && (p.Pid == Environment.ProcessId || p.Pid <= 1)
            ? $"{p.Name} · Protected system process. Ending it would close your desktop session."
            : $"{p.Name} · {p.ExecutablePath}";
    public string SearchFilter { get => _searchFilter; set { _searchFilter = value; ApplyFilter(); Notify(); } }
    public string[] SortOptions { get; } = ["Name", "CPU usage", "Memory usage", "PID"];
    public int SortIndex { get => _sortIndex; set { _sortIndex = value; ApplyFilter(); Notify(); } }
    public ProcessRowViewModel? SelectedProcess { get => _selectedProcess; set { _selectedProcess = value; Notify(); Notify(nameof(CanEnd)); Notify(nameof(CanForce)); Notify(nameof(SelectionDetail)); } }
    public void Navigate(int section) { _section = section; SelectedProcess = null; ApplyFilter(); NotifyAll(); }
    public void TogglePause() { _paused = !_paused; Status = _paused ? "Paused · Values are frozen" : "Live · Updates every 2 seconds"; NotifyAll(); if (!_paused) _ = RefreshAsync(); }

    public async Task RefreshAsync()
    {
        if (_refreshing || _disposed) return;
        _refreshing = true;
        try
        {
            var result = await Task.Run(async () =>
            {
                var metrics = await _metricsService.GetStatusAsync(_lifetime.Token);
                var processes = await _processService.GetProcessesAsync(_lifetime.Token);
                return (metrics, processes, activity: _activity.Sample());
            }, _lifetime.Token);
            if (_disposed) return;
            if (Error.StartsWith("Unable to update system activity:", StringComparison.Ordinal)) Error = "";
            var m = result.metrics;
            CpuPercent = m.CpuUsagePercent ?? 0;
            MemoryPercent = m.MemoryUsagePercent ?? 0;
            CpuUsage = m.CpuUsagePercent is { } cpu ? $"{cpu:0.0}%" : "Sampling…";
            MemoryUsage = m.MemoryUsagePercent is { } mem ? $"{mem:0.0}%" : "Unavailable";
            TotalMemory = m.MemoryTotalBytes ?? 0;
            MemoryDetail = $"{ProcessRowViewModel.FormatBytes(m.MemoryUsedBytes ?? 0)} of {ProcessRowViewModel.FormatBytes(TotalMemory)} in use";
            if (m.CpuUsagePercent.HasValue) CpuHistory = CpuHistory.Append(CpuPercent).TakeLast(60).ToArray();
            if (m.MemoryUsagePercent.HasValue) MemoryHistory = MemoryHistory.Append(MemoryPercent).TakeLast(60).ToArray();
            var a = result.activity;
            static string Rate(double? value) => value.HasValue ? ProcessRowViewModel.FormatBytes(value.Value) + "/s" : "Sampling…";
            DiskRead = Rate(a.ReadBytesPerSecond); DiskWrite = Rate(a.WriteBytesPerSecond);
            NetworkReceive = Rate(a.ReceiveBytesPerSecond); NetworkSend = Rate(a.SendBytesPerSecond);
            SwapDetail = a.SwapTotal == 0 ? "No swap configured" : $"{ProcessRowViewModel.FormatBytes(a.SwapUsed)} of {ProcessRowViewModel.FormatBytes(a.SwapTotal)}";
            SwapPercent = a.SwapTotal > 0 ? a.SwapUsed * 100d / a.SwapTotal : 0;
            Uptime = $"{(int)a.Uptime.TotalDays}d {a.Uptime.Hours}h {a.Uptime.Minutes}m";
            _snapshot = result.processes.ToArray();
            Status = $"{(_paused ? "Paused" : "Live · Every 2 seconds")} · Updated {DateTime.Now:T}";
            ApplyFilter(); NotifyAll();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error = $"Unable to update system activity: {ex.Message}"; Status = "Update failed · Showing last available values"; NotifyAll(); }
        finally { _refreshing = false; }
    }

    private void ApplyFilter()
    {
        var selectedKey = SelectedProcess?.Key;
        IEnumerable<ProcessRowViewModel> rows = _snapshot.Select(p =>
        {
            var row = new ProcessRowViewModel(p, TotalMemory);
            row.ResolveProcessIcon();
            return row;
        });
        if (IsApplications)
        {
            var processes = _snapshot.ToDictionary(p => p.Pid);
            rows = (_windows?.Windows.ToArray() ?? []).Select(w => ProcessRowViewModel.ForWindow(
                processes.GetValueOrDefault(w.ProcessId) ?? new(w.ProcessId, w.AppId, w.AppId, 0, 0, "Unknown"), TotalMemory, w));
        }
        var query = SearchFilter.Trim();
        rows = rows.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Pid.ToString().Contains(query) || p.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase));
        rows = SortIndex switch { 1 => rows.OrderByDescending(p => p.CpuPercent).ThenBy(p => p.Name), 2 => rows.OrderByDescending(p => p.MemoryBytes).ThenBy(p => p.Name), 3 => rows.OrderBy(p => p.Pid), _ => rows.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase) };
        var desired = rows.ToArray();
        var keys = desired.Select(p => p.Key).ToHashSet();
        for (var i = FilteredProcesses.Count - 1; i >= 0; i--) if (!keys.Contains(FilteredProcesses[i].Key)) FilteredProcesses.RemoveAt(i);
        for (var i = 0; i < desired.Length; i++)
        {
            var existing = FilteredProcesses.FirstOrDefault(p => p.Key == desired[i].Key);
            if (existing is null) FilteredProcesses.Insert(i, desired[i]);
            else { existing.Update(desired[i]); var oldIndex = FilteredProcesses.IndexOf(existing); if (oldIndex != i) FilteredProcesses.Move(oldIndex, i); }
        }
        SelectedProcess = FilteredProcesses.FirstOrDefault(p => p.Key == selectedKey);
        Notify(nameof(ListSummary)); Notify(nameof(IsEmpty)); Notify(nameof(EmptyMessage));
    }

    public async Task EndProcessAsync(ProcessRowViewModel process, bool force = false)
    {
        if (_actionBusy || _disposed) return;
        _actionBusy = true; Error = ""; NotifyAll();
        try
        {
            if (process.WindowId is { } windowId && _windows is not null)
                await _windows.CloseAsync(windowId, _lifetime.Token);
            else
            {
                var success = await _processService.EndInstanceAsync(process.Pid, process.StartTimeTicks, force, _lifetime.Token);
                if (!success) Error = "The process could not be ended. It may have exited, changed, or require another user’s permission.";
            }
            if (Error.Length == 0) Status = force ? "Force quit sent. Waiting for the next update…" : "Close request sent. The application may need your attention.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error = $"Unable to end process: {ex.Message}"; }
        finally { _actionBusy = false; NotifyAll(); }
    }
    public void SetActive(bool active) { if (_disposed) return; if (active) { _timer.Start(); if (!_paused) _ = RefreshAsync(); } else _timer.Stop(); }
    public void Dispose() { if (_disposed) return; _disposed = true; _timer.Stop(); _timer.Tick -= OnTick; _lifetime.Cancel(); }
    private void NotifyAll() => Notify(null);
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
