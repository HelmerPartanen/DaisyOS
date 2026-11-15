using System.ComponentModel;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.ViewModels;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

public static class MonitorFormat
{
    public static string Bytes(double value) => value >= 1073741824 ? $"{value / 1073741824:0.0} GiB" : value >= 1048576 ? $"{value / 1048576:0.0} MiB" : value >= 1024 ? $"{value / 1024:0.0} KiB" : $"{value:0} B";
    public static string Rate(double? value) => value is { } rate ? Bytes(rate) + "/s" : "—";
    public static string Percent(double? value) => value is { } number ? $"{number:0.0}%" : "—";
}

public sealed class ProcessRowViewModel(ProcessItemInfo info, long totalMemory = 0) : INotifyPropertyChanged
{
    public IImage? ApplicationIcon { get; set; }
    public ProcessItemInfo Info { get; private set; } = info;
    private long _totalMemory = totalMemory;
    public string Key => $"{Pid}:{StartTimeTicks}";
    public int Pid => Info.Pid;
    public long StartTimeTicks => Info.StartTimeTicks;
    public string Name => Info.Name;
    public string ExecutablePath => Info.ExecutablePath;
    public string User => Info.User;
    public string State => Info.State;
    public int ParentPid => Info.ParentPid;
    public int Threads => Info.ThreadCount;
    public double CpuPercent => Info.CpuPercent;
    public long MemoryBytes => Info.MemoryBytes;
    public double MemoryPercent => _totalMemory > 0 ? MemoryBytes * 100d / _totalMemory : 0;
    public string CpuText => MonitorFormat.Percent(Info.CpuUsageAvailable ? CpuPercent : null);
    public string MemoryText => MonitorFormat.Bytes(MemoryBytes);
    public string ReadText => MonitorFormat.Rate(Info.ReadBytesPerSecond);
    public string WriteText => MonitorFormat.Rate(Info.WriteBytesPerSecond);
    public string Detail => $"PID {Pid} · {State}";
    public int Depth { get; set; }
    public Thickness Indent => new(Math.Min(Depth, 12) * 16, 0, 0, 0);
    public bool HasChildren { get; set; }
    public string ExpandIcon { get; set; } = "caret-down";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(ProcessRowViewModel value)
    {
        Info = value.Info; _totalMemory = value._totalMemory; ApplicationIcon = value.ApplicationIcon;
        Depth = value.Depth; HasChildren = value.HasChildren; ExpandIcon = value.ExpandIcon;
        PropertyChanged?.Invoke(this, new(null));
    }
    public static string FormatBytes(double value) => MonitorFormat.Bytes(value);
}

public sealed class WindowRowViewModel(CompositorWindowRecord window, IImage? icon = null) : INotifyPropertyChanged
{
    public IImage? ApplicationIcon { get; private set; } = icon;
    public CompositorWindowRecord Window { get; private set; } = window;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(CompositorWindowRecord value, IImage? icon) { Window = value; ApplicationIcon = icon; PropertyChanged?.Invoke(this, new(null)); }
    public string Title => Window.DisplayName;
    public string Detail => $"Workspace {Window.Workspace} · {(Window.IsUnresponsive ? "Not responding" : Window.IsMinimized ? "Minimized" : "Open")}";
    public bool CanClose => Window.CanClose;
}

public sealed class ApplicationRowViewModel : INotifyPropertyChanged
{
    private bool _expanded;
    public bool IsExpanded { get => _expanded; set { _expanded = value; PropertyChanged?.Invoke(this, new(null)); } }
    public string ExpandIcon => IsExpanded ? "caret-down" : "caret-right";
    public required ApplicationGroup Group { get; set; }
    public required string Name { get; init; }
    public IImage? ApplicationIcon { get; set; }
    public bool HasApplicationIcon => ApplicationIcon is not null;
    public string Key => Group.Key;
    public string Summary => $"{Group.Windows.Count} {(Group.Windows.Count == 1 ? "window" : "windows")} · {Group.Processes.Count} attributable {(Group.Processes.Count == 1 ? "process" : "processes")}";
    public double CpuPercent => Group.Cpu;
    public long MemoryBytes => Group.Memory;
    public string CpuText => MonitorFormat.Percent(Group.HasUsage && Group.Processes.All(p => p.CpuUsageAvailable) ? Group.Cpu : null);
    public string MemoryText => Group.HasUsage ? MonitorFormat.Bytes(Group.Memory) : "—";
    public string Attribution => Group.Attribution;
    public ObservableCollection<WindowRowViewModel> Windows { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(ApplicationGroup group)
    {
        Group = group;
        var ids = group.Windows.Select(w => w.Id).ToHashSet();
        for (var i = Windows.Count - 1; i >= 0; i--) if (!ids.Contains(Windows[i].Window.Id)) Windows.RemoveAt(i);
        foreach (var window in group.Windows)
        {
            var row = Windows.FirstOrDefault(w => w.Window.Id == window.Id);
            if (row is not null) row.Update(window, ApplicationIcon); else Windows.Add(new(window, ApplicationIcon));
        }
        PropertyChanged?.Invoke(this, new(null));
    }
}

public sealed class TopUsageRow(string name, string detail, string value, string? appKey, int? pid, long? startTimeTicks = null) : INotifyPropertyChanged
{
    public IImage? ApplicationIcon { get; set; }
    public string Name { get; private set; } = name;
    public string Detail { get; private set; } = detail;
    public string Value { get; private set; } = value;
    public string? AppKey { get; } = appKey;
    public int? Pid { get; } = pid;
    public long? StartTimeTicks { get; } = startTimeTicks;
    public string Key => AppKey ?? $"{Pid}:{StartTimeTicks}";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(TopUsageRow row) { ApplicationIcon = row.ApplicationIcon; Name = row.Name; Detail = row.Detail; Value = row.Value; PropertyChanged?.Invoke(this, new(null)); }
}
public sealed class ResourceContributor(string name, string value, int pid, long startTimeTicks) : INotifyPropertyChanged
{
    public IImage? ApplicationIcon { get; set; }
    public string Name { get; private set; } = name;
    public string Value { get; private set; } = value;
    public int Pid { get; } = pid;
    public long StartTimeTicks { get; } = startTimeTicks;
    public string Key => $"{Pid}:{StartTimeTicks}";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(ResourceContributor row) { ApplicationIcon = row.ApplicationIcon; Name = row.Name; Value = row.Value; PropertyChanged?.Invoke(this, new(null)); }
}
public sealed class CoreUsageRow(string name, double? usage, HistoryPoint[] samples, DateTimeOffset endTime) : INotifyPropertyChanged
{
    public string Name { get; } = name;
    public double? Usage { get; private set; } = usage;
    public HistoryPoint[] Samples { get; private set; } = samples;
    public DateTimeOffset EndTime { get; private set; } = endTime;
    public string Value => MonitorFormat.Percent(Usage);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(CoreUsageRow row) { Usage = row.Usage; Samples = row.Samples; EndTime = row.EndTime; PropertyChanged?.Invoke(this, new(null)); }
}
public sealed class DetailRow(string label, string value) : INotifyPropertyChanged
{
    public string Label { get; } = label;
    public string Value { get; private set; } = value;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(DetailRow row) { if (Value == row.Value) return; Value = row.Value; PropertyChanged?.Invoke(this, new(nameof(Value))); }
}
