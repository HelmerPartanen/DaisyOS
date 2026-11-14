using DaisyOS.System.AppStore;
using DaisyOS.System.Applications;
using DaisyOS.Core.Models;
using DaisyOS.Shell.ViewModels;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Services;

public enum InstalledApplicationPackageKind
{
    None,
    Pacman,
    Flatpak,
    DesktopEntry,
    AppImage,
    Container
}

public sealed record InstalledApplicationManagement(
    AppEntry App,
    string? PackageId,
    InstalledApplicationPackageKind PackageKind,
    string? DesktopEntryPath,
    string UnavailableReason)
{
    public bool CanManage => !IsProtected && PackageKind != InstalledApplicationPackageKind.None && !string.IsNullOrWhiteSpace(PackageId);
    public string InstallationScope { get; init; } = "user";
    public string Origin { get; init; } = "flathub";
    public InstalledApplication? Installation { get; init; }
    public bool IsProtected { get; init; }
}

/// <summary>Resolves visible desktop applications to their real Linux package owner or user-level desktop entry.</summary>
public sealed class InstalledApplicationService
{
    private readonly ICommandRunner _runner;
    private readonly ApplicationManagementService _management;

    public InstalledApplicationService(ICommandRunner? runner = null) { _runner = runner ?? new SafeCommandRunner(); _management = new(_runner); }

    public IReadOnlyList<AppEntry> GetInstalledApplications() =>
        new LinuxAppLauncherService().GetAvailableApps()
            .Select(app => app with
            {
                Name = app.LaunchKind == AppLaunchKind.Internal || app.Id.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase) ? Loc.Get(app.Name) : app.Name,
                Description = app.LaunchKind == AppLaunchKind.Internal || app.Id.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase) || app.Description == "Desktop application" ? Loc.Get(app.Description) : app.Description
            })
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public async Task<IReadOnlyList<AppEntry>> GetInstalledApplicationsAsync(CancellationToken token = default)
    {
        var launchers = GetInstalledApplications();
        var inventory = await _management.GetInstalledAsync(token);
        var result = new List<AppEntry>();
        foreach (var installation in inventory)
        {
            var launcher = launchers.FirstOrDefault(app => installation.DesktopIds.Contains(DesktopApplicationMaskStore.NormalizeDesktopId(app.Id), StringComparer.OrdinalIgnoreCase));
            var entry = launcher ?? new AppEntry(installation.DesktopIds.FirstOrDefault() ?? installation.Identity.PackageId, installation.Name, "", "package", false, AppLaunchKind.DesktopEntry);
            result.Add(entry with { Name = entry.Name + (installation.CleanupPending ? " — " + Loc.Get("Cleanup pending") : "") + " (" + installation.Identity.Backend + " · " + installation.Identity.Scope + (installation.Identity.Reference is null ? "" : " · " + installation.Identity.Reference.Split('/').Last()) + ")", Installation = installation.Identity });
        }
        result.AddRange(launchers.Where(app => !inventory.Any(installation => installation.DesktopIds.Contains(DesktopApplicationMaskStore.NormalizeDesktopId(app.Id), StringComparer.OrdinalIgnoreCase))));
        return result.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

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
        var normalized = DesktopApplicationMaskStore.NormalizeDesktopId(app.Id);
        if (app.Installation is { } selectedIdentity)
        {
            var installed = (await _management.GetInstalledAsync(cancellationToken)).FirstOrDefault(record => record.Identity == selectedIdentity);
            if (installed is null) return new(app, null, InstalledApplicationPackageKind.None, null, Loc.Get("This installation no longer exists. Refresh the application list."));
            return new(app, selectedIdentity.PackageId, selectedIdentity.Backend switch
            {
                ApplicationBackend.Pacman => InstalledApplicationPackageKind.Pacman, ApplicationBackend.Flatpak => InstalledApplicationPackageKind.Flatpak,
                ApplicationBackend.AppImage => InstalledApplicationPackageKind.AppImage, ApplicationBackend.Container => InstalledApplicationPackageKind.Container,
                _ => InstalledApplicationPackageKind.DesktopEntry
            }, installed.OwnedLaunchers.FirstOrDefault(), installed.ProtectionReason)
            { Installation = installed, InstallationScope = selectedIdentity.Scope, Origin = installed.Origin, IsProtected = installed.IsProtected };
        }
        if (new[] { "org.daisyos.settings.desktop", "org.daisyos.appinstaller.desktop", "org.daisyos.installer.desktop", "org.daisyos.taskmanager.desktop" }.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return new(app, null, InstalledApplicationPackageKind.None, null, Loc.Get("This application is required for system management and recovery.")) { IsProtected = true };
        var managed = (await _management.GetManagedInstalledAsync(cancellationToken)).FirstOrDefault(record => record.DesktopIds.Contains(normalized, StringComparer.OrdinalIgnoreCase));
        if (managed is not null) return new(app, managed.Identity.PackageId, managed.Identity.Backend switch { ApplicationBackend.AppImage => InstalledApplicationPackageKind.AppImage, ApplicationBackend.Container => InstalledApplicationPackageKind.Container, _ => InstalledApplicationPackageKind.DesktopEntry }, managed.OwnedLaunchers.FirstOrDefault(), "") { Installation = managed };
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

        return new(app, normalized, InstalledApplicationPackageKind.DesktopEntry, desktopPath,
            Loc.Get("This app has no package ownership record. Use guided removal; hiding its shortcut will not uninstall it."));
    }

    public Task<ApplicationTransactionPlan> PrepareUninstallAsync(InstalledApplicationManagement target, CancellationToken token = default)
    {
        var identity = new ApplicationIdentity(target.PackageKind switch
        {
            InstalledApplicationPackageKind.Pacman => ApplicationBackend.Pacman,
            InstalledApplicationPackageKind.Flatpak => ApplicationBackend.Flatpak,
            InstalledApplicationPackageKind.AppImage => ApplicationBackend.AppImage,
            InstalledApplicationPackageKind.Container => ApplicationBackend.Container,
            _ => ApplicationBackend.Unmanaged
        }, target.PackageId ?? target.App.Id, target.InstallationScope);
        var app = target.Installation ?? new InstalledApplication(identity, target.App.Name)
        {
            DesktopIds = [DesktopApplicationMaskStore.NormalizeDesktopId(target.App.Id)],
            OwnedLaunchers = target.DesktopEntryPath is null ? [] : [target.DesktopEntryPath],
            OwnedCommands = ApplicationIntegration.Commands(target.DesktopEntryPath is null ? [] : [target.DesktopEntryPath]),
            Origin = target.Origin, IsProtected = target.IsProtected, ProtectionReason = target.UnavailableReason
        };
        return _management.PrepareUninstallAsync(app, token);
    }

    public async Task<ApplicationTransactionResult> ExecuteUninstallAsync(ApplicationTransactionPlan plan, bool removeData = false, CancellationToken token = default)
    {
        var result = await _management.ExecuteAsync(plan, removeData, token: token);
        if (result.Succeeded)
        {
            foreach (var id in result.RemovedDesktopIds) (Avalonia.Application.Current as App)?.NotifyApplicationRemoved(id);
            if (result.RemovedDesktopIds.Length == 0) (Avalonia.Application.Current as App)?.NotifyApplicationRestored(plan.Application.DesktopIds.FirstOrDefault() ?? plan.Application.Identity.PackageId);
        }
        return result;
    }

    public async Task<ApplicationTransactionPlan> PrepareRepairAsync(InstalledApplicationManagement target, CancellationToken token = default)
    {
        var removal = await PrepareUninstallAsync(target, token);
        return await _management.PrepareRepairAsync(removal.Application, token);
    }
    public async Task<bool> RepairAsync(InstalledApplicationManagement target, CancellationToken cancellationToken = default) =>
        (await _management.ExecuteAsync(await PrepareRepairAsync(target, cancellationToken), token: cancellationToken)).Succeeded;
    public async Task<ApplicationTransactionResult> ExecuteRepairAsync(ApplicationTransactionPlan plan, CancellationToken token = default)
    {
        var result = await _management.ExecuteAsync(plan, token: token);
        if (result.Succeeded) foreach (var id in plan.Application.DesktopIds) (Avalonia.Application.Current as App)?.NotifyApplicationRestored(id);
        return result;
    }

    public async Task<bool> UninstallAsync(InstalledApplicationManagement target, CancellationToken cancellationToken = default)
    {
        if (!target.CanManage) return false;
        var plan = await PrepareUninstallAsync(target, cancellationToken);
        return (await ExecuteUninstallAsync(plan, token: cancellationToken)).Succeeded;
    }

    public bool HideApplication(InstalledApplicationManagement target) => DesktopApplicationMaskStore.RemoveOrMask(
        target.App.Id, target.DesktopEntryPath, target.App.Name, target.App.Icon);

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
            File.AppendAllText(destination, "\nX-DaisyOS-SourceDesktopFile=" + Path.GetFullPath(source!) + "\n");
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
