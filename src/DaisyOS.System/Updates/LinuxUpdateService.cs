using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Updates;

public sealed class LinuxUpdateService : IUpdateService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly string PacmanPath = "/usr/bin/pacman";
    private static readonly string[] RebootMarkers = ["/run/reboot-required", "/var/run/reboot-required"];

    private readonly ICommandRunner _runner;
    private readonly ILogService _log;
    private UpdateStatus? _cached;
    private readonly object _lock = new();

    public event EventHandler? UpdateStatusChanged;

    public LinuxUpdateService(ICommandRunner runner, ILogService? log = null)
    {
        _runner = runner;
        _log = log ?? NullLogService.Instance;
    }

    public async Task<UpdateStatus> CheckAsync(CancellationToken ct = default)
    {
        var status = await CheckInternalAsync(ct);
        lock (_lock)
        {
            _cached = status;
        }

        UpdateStatusChanged?.Invoke(this, EventArgs.Empty);
        return status;
    }

    public Task<UpdateStatus> GetCachedStatusAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_cached is not null)
                return Task.FromResult(_cached);
        }
        return CheckAsync(ct);
    }

    private async Task<UpdateStatus> CheckInternalAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rebootRequired = RebootMarkers.Any(File.Exists);

        if (!File.Exists(PacmanPath))
        {
            return new UpdateStatus(
                IsChecking: false,
                IsUpdating: false,
                IsRebootRequired: rebootRequired,
                HasError: true,
                ErrorMessage: "Updates aren’t available on this system.",
                PendingCount: 0,
                TotalDownloadSize: 0,
                PendingPackages: Array.Empty<UpdatePackage>(),
                LastChecked: now,
                Detail: "DaisyOS couldn’t find the system update service.");
        }

        var queryResult = await _runner.RunAsync(PacmanPath, ["-Qu"], Timeout, ct);
        if (!queryResult.Succeeded && queryResult.ExitCode != 1)
        {
            return new UpdateStatus(
                IsChecking: false,
                IsUpdating: false,
                IsRebootRequired: rebootRequired,
                HasError: true,
                ErrorMessage: "Couldn’t check for updates.",
                PendingCount: 0,
                TotalDownloadSize: 0,
                PendingPackages: Array.Empty<UpdatePackage>(),
                LastChecked: now,
                Detail: "Try again in a few minutes.");
        }

        var lines = queryResult.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0 || (lines.Length == 1 && string.IsNullOrWhiteSpace(lines[0])))
        {
            return new UpdateStatus(
                IsChecking: false,
                IsUpdating: false,
                IsRebootRequired: rebootRequired,
                HasError: false,
                ErrorMessage: string.Empty,
                PendingCount: 0,
                TotalDownloadSize: 0,
                PendingPackages: Array.Empty<UpdatePackage>(),
                LastChecked: now,
                Detail: "System is up to date.");
        }

        var packages = new List<UpdatePackage>();
        foreach (var line in lines)
        {
            var parsed = TryParsePackageLine(line);
            if (parsed is not null)
                packages.Add(parsed);
        }

        return new UpdateStatus(
            IsChecking: false,
            IsUpdating: false,
            IsRebootRequired: rebootRequired,
            HasError: false,
            ErrorMessage: string.Empty,
            PendingCount: packages.Count,
            TotalDownloadSize: 0, // TODO: Parse pacman -Qu output to calculate actual download size
            PendingPackages: packages,
            LastChecked: now,
            Detail: $"{packages.Count} package(s) available for update.");
    }

    internal static UpdatePackage? TryParsePackageLine(string line)
    {
        var arrow = line.IndexOf(" -> ", StringComparison.Ordinal);
        if (arrow < 0)
            return null;

        var nameAndCurrent = line[..arrow].Trim();
        var available = line[(arrow + 4)..].Trim();

        var spaceIdx = nameAndCurrent.LastIndexOf(' ');
        if (spaceIdx < 0)
            return null;

        var name = nameAndCurrent[..spaceIdx];
        var currentVersion = nameAndCurrent[(spaceIdx + 1)..];

        return new UpdatePackage(name, currentVersion, available, 0);
    }

}
