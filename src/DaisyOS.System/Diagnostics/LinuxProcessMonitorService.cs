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
    private readonly Dictionary<int, (long Start, long Read, long Write, long Time)> _previousIo = new();
    private readonly Dictionary<string, string> _users = ReadUsers();

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
                cancellationToken.ThrowIfCancellationRequested();

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
                    var cpuAvailable = false;
                    if (_previousProcessTicks.TryGetValue(pid, out var prev) && prev.Start == start)
                    {
                        var timeDiffSec = (now - prev.Time) / (double)Stopwatch.Frequency;
                        if (timeDiffSec > 0)
                        {
                            var tickDiff = totalTicks - prev.Ticks;
                            cpuAvailable = tickDiff >= 0;
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

                    double? readRate = null, writeRate = null;
                    try
                    {
                        var io = File.ReadLines(Path.Combine(dir, "io")).Select(l => l.Split(':', 2)).Where(p => p.Length == 2)
                            .ToDictionary(p => p[0], p => long.Parse(p[1].Trim(), CultureInfo.InvariantCulture));
                        if (_previousIo.TryGetValue(pid, out var old) && old.Start == start)
                        {
                            var seconds = (now - old.Time) / (double)Stopwatch.Frequency;
                            readRate = LinuxResourceMonitorService.Rate(io["read_bytes"], old.Read, seconds);
                            writeRate = LinuxResourceMonitorService.Rate(io["write_bytes"], old.Write, seconds);
                        }
                        _previousIo[pid] = (start, io["read_bytes"], io["write_bytes"], now);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or KeyNotFoundException) { _previousIo.Remove(pid); }
                    int.TryParse(rest[1], out var parent);
                    int.TryParse(rest[17], out var threads);
                    results.Add(new ProcessItemInfo(
                        pid,
                        comm,
                        exePath,
                        Math.Round(cpuPercent, 1),
                        memBytes,
                        _users.GetValueOrDefault(user, user)) { UserId = user, ParentPid = parent, ThreadCount = threads, CpuUsageAvailable = cpuAvailable, ReadBytesPerSecond = readRate, WriteBytesPerSecond = writeRate, StartTimeTicks = start, State = rest[0] switch { "R" => "Running", "S" => "Sleeping", "D" => "Disk wait", "T" or "t" => "Stopped", "Z" => "Zombie", "I" => "Idle", _ => rest[0] } });
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

            foreach (var dead in _previousIo.Keys.Where(k => !activePids.Contains(k)).ToArray()) _previousIo.Remove(dead);

            return (IReadOnlyList<ProcessItemInfo>)results
                .OrderByDescending(p => p.MemoryBytes)
                .ToList();
        }, cancellationToken);
    }

    private static Dictionary<string, string> ReadUsers()
    {
        try { return File.ReadLines("/etc/passwd").Select(l => l.Split(':')).Where(p => p.Length > 2).GroupBy(p => p[2]).ToDictionary(g => g.Key, g => g.First()[0]); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(); }
    }

    public async Task<ProcessDetails?> GetDetailsAsync(int pid, long startTimeTicks, CancellationToken cancellationToken = default)
    {
        try
        {
            async Task<bool> Matches()
            {
                var stat = await File.ReadAllTextAsync($"/proc/{pid}/stat", cancellationToken);
                var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return fields.Length > 19 && long.TryParse(fields[19], out var start) && start == startTimeTicks;
            }
            if (startTimeTicks <= 0 || !await Matches()) return null;
            string command;
            try { command = (await File.ReadAllTextAsync($"/proc/{pid}/cmdline", cancellationToken)).Replace('\0', ' ').Trim(); }
            catch (UnauthorizedAccessException) { command = "Permission denied"; }
            var uptime = double.Parse((await File.ReadAllTextAsync("/proc/uptime", cancellationToken)).Split(' ')[0], CultureInfo.InvariantCulture);
            if (!await Matches()) return null;
            return new(pid, startTimeTicks, command.Length == 0 ? "Not available for this process" : command,
                DateTimeOffset.Now - TimeSpan.FromSeconds(Math.Max(0, uptime - startTimeTicks / (double)ClockTicks)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException) { return null; }
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
