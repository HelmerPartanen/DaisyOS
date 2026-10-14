using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.System.Diagnostics;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

public sealed class ProcessRowViewModel : INotifyPropertyChanged
{
    public int Pid { get; }
    private string _name;
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    private string _executablePath;
    public string ExecutablePath { get => _executablePath; set { _executablePath = value; OnPropertyChanged(); } }
    private double _cpuPercent;
    public double CpuPercent { get => _cpuPercent; set { _cpuPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(CpuText)); } }
    public string CpuText => $"{CpuPercent:0.0}%";
    private long _memoryBytes;
    public long MemoryBytes { get => _memoryBytes; set { _memoryBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(MemoryText)); } }
    public string MemoryText => FormatBytes(MemoryBytes);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public ProcessRowViewModel(ProcessItemInfo info)
    {
        Pid = info.Pid;
        _name = info.Name;
        _executablePath = info.ExecutablePath;
        _cpuPercent = info.CpuPercent;
        _memoryBytes = info.MemoryBytes;
    }

    public void Update(ProcessItemInfo info)
    {
        Name = info.Name;
        ExecutablePath = info.ExecutablePath;
        CpuPercent = info.CpuPercent;
        MemoryBytes = info.MemoryBytes;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.1} GB";
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):0.0} MB";
        return $"{bytes / 1024.0:0.0} KB";
    }
}

public sealed class TaskManagerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly LinuxProcessMonitorService _processService = new();
    private readonly LinuxSystemMetricsService _metricsService = new();
    private readonly DispatcherTimer _timer;

    private string _cpuUsage = "0%";
    private string _memoryUsage = "0%";
    private string _memoryDetail = "Loading…";
    private int _processCount;
    private string _searchFilter = string.Empty;
    private ProcessRowViewModel? _selectedProcess;
    private ObservableCollection<ProcessRowViewModel> _allProcesses = new();
    private ObservableCollection<ProcessRowViewModel> _filteredProcesses = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CpuUsage
    {
        get => _cpuUsage;
        private set { _cpuUsage = value; OnPropertyChanged(); }
    }

    public string MemoryUsage
    {
        get => _memoryUsage;
        private set { _memoryUsage = value; OnPropertyChanged(); }
    }

    public string MemoryDetail
    {
        get => _memoryDetail;
        private set { _memoryDetail = value; OnPropertyChanged(); }
    }

    public int ProcessCount
    {
        get => _processCount;
        private set { _processCount = value; OnPropertyChanged(); }
    }

    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            _searchFilter = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public ProcessRowViewModel? SelectedProcess
    {
        get => _selectedProcess;
        set { _selectedProcess = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ProcessRowViewModel> FilteredProcesses
    {
        get => _filteredProcesses;
        private set { _filteredProcesses = value; OnPropertyChanged(); }
    }

    public TaskManagerViewModel()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var metrics = await _metricsService.GetStatusAsync();
            if (metrics.CpuUsagePercent.HasValue)
            {
                CpuUsage = $"{metrics.CpuUsagePercent.Value:0}%";
            }
            if (metrics.MemoryUsagePercent.HasValue)
            {
                MemoryUsage = $"{metrics.MemoryUsagePercent.Value:0}%";
            }
            MemoryDetail = metrics.Detail;

            var processes = await _processService.GetProcessesAsync();
            ProcessCount = processes.Count;

            var existingProcesses = _allProcesses.ToDictionary(p => p.Pid);
            foreach (var p in processes)
            {
                if (existingProcesses.TryGetValue(p.Pid, out var existing))
                {
                    existing.Update(p);
                    existingProcesses.Remove(p.Pid);
                }
                else
                {
                    _allProcesses.Add(new ProcessRowViewModel(p));
                }
            }
            foreach (var toRemove in existingProcesses.Values)
            {
                _allProcesses.Remove(toRemove);
            }

            ApplyFilter();
        }
        catch { }
    }

    public async Task EndProcessAsync(int pid)
    {
        await _processService.KillProcessAsync(pid);
        await RefreshAsync();
    }

    private void ApplyFilter()
    {
        var filter = SearchFilter.Trim();
        var selectedPid = SelectedProcess?.Pid;

        var filtered = _allProcesses.Where(p =>
                string.IsNullOrWhiteSpace(filter)
                || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || p.Pid.ToString().Contains(filter, StringComparison.Ordinal)
                || p.ExecutablePath.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var desiredPids = filtered.Select(process => process.Pid).ToHashSet();
        for (var index = _filteredProcesses.Count - 1; index >= 0; index--)
        {
            if (!desiredPids.Contains(_filteredProcesses[index].Pid))
                _filteredProcesses.RemoveAt(index);
        }
        for (var index = 0; index < filtered.Length; index++)
        {
            if (index < _filteredProcesses.Count && _filteredProcesses[index].Pid == filtered[index].Pid)
                continue;
            var existingIndex = _filteredProcesses.IndexOf(filtered[index]);
            if (existingIndex >= 0) _filteredProcesses.Move(existingIndex, index);
            else _filteredProcesses.Insert(index, filtered[index]);
        }

        if (selectedPid.HasValue)
        {
            SelectedProcess = _filteredProcesses.FirstOrDefault(p => p.Pid == selectedPid.Value);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void SetActive(bool active)
    {
        if (active)
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
                _ = RefreshAsync();
            }
        }
        else
        {
            _timer.Stop();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
