namespace DaisyOS.Core.Models;

public sealed record ProcessItemInfo(
    int Pid,
    string Name,
    string ExecutablePath,
    double CpuPercent,
    long MemoryBytes,
    string User);
