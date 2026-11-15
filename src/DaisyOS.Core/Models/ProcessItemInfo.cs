namespace DaisyOS.Core.Models;

public sealed record ProcessItemInfo(
    int Pid,
    string Name,
    string ExecutablePath,
    double CpuPercent,
    long MemoryBytes,
    string User)
{
    public int ParentPid { get; init; }
    public int ThreadCount { get; init; }
    public string UserId { get; init; } = "";
    public bool CpuUsageAvailable { get; init; } = true;
    public double? ReadBytesPerSecond { get; init; }
    public double? WriteBytesPerSecond { get; init; }
    public long StartTimeTicks { get; init; }
    public string State { get; init; } = "Unknown";
}
