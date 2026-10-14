namespace DaisyOS.Core.Models;

public sealed record SystemMetricsStatus(
    double? CpuUsagePercent,
    double? MemoryUsagePercent,
    long? MemoryUsedBytes,
    long? MemoryTotalBytes,
    string Detail);
