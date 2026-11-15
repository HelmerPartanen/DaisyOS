namespace DaisyOS.System.Diagnostics;

public sealed record CpuResource(string Model, double? Usage, IReadOnlyList<double?> LogicalProcessors, string LoadAverage)
{
    public IReadOnlyList<string> LogicalProcessorNames { get; init; } = [];
}
public sealed record MemoryResource(long Total, long Available, long Cache, long SwapTotal, long SwapUsed, double? Pressure)
{
    public long Used => Math.Max(0, Total - Available);
    public double Usage => Total > 0 ? Used * 100d / Total : 0;
}
public sealed record DiskResource(string Name, double? ReadRate, double? WriteRate, double? Activity);
public sealed record NetworkResource(string Name, string State, long Received, long Sent, double? ReceiveRate, double? SendRate);
public sealed record MonitorResourceSnapshot(DateTimeOffset Timestamp, CpuResource? Cpu, MemoryResource? Memory,
    IReadOnlyList<DiskResource>? Disks, IReadOnlyList<NetworkResource>? Networks, TimeSpan? Uptime, IReadOnlyList<string> Errors);
public sealed record ProcessDetails(int Pid, long StartTimeTicks, string CommandLine, DateTimeOffset? StartedAt);
