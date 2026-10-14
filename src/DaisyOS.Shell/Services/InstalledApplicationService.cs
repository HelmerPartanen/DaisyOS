using System.Text.RegularExpressions;
using DaisyOS.Core.Models;
using DaisyOS.Shell.ViewModels;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Services;

public enum InstalledApplicationPackageKind
{
    None,
    Pacman,
    Flatpak
}

public sealed record InstalledApplicationManagement(
    AppEntry App,
    string? PackageId,
    InstalledApplicationPackageKind PackageKind,
    string? DesktopEntryPath,
    string UnavailableReason)
{
    public bool CanManage => PackageKind != InstalledApplicationPackageKind.None && !string.IsNullOrWhiteSpace(PackageId);
}

/// <summary>Resolves visible desktop applications to their real Linux package owner.</summary>
public sealed partial class InstalledApplicationService
{
    private readonly SafeCommandRunner _runner = new();

    public IReadOnlyList<AppEntry> GetInstalledApplications() =>
        new LinuxAppLauncherService().GetAvailableApps()
            .Select(app => app with
            {
                Name = app.LaunchKind == AppLaunchKind.Internal || app.Id.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase) ? Loc.Get(app.Name) : app.Name,
                Description = app.LaunchKind == AppLaunchKind.Internal || app.Id.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase) || app.Description == "Desktop application" ? Loc.Get(app.Description) : app.Description
            })
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public async Task<InstalledApplicationManagement> ResolveAsync(AppEntry app, CancellationToken cancellationToken = default)
    {
        if (app.LaunchKind == AppLaunchKind.Internal)
        {
            return new(app, null, InstalledApplicationPackageKind.None, null,
                Loc.Get("DaisyOS system applications are managed with system updates."));
        }

        var desktopPath = DesktopItemLoader.FindDesktopEntryFilePath([app.Id, app.Name, app.Icon]);
        var flatpakId = app.Id.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? app.Id[..^8] : app.Id;
        var flatpak = await _runner.RunAsync("flatpak", ["info", flatpakId], TimeSpan.FromSeconds(4), cancellationToken);
        if (flatpak.Succeeded)
        {
            return new(app, flatpakId, InstalledApplicationPackageKind.Flatpak, desktopPath, string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(desktopPath))
        {
            var owner = await _runner.RunAsync("pacman", ["-Qo", desktopPath], TimeSpan.FromSeconds(4), cancellationToken);
            var match = PacmanOwnerRegex().Match(owner.StandardOutput);
            if (owner.Succeeded && match.Success)
            {
                return new(app, match.Groups["package"].Value, InstalledApplicationPackageKind.Pacman, desktopPath, string.Empty);
            }
        }

        return new(app, null, InstalledApplicationPackageKind.None, desktopPath,
            Loc.Get("This application is not owned by a supported Pacman or Flatpak package."));
    }

    public async Task<bool> RepairAsync(InstalledApplicationManagement target, CancellationToken cancellationToken = default)
    {
        if (!target.CanManage) return false;
        var result = target.PackageKind == InstalledApplicationPackageKind.Flatpak
            ? await _runner.RunAsync("flatpak", ["install", "-y", "--noninteractive", "flathub", target.PackageId!], TimeSpan.FromMinutes(10), cancellationToken)
            : await _runner.RunAsync("pkexec", ["pacman", "-S", "--noconfirm", target.PackageId!], TimeSpan.FromMinutes(5), cancellationToken);
        return result.Succeeded;
    }

    public async Task<bool> UninstallAsync(InstalledApplicationManagement target, CancellationToken cancellationToken = default)
    {
        if (!target.CanManage) return false;
        var result = target.PackageKind == InstalledApplicationPackageKind.Flatpak
            ? await _runner.RunAsync("flatpak", ["uninstall", "-y", "--noninteractive", target.PackageId!], TimeSpan.FromMinutes(5), cancellationToken)
            : await _runner.RunAsync("pkexec", ["pacman", "-R", "--noconfirm", target.PackageId!], TimeSpan.FromMinutes(3), cancellationToken);
        return result.Succeeded;
    }

    public string? AddToDesktop(AppEntry app)
    {
        var source = DesktopItemLoader.FindDesktopEntryFilePath([app.Id, app.Name, app.Icon]);
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return null;
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
        Directory.CreateDirectory(desktop);
        var destination = Path.Combine(desktop, Path.GetFileName(source));
        File.Copy(source, destination, overwrite: true);
        try
        {
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(destination, File.GetUnixFileMode(destination) | UnixFileMode.UserExecute);
            }
        }
        catch (PlatformNotSupportedException) { }
        return destination;
    }

    [GeneratedRegex(@"\sis owned by\s(?<package>[^\s]+)\s", RegexOptions.IgnoreCase)]
    private static partial Regex PacmanOwnerRegex();
}
