namespace DaisyOS.Core.Models;

public sealed record AppPackageInfo(long DownloadBytes, long InstalledBytes, long RuntimeInstalledBytes,
    IReadOnlyList<string> Permissions, bool RuntimeEndOfLife);
