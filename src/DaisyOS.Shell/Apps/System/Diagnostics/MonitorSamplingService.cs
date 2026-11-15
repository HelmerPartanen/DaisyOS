using DaisyOS.Core.Models;
using DaisyOS.System.Diagnostics;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

public sealed record MonitorSample(MonitorResourceSnapshot Resources, IReadOnlyList<ProcessItemInfo>? Processes, string? ProcessError);

/// <summary>Owns stateful samplers; presentation only receives immutable snapshots.</summary>
public interface IMonitorSamplingService
{
    Task<MonitorSample> SampleAsync(CancellationToken token);
    Task<ProcessDetails?> GetDetailsAsync(int pid, long start, CancellationToken token);
    Task<bool> EndInstanceAsync(int pid, long start, bool force, CancellationToken token);
}

public sealed class MonitorSamplingService : IMonitorSamplingService
{
    private readonly LinuxResourceMonitorService _resources = new();
    public LinuxProcessMonitorService Processes { get; } = new();
    public Task<ProcessDetails?> GetDetailsAsync(int pid, long start, CancellationToken token) => Processes.GetDetailsAsync(pid, start, token);
    public Task<bool> EndInstanceAsync(int pid, long start, bool force, CancellationToken token) => Processes.EndInstanceAsync(pid, start, force, token);
    public async Task<MonitorSample> SampleAsync(CancellationToken token)
    {
        // Sources run serially on a worker; the owner prevents overlapping samples.
        return await Task.Run(async () =>
        {
            var resources = _resources.Sample();
            try { return new MonitorSample(resources, await Processes.GetProcessesAsync(token), null); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(resources, null, "Process list unavailable · Showing last sample"); }
        }, token);
    }
}

public sealed record HistoryPoint(DateTimeOffset Time, double? Value);

public static class MonitorHistory
{
    public static HistoryPoint[] Append(HistoryPoint[] history, DateTimeOffset time, double? value) =>
        history.Where(p => p.Time > time.AddMinutes(-2) && p.Time < time).Append(new(time, value)).TakeLast(240).ToArray();
}
