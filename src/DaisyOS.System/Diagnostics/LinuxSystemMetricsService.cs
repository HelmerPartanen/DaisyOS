using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using System.Globalization;

namespace DaisyOS.System.Diagnostics;

public sealed class LinuxSystemMetricsService : ISystemMetricsService
{
    private readonly object _syncRoot = new();
    private CpuSample? _previousCpuSample;

    public async Task<SystemMetricsStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var cpu = await ReadCpuSampleAsync(cancellationToken);
        var memory = await ReadMemorySampleAsync(cancellationToken);

        var cpuUsage = CalculateCpuUsage(cpu);
        double? memoryUsage = memory.TotalBytes <= 0
            ? null
            : Math.Clamp((double)memory.UsedBytes / memory.TotalBytes * 100, 0, 100);

        var detail = memory.TotalBytes <= 0
            ? "CPU and memory status are read from /proc."
            : $"Using {FormatBytes(memory.UsedBytes)} of {FormatBytes(memory.TotalBytes)} memory.";

        return new SystemMetricsStatus(
            cpuUsage,
            memoryUsage,
            memory.UsedBytes > 0 ? memory.UsedBytes : null,
            memory.TotalBytes > 0 ? memory.TotalBytes : null,
            detail);
    }

    private static async Task<CpuSample> ReadCpuSampleAsync(CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync("/proc/stat", cancellationToken);
        var cpuLine = lines.FirstOrDefault(line => line.StartsWith("cpu ", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(cpuLine))
        {
            return new CpuSample(0, 0);
        }

        var values = cpuLine
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
            .ToArray();

        if (values.Length < 4)
        {
            return new CpuSample(0, 0);
        }

        var idle = values[3] + (values.Length > 4 ? values[4] : 0);
        var total = values.Sum();
        return new CpuSample(idle, total);
    }

    private static async Task<MemorySample> ReadMemorySampleAsync(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in await File.ReadAllLinesAsync("/proc/meminfo", cancellationToken))
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var rawValue = parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kibibytes))
            {
                values[parts[0]] = kibibytes * 1024;
            }
        }

        var total = values.GetValueOrDefault("MemTotal");
        var available = values.GetValueOrDefault("MemAvailable");
        var used = total > 0 && available >= 0 ? total - available : 0;
        return new MemorySample(used, total);
    }

    private double? CalculateCpuUsage(CpuSample current)
    {
        CpuSample? previous;
        lock (_syncRoot)
        {
            previous = _previousCpuSample;
            _previousCpuSample = current;
        }

        if (previous is null)
        {
            return null;
        }

        var idleDelta = current.Idle - previous.Idle;
        var totalDelta = current.Total - previous.Total;
        if (totalDelta <= 0)
        {
            return null;
        }

        return Math.Clamp((1 - (double)idleDelta / totalDelta) * 100, 0, 100);
    }

    private static string FormatBytes(long bytes)
    {
        var gibibytes = bytes / 1024d / 1024d / 1024d;
        return $"{gibibytes:0.0} GB";
    }

    private sealed record CpuSample(long Idle, long Total);

    private sealed record MemorySample(long UsedBytes, long TotalBytes);
}
