namespace DaisyOS.Core.Models;

public sealed record UpdatePackage(
    string Name,
    string CurrentVersion,
    string AvailableVersion,
    long DownloadSize);

public sealed record UpdateStatus(
    bool IsChecking,
    bool IsUpdating,
    bool IsRebootRequired,
    bool HasError,
    string ErrorMessage,
    int PendingCount,
    long TotalDownloadSize,
    IReadOnlyList<UpdatePackage> PendingPackages,
    DateTimeOffset LastChecked,
    string Detail);

public sealed record UpdateCheckRecord(
    DateTimeOffset CheckedAt,
    int PendingCount,
    bool Succeeded,
    bool RebootRequired,
    string Summary);
