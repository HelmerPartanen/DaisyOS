using DaisyOS.System.AppStore;
using DaisyOS.Core.Models;
using DaisyOS.Shell.ViewModels;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Services;

public enum InstalledApplicationPackageKind
{
    None,
    Pacman,
    Flatpak,
    DesktopEntry
}

public sealed record InstalledApplicationManagement(
    AppEntry App,
    string? PackageId,
    InstalledApplicationPackageKind PackageKind,
    string? DesktopEntryPath,
    string UnavailableReason)
{
    public bool CanManage => PackageKind != InstalledApplicationPackageKind.None && !string.IsNullOrWhiteSpace(PackageId);
    public string InstallationScope { get; init; } = "user";
    public string Origin { get; init; } = "flathub";
}

/// <summary>Resolves visible desktop applications to their real Linux package owner or user-level desktop entry.</summary>
public sealed class InstalledApplicationService
{
    private readonly ICommandRunner _runner;

    public InstalledApplicationService(ICommandRunner? runner = null) => _runner = runner ?? new SafeCommandRunner();

    public IReadOnlyList<AppEntry> GetInstalledApplications() =>
        new LinuxAppLauncherService().GetAvailableApps()
            .Select(app => app with
            {
                Name = app.LaunchKind == AppLaunchKind.Internal || app.Id.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase) ? Loc.Get(app.Name) : app.Name,
                Description = app.LaunchKind == AppLaunchKind.Internal || app.Id.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase) || app.Description == "Desktop application" ? Loc.Get(app.Description) : app.Description
            })
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public IReadOnlyList<(string DesktopId, string Name, string Icon)> GetRemovedApplications() =>
        DesktopApplicationMaskStore.GetRemovedApplications();

    public bool RestoreRemovedApplication(string desktopId)
    {
        var restored = DesktopApplicationMaskStore.Restore(desktopId);
        if (restored)
        {
            (Avalonia.Application.Current as App)?.NotifyApplicationRestored(desktopId);
        }
        return restored;
    }

    public async Task<InstalledApplicationManagement> ResolveAsync(AppEntry app, CancellationToken cancellationToken = default)
    {
        string? desktopPath = null;
        if (app.LaunchKind != AppLaunchKind.Internal)
        {
            desktopPath = DesktopItemLoader.FindDesktopEntryFilePath([app.Id, app.Name, app.Icon]);
            var flatpakId = app.Id.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? app.Id[..^8] : app.Id;
            var flatpak = await _runner.RunAsync("flatpak", ["list", "--app", "--columns=application,installation,origin"], TimeSpan.FromSeconds(4), cancellationToken);
            var installation = flatpak.Succeeded ? flatpak.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('\t'))
                .Where(parts => parts.Length == 3 && parts[0] == flatpakId && !string.IsNullOrWhiteSpace(parts[1]))
                .OrderBy(parts => parts[1] == "user" ? 0 : 1).FirstOrDefault() : null;
            if (installation != null)
            {
                return new(app, flatpakId, InstalledApplicationPackageKind.Flatpak, desktopPath, string.Empty)
                    { InstallationScope = installation[1].Trim(), Origin = installation[2].Trim() };
            }

            if (!string.IsNullOrWhiteSpace(desktopPath))
            {
                var package = await NativeApplicationPackages.FindOwnerAsync(_runner, desktopPath, cancellationToken);
                if (package == null && !desktopPath.StartsWith("/usr/share/applications/", StringComparison.Ordinal))
                {
                    var systemDesktop = Path.Combine("/usr/share/applications", Path.GetFileName(desktopPath));
                    if (File.Exists(systemDesktop))
                    {
                        package = await NativeApplicationPackages.FindOwnerAsync(_runner, systemDesktop, cancellationToken);
                    }
                }
                if (package != null)
                {
                    return new(app, package, InstalledApplicationPackageKind.Pacman, desktopPath, string.Empty);
                }
            }
        }

        var normalizedId = DesktopApplicationMaskStore.NormalizeDesktopId(app.Id);
        if (!string.IsNullOrWhiteSpace(normalizedId))
        {
            var isDaisy = app.LaunchKind == AppLaunchKind.Internal || normalizedId.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase);
            var reason = isDaisy ? Loc.Get("DaisyOS system applications are managed with system updates.") : string.Empty;
            return new(app, normalizedId, InstalledApplicationPackageKind.DesktopEntry, desktopPath, reason)
                { InstallationScope = "user", Origin = isDaisy ? "daisyos" : "local" };
        }

        return new(app, null, InstalledApplicationPackageKind.None, desktopPath,
            Loc.Get("This application is not owned by a supported Pacman or Flatpak package."));
    }

    public async Task<bool> RepairAsync(InstalledApplicationManagement target, CancellationToken cancellationToken = default)
    {
        if (!target.CanManage) return false;
        if (target.PackageKind == InstalledApplicationPackageKind.DesktopEntry)
        {
            DesktopApplicationMaskStore.Restore(target.PackageId!);
            (Avalonia.Application.Current as App)?.NotifyApplicationRestored(target.App.Id);
            return true;
        }
        var result = target.PackageKind == InstalledApplicationPackageKind.Flatpak
            ? await _runner.RunAsync("flatpak", ["install", ScopeArgument(target.InstallationScope), "--reinstall", "-y", "--noninteractive", "--", target.Origin, target.PackageId!], TimeSpan.FromMinutes(10), cancellationToken)
            : await _runner.RunAsync("pkexec", ["pacman", "-S", "--noconfirm", "--", target.PackageId!], TimeSpan.FromMinutes(5), cancellationToken);
        return result.Succeeded;
    }

    public async Task<bool> UninstallAsync(InstalledApplicationManagement target, CancellationToken cancellationToken = default)
    {
        if (!target.CanManage) return false;
        bool succeeded;
        if (target.PackageKind == InstalledApplicationPackageKind.DesktopEntry)
        {
            succeeded = DesktopApplicationMaskStore.RemoveOrMask(target.PackageId!, target.DesktopEntryPath, target.App.Name, target.App.Icon);
        }
        else
        {
            var result = target.PackageKind == InstalledApplicationPackageKind.Flatpak
                ? await _runner.RunAsync("flatpak", ["uninstall", ScopeArgument(target.InstallationScope), "-y", "--noninteractive", "--", target.PackageId!], TimeSpan.FromMinutes(5), cancellationToken)
                : await NativeApplicationPackages.RemoveAsync(_runner, target.PackageId!, cancellationToken);
            succeeded = result.Succeeded;
            if (succeeded)
            {
                DesktopApplicationMaskStore.RemoveDesktopShortcut(target.App.Id);
            }
        }

        if (succeeded)
        {
            (Avalonia.Application.Current as App)?.NotifyApplicationRemoved(target.App.Id);
        }
        return succeeded;
    }

    public string? AddToDesktop(AppEntry app)
    {
        var desktop = ViewModels.DesktopViewModel.GetDesktopDirectoryPath();
        return AddToDesktop(app, desktop);
    }

    internal string? AddToDesktop(AppEntry app, string desktop)
    {
        var source = DesktopItemLoader.FindDesktopEntryFilePath([app.Id, app.Name, app.Icon]);
        var hasSource = !string.IsNullOrWhiteSpace(source) && File.Exists(source);
        if (!hasSource && app.LaunchKind != AppLaunchKind.Internal) return null;
        Directory.CreateDirectory(desktop);
        var desktopId = DesktopApplicationMaskStore.NormalizeDesktopId(app.Id);
        var destination = Path.Combine(desktop, hasSource ? Path.GetFileName(source!) : desktopId);
        if (hasSource)
        {
            File.Copy(source!, destination, overwrite: true);
        }
        else
        {
            // A few built-in apps are synthesized into the launcher and do not
            // ship a standalone entry. They still need a launchable desktop pin.
            var shortId = DesktopApplicationMaskStore.NormalizeShortId(app.Id);
            var name = DesktopEntryValue(app.Name);
            var comment = DesktopEntryValue(app.Description);
            var icon = DesktopEntryValue(app.Icon);
            File.WriteAllText(destination,
                $"[Desktop Entry]\nType=Application\nName={name}\nComment={comment}\nExec=daisyos-shell --open-app={shortId}\nIcon={icon}\nTerminal=false\n");
        }
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

    private static string DesktopEntryValue(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", string.Empty, StringComparison.Ordinal);

    private static string ScopeArgument(string scope) => scope is "user" or "system" ? "--" + scope : "--installation=" + scope;

}
