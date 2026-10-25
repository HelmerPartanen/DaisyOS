using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaisyOS.Core.Models;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Diagnostics;

public sealed class LinuxProcessMonitorService
{
    [DllImport("libc", EntryPoint = "sysconf")]
    private static extern long Sysconf(int name);
    private static readonly long ClockTicks = OperatingSystem.IsLinux() ? Math.Max(1, Sysconf(2)) : 100;
    private static readonly long PageSize = Environment.SystemPageSize;
    private readonly Dictionary<int, (long Ticks, long Start, long Time)> _previousProcessTicks = new();
    private readonly ICommandRunner _commandRunner;

    public LinuxProcessMonitorService(ICommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new SafeCommandRunner();
    }

    public async Task<IReadOnlyList<ProcessItemInfo>> GetProcessesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<ProcessItemInfo>();
            var procDirs = Directory.GetDirectories("/proc");
            var now = Stopwatch.GetTimestamp();

            foreach (var dir in procDirs)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var name = Path.GetFileName(dir);
                if (!int.TryParse(name, out var pid) || pid <= 0) continue;

                try
                {
                    var statPath = Path.Combine(dir, "stat");
                    if (!File.Exists(statPath)) continue;

                    var statContent = File.ReadAllText(statPath);
                    var openParen = statContent.IndexOf('(');
                    var closeParen = statContent.LastIndexOf(')');
                    if (openParen < 0 || closeParen <= openParen) continue;

                    var comm = statContent.Substring(openParen + 1, closeParen - openParen - 1);
                    var rest = statContent[(closeParen + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (rest.Length < 22) continue;

                    long.TryParse(rest[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out var utime);
                    long.TryParse(rest[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out var stime);
                    long.TryParse(rest[21], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rssPages);

                    long.TryParse(rest[19], out var start);
                    var totalTicks = utime + stime;
                    var memBytes = rssPages * PageSize;

                    double cpuPercent = 0;
                    if (_previousProcessTicks.TryGetValue(pid, out var prev) && prev.Start == start)
                    {
                        var timeDiffSec = (now - prev.Time) / (double)Stopwatch.Frequency;
                        if (timeDiffSec > 0)
                        {
                            var tickDiff = totalTicks - prev.Ticks;
                            cpuPercent = Math.Max((tickDiff / (double)ClockTicks) / timeDiffSec * 100.0 / LinuxSystemMetricsService.LogicalProcessorCount, 0);
                        }
                    }
                    _previousProcessTicks[pid] = (totalTicks, start, now);



                    string exePath = comm;
                    try { exePath = File.ResolveLinkTarget(Path.Combine(dir, "exe"), true)?.FullName ?? comm; } catch { }
                    var user = "Unknown";
                    try
                    {
                        var uid = File.ReadLines(Path.Combine(dir, "status")).FirstOrDefault(l => l.StartsWith("Uid:"));
                        if (uid is not null) user = uid.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[1];
                    }
                    catch { }

                    results.Add(new ProcessItemInfo(
                        pid,
                        comm,
                        exePath,
                        Math.Round(cpuPercent, 1),
                        memBytes,
                        user) { StartTimeTicks = start, State = rest[0] switch { "R" => "Running", "S" => "Sleeping", "D" => "Disk wait", "T" or "t" => "Stopped", "Z" => "Zombie", "I" => "Idle", _ => rest[0] } });
                }
                catch
                {
                    // Process exited during read
                }
            }

            var activePids = new HashSet<int>(results.Select(r => r.Pid));
            var deadPids = _previousProcessTicks.Keys.Where(k => !activePids.Contains(k)).ToList();
            foreach (var dead in deadPids)
            {
                _previousProcessTicks.Remove(dead);
            }

            return (IReadOnlyList<ProcessItemInfo>)results
                .OrderByDescending(p => p.MemoryBytes)
                .ToList();
        }, cancellationToken);
    }

    public async Task<bool> EndInstanceAsync(int pid, long startTimeTicks, bool force = false, CancellationToken cancellationToken = default)
    {
        if (pid <= 1 || pid == Environment.ProcessId || startTimeTicks <= 0) return false;
        try
        {
            var stat = await File.ReadAllTextAsync($"/proc/{pid}/stat", cancellationToken);
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 20 || !long.TryParse(fields[19], out var start) || start != startTimeTicks) return false;
            return await KillProcessAsync(pid, force, cancellationToken);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public async Task<bool> KillProcessAsync(int pid, bool force = false, CancellationToken cancellationToken = default)
    {
        if (pid <= 1 || pid == Environment.ProcessId) return false;

        var signal = force ? "-9" : "-15";
        var result = await _commandRunner.RunAsync("kill", [signal, pid.ToString()], TimeSpan.FromSeconds(2), cancellationToken);
        return result.Succeeded;
    }
}
