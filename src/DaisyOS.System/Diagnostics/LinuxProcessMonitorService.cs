using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaisyOS.Core.Models;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Diagnostics;

public sealed class LinuxProcessMonitorService
{
    private static readonly long PageSize = Environment.SystemPageSize;
    private readonly Dictionary<int, (long Ticks, DateTime Time)> _previousProcessTicks = new();
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
            var now = DateTime.UtcNow;

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

                    var totalTicks = utime + stime;
                    var memBytes = rssPages * PageSize;

                    double cpuPercent = 0;
                    if (_previousProcessTicks.TryGetValue(pid, out var prev))
                    {
                        var timeDiffSec = (now - prev.Time).TotalSeconds;
                        if (timeDiffSec > 0)
                        {
                            var tickDiff = totalTicks - prev.Ticks;
                            cpuPercent = Math.Max((tickDiff / 100.0) / timeDiffSec * 100.0, 0);
                        }
                    }
                    _previousProcessTicks[pid] = (totalTicks, now);

                    if (memBytes <= 0 && pid > 2) continue;

                    var cmdlinePath = Path.Combine(dir, "cmdline");
                    string exePath = comm;
                    if (File.Exists(cmdlinePath))
                    {
                        try
                        {
                            var cmd = File.ReadAllText(cmdlinePath).Replace('\0', ' ').Trim();
                            if (!string.IsNullOrWhiteSpace(cmd))
                            {
                                var firstWord = cmd.Split(' ')[0];
                                var binName = Path.GetFileName(firstWord);
                                if (!string.IsNullOrWhiteSpace(binName))
                                {
                                    exePath = binName;
                                }
                            }
                        }
                        catch { }
                    }

                    results.Add(new ProcessItemInfo(
                        pid,
                        comm,
                        exePath,
                        Math.Round(cpuPercent, 1),
                        memBytes,
                        "User"));
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

    public async Task<bool> KillProcessAsync(int pid, bool force = false, CancellationToken cancellationToken = default)
    {
        if (pid <= 1 || pid == Environment.ProcessId) return false;

        var signal = force ? "-9" : "-15";
        var result = await _commandRunner.RunAsync("kill", [signal, pid.ToString()], TimeSpan.FromSeconds(2), cancellationToken);
        if (!result.Succeeded && !force)
        {
            result = await _commandRunner.RunAsync("kill", ["-9", pid.ToString()], TimeSpan.FromSeconds(2), cancellationToken);
        }
        return result.Succeeded;
    }
}
