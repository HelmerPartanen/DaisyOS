using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Compositor;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

public sealed record ApplicationGroup(string Key, string Identity, IReadOnlyList<CompositorWindowRecord> Windows,
    IReadOnlyList<ProcessItemInfo> Processes, bool HasSharedProcesses, bool HasMissingProcesses)
{
    public double Cpu => Processes.Sum(p => p.CpuPercent);
    public long Memory => Processes.Sum(p => p.MemoryBytes);
    public bool HasUsage => Processes.Count > 0;
    public string Attribution => !HasUsage ? "Usage unavailable · Owning processes are shared or inaccessible"
        : "Attributed usage · " + Processes.Count + (Processes.Count == 1 ? " process" : " processes") +
          (HasSharedProcesses || HasMissingProcesses ? " · Partial attribution" : " · Based on window owners and their descendants");
}

/// <summary>Each process belongs to at most one app. Shared hosts form ownership boundaries.</summary>
public static class ApplicationAttribution
{
    public static string Identity(CompositorWindowRecord window)
    {
        var value = new[] { window.DaisyAppId, window.DesktopFileId, window.AppId }.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (value is null) return "window:" + window.Id;
        value = Path.GetFileName(value.Trim()).ToLowerInvariant();
        if (value.EndsWith(".desktop", StringComparison.Ordinal)) value = value[..^8];
        if (value.StartsWith("org.daisyos.", StringComparison.Ordinal)) value = value[12..];
        return value;
    }

    public static IReadOnlyList<ApplicationGroup> Group(IEnumerable<CompositorWindowRecord> windows, IReadOnlyList<ProcessItemInfo> processes)
    {
        var groups = windows.GroupBy(Identity).ToDictionary(g => g.Key, g => g.ToArray());
        var seeds = groups.SelectMany(g => g.Value.Where(w => w.ProcessId > 1).Select(w => (w.ProcessId, g.Key)))
            .GroupBy(x => x.ProcessId).ToDictionary(g => g.Key, g => g.Select(x => x.Key).Distinct().ToArray());
        var byPid = processes.ToDictionary(p => p.Pid);
        var members = groups.Keys.ToDictionary(k => k, _ => new List<ProcessItemInfo>());
        foreach (var process in processes)
        {
            var current = process;
            var visited = new HashSet<int>();
            while (visited.Add(current.Pid))
            {
                if (seeds.TryGetValue(current.Pid, out var owners))
                {
                    // The desktop hosts embedded apps; its descendants are not app-owned workers.
                    var host = current.Pid == Environment.ProcessId || Path.GetFileName(current.ExecutablePath).Equals("DaisyOS.Shell", StringComparison.OrdinalIgnoreCase);
                    if (owners.Length == 1 && (current.Pid == process.Pid || !host)) members[owners[0]].Add(process);
                    break;
                }
                if (!byPid.TryGetValue(current.ParentPid, out var parent) || parent.StartTimeTicks > current.StartTimeTicks || parent.Pid <= 1) break;
                current = parent;
            }
        }
        return groups.Select(g => new ApplicationGroup(g.Key, g.Key, g.Value, members[g.Key],
            g.Value.Any(w => seeds.TryGetValue(w.ProcessId, out var owners) && owners.Length > 1),
            g.Value.Any(w => !byPid.ContainsKey(w.ProcessId)))).ToArray();
    }
}
