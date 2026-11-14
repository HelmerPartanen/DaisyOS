using System.Security.Cryptography;
using System.Text.Json;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.AppImage;
using DaisyOS.System.Processes;
using DaisyOS.System.Serialization;
namespace DaisyOS.System.Applications;

public sealed class ApplicationManagementService : IApplicationManagementService
{
    public const string PackageHelper = "/usr/lib/daisyos/daisy-package-helper";
    private readonly ICommandRunner _runner;
    private readonly IAppImageInstallerService _images;
    private readonly ApplicationRegistry _registry;
    private readonly CompatibilityApplications _containers;
    private readonly ApplicationIntegration _integration;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public ApplicationManagementService(ICommandRunner? runner = null, string? dataHome = null, IAppImageInstallerService? images = null)
    {
        _runner = runner ?? new SafeCommandRunner(); _images = images ?? new AppImageService(_runner, dataHome);
        _registry = new(dataHome); _containers = new(_runner, _registry); _integration = new(_runner, dataHome);
    }
    public static bool SupportsFile(string path) => new[] { ".appimage", ".flatpakref", ".flatpak", ".deb", ".rpm", ".pkg.tar.zst", ".pkg.tar.xz", ".pkg.tar.gz", ".pkg.tar" }
        .Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<InstalledApplication>> GetInstalledAsync(CancellationToken token = default)
    {
        var applications = new List<InstalledApplication>(await GetManagedInstalledAsync(token).ConfigureAwait(false));
        var flatpak = await Run("flatpak", ["list", "--app", "--columns=application,name,version,installation,origin,ref"], token).ConfigureAwait(false);
        if (flatpak.Succeeded)
            foreach (var line in flatpak.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split('\t'); if (fields.Length < 5) continue;
                applications.Add(new(new(ApplicationBackend.Flatpak, fields[0], fields[3], Reference: fields.Length > 5 ? fields[5] : null), fields[1], fields[2])
                { Origin = fields[4], DesktopIds = [fields[0] + ".desktop"] });
            }
        var native = await Run(PackageHelper, ["list"], token).ConfigureAwait(false);
        if (native.Succeeded)
        {
            try
            {
                var records = JsonSerializer.Deserialize(native.StandardOutput, DaisySystemJsonContext.Default.ListInstalledApplication) ?? [];
                applications.AddRange(records.Select(ApplicationRegistry.Normalize).Select(record => record with { OwnedCommands = ApplicationIntegration.Commands(record.OwnedLaunchers) }));
            }
            catch (JsonException ex) { throw new IOException("Invalid native application inventory.", ex); }
        }
        return applications.DistinctBy(app => app.Identity).ToArray();
    }

    public async Task<IReadOnlyList<InstalledApplication>> GetManagedInstalledAsync(CancellationToken token = default)
    {
        var applications = new List<InstalledApplication>(_registry.Read());
        foreach (var image in await _images.GetInstalledAsync(token).ConfigureAwait(false))
            applications.Add(new(new(ApplicationBackend.AppImage, image.DesktopId), image.Name, image.Version)
            { SourcePath = image.AppImagePath, IsBroken = !File.Exists(image.AppImagePath) || !File.Exists(image.DesktopFilePath), DesktopIds = [image.DesktopId.EndsWith(".desktop", StringComparison.Ordinal) ? image.DesktopId : image.DesktopId + ".desktop"], OwnedLaunchers = [image.DesktopFilePath], OwnedCommands = ApplicationIntegration.Commands([image.DesktopFilePath]), OwnedIcons = [image.IconPath] });
        return applications;
    }

    public async Task<ApplicationTransactionPlan> InspectFileAsync(string path, string? compatibilityImage = null, CancellationToken token = default)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new IOException("The selected application file no longer exists.");
        var fingerprint = await FingerprintFile(path, token).ConfigureAwait(false);
        if (!SupportsFile(path)) throw new IOException("This format is not supported. Choose an Arch package, Flatpak, AppImage, DEB or RPM application.");
        if (path.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
        {
            var image = await _images.InspectAsync(path, token).ConfigureAwait(false);
            return new(ApplicationOperation.Install, new(new(ApplicationBackend.AppImage, image.DesktopId), image.Name, image.Version)
            { SourcePath = path, SourceFingerprint = fingerprint, Architecture = image.Architecture, DesktopIds = [image.DesktopId.EndsWith(".desktop", StringComparison.Ordinal) ? image.DesktopId : image.DesktopId + ".desktop"] });
        }
        if (path.EndsWith(".deb", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase))
        {
            var plan = await _containers.InspectAsync(path, compatibilityImage, token).ConfigureAwait(false);
            plan = plan with { Application = plan.Application with { SourceFingerprint = fingerprint } };
            if (!plan.CanExecute) return plan;
            var tools = await Run("podman", ["info", "--format", "{{.Host.Security.Rootless}}"], token).ConfigureAwait(false);
            var distrobox = await Run("distrobox", ["--version"], token).ConfigureAwait(false);
            if (tools.Succeeded && distrobox.Succeeded) return plan;
            var setup = await NativePlan("plan-tools", "-", token).ConfigureAwait(false);
            return plan with { RequiresCompatibilitySetup = true, AffectedPackages = setup.Packages, Error = setup.Error,
                DownloadBytes = setup.DownloadBytes, InstalledBytes = setup.InstalledBytes };
        }
        if (path.EndsWith(".flatpakref", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".flatpak", StringComparison.OrdinalIgnoreCase))
        {
            InstalledApplication app;
            if (path.EndsWith(".flatpak", StringComparison.OrdinalIgnoreCase))
            {
                try { app = FlatpakFileMetadata.ReadBundle(path); }
                catch (DllNotFoundException)
                {
                    var setup = await PrepareInstallAsync(new(new(ApplicationBackend.Flatpak, Path.GetFileNameWithoutExtension(path)), Path.GetFileName(path)) { SourcePath = path, SourceFingerprint = fingerprint }, token).ConfigureAwait(false);
                    return setup.RequiresFlatpakSetup ? setup : setup with { Error = "Flatpak’s metadata library is missing. Repair Flatpak before opening this bundle." };
                }
            }
            else
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                var section = "";
                foreach (var line in File.ReadLines(path))
                {
                    var value = line.Trim();
                    if (value.StartsWith('[')) { section = value; continue; }
                    var split = value.IndexOf('=');
                    if (section == "[Flatpak Ref]" && split > 0) values[value[..split]] = value[(split + 1)..];
                }
                if (!values.TryGetValue("Name", out var name) || name.Count(c => c == '.') < 2 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')))
                    throw new IOException("This Flatpak reference has no valid application ID.");
                if (values.GetValueOrDefault("IsRuntime") == "true") throw new IOException("Choose an application reference. Runtime references are managed by Flatpak.");
                app = new(new(ApplicationBackend.Flatpak, name), values.GetValueOrDefault("Title", name), values.GetValueOrDefault("Branch", "stable"))
                    { SourcePath = path, Origin = values.GetValueOrDefault("Url", ""), DesktopIds = [name + ".desktop"] };
            }
            return await PrepareInstallAsync(app with { SourceFingerprint = fingerprint }, token).ConfigureAwait(false);
        }
        var result = await NativePlan("plan-install", path, token).ConfigureAwait(false);
        return NativePlanResult(ApplicationOperation.Install, new(new(ApplicationBackend.Pacman, result.Name, "system"), result.Name, result.Version)
        { SourcePath = path, SourceFingerprint = fingerprint, Architecture = result.Architecture, DesktopIds = result.DesktopFiles.Select(Path.GetFileName).OfType<string>().ToArray() }, result);
    }

    public async Task<ApplicationTransactionPlan> PrepareInstallAsync(InstalledApplication application, CancellationToken token = default)
    {
        var plan = new ApplicationTransactionPlan(ApplicationOperation.Install, application);
        if (application.Identity.Backend != ApplicationBackend.Flatpak) return plan;
        var available = await Run("flatpak", ["--version"], token).ConfigureAwait(false);
        if (available.Succeeded) return plan;
        var setup = await NativePlan("plan-flatpak", "-", token).ConfigureAwait(false);
        return plan with { RequiresFlatpakSetup = true, AffectedPackages = setup.Packages, Error = setup.Error };
    }
    private static async Task<string> FingerprintFile(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
    }

    public async Task<ApplicationTransactionPlan> PrepareRepairAsync(InstalledApplication application, CancellationToken token = default)
    {
        if (application.Identity.Backend == ApplicationBackend.Pacman)
            return NativePlanResult(ApplicationOperation.Repair, application, await NativePlan("plan-repair", application.Identity.PackageId, token).ConfigureAwait(false));
        if (application.Identity.Backend == ApplicationBackend.Unmanaged)
            return new(ApplicationOperation.Repair, application) { Error = "This app has no supported repair operation. Use its original installer." };
        if (application.Identity.Backend is ApplicationBackend.AppImage or ApplicationBackend.Container && !File.Exists(application.SourcePath))
            return new(ApplicationOperation.Repair, application) { Error = "Choose the original application package in App Installer to repair this app." };
        return new(ApplicationOperation.Repair, application);
    }

    public async Task<ApplicationTransactionPlan> PrepareUninstallAsync(InstalledApplication application, CancellationToken token = default)
    {
        if (application.CleanupPending) return new(ApplicationOperation.Uninstall, application) { CleanupOnly = true };
        if (application.IsProtected) return new(ApplicationOperation.Uninstall, application) { Error = application.ProtectionReason };
        if (application.Identity.Backend == ApplicationBackend.Pacman)
            return NativePlanResult(ApplicationOperation.Uninstall, application, await NativePlan("plan-remove", application.Identity.PackageId, token).ConfigureAwait(false));
        if (application.Identity.Backend == ApplicationBackend.Flatpak)
        {
            var list = await Run("flatpak", ["list", "--app", "--columns=application,installation,ref"], token).ConfigureAwait(false);
            var entries = list.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('\t')).Where(parts => parts.Length >= 2 && parts[0] == application.Identity.PackageId).ToArray();
            var selected = entries.Where(parts => parts[1] == application.Identity.Scope).ToArray();
            if (application.Identity.Reference is null && selected.Length > 1) return new(ApplicationOperation.Uninstall, application) { Error = "Several versions of this app are installed. Select its exact version in Installed apps." };
            if (application.Identity.Reference is null && selected is [{ Length: >= 3 } single]) application = application with { Identity = application.Identity with { Reference = single[2] } };
            return new(ApplicationOperation.Uninstall, application) { IsDataShared = !list.Succeeded || entries.Length > 1 };
        }
        if (application.Identity.Backend == ApplicationBackend.Unmanaged)
        {
            if (application.ReviewedPaths.Length == 1 && !Directory.Exists(application.ReviewedPaths[0]) && !File.Exists(application.ReviewedPaths[0]) &&
                _registry.Read().Any(record => record.Identity == application.Identity && record.ReviewedPaths.SequenceEqual(application.ReviewedPaths)))
                return new(ApplicationOperation.Uninstall, application) { CleanupOnly = true };
            if (application.ReviewedPaths.Length != 1) return new(ApplicationOperation.Uninstall, application) { Error = "This app has no ownership record. Choose its dedicated installation folder. Removing a shortcut does not uninstall the app." };
            var path = Path.GetFullPath(application.ReviewedPaths[0]);
            if (!ApplicationRegistry.IsWithin(path, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
            {
                var native = await NativePlan("plan-portable-remove", path, token).ConfigureAwait(false);
                var plan = NativePlanResult(ApplicationOperation.Uninstall, application with { Identity = application.Identity with { Scope = "system" } }, native);
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (native.Error.Length == 0 && application.OwnedLaunchers.Any(launcher => !ApplicationRegistry.IsWithin(launcher, home) && File.Exists(launcher) && !native.DesktopFiles.Contains(launcher, StringComparer.Ordinal)))
                    return plan with { Error = "The system launcher could not be verified as belonging to this folder. Use the app’s original uninstaller or select its actual installation folder." };
                return plan;
            }
            return new(ApplicationOperation.Uninstall, application with { Identity = application.Identity with { Scope = "user" } });
        }
        return new(ApplicationOperation.Uninstall, application);
    }

    public async Task<ApplicationTransactionResult> ExecuteAsync(ApplicationTransactionPlan plan, bool removeData = false, IProgress<ApplicationProgress>? progress = null, CancellationToken token = default)
    {
        if (!plan.CanExecute) return new(false, plan.Error.Length > 0 ? plan.Error : plan.Application.ProtectionReason);
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_registry.Root);
            using var transactionLock = new FileStream(Path.Combine(_registry.Root, "transaction.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _registry.Read(); // Fail closed on corrupt ownership records before changing any installation.
            progress?.Report(new("Preparing"));
            var app = plan.Application; var id = app.Identity;
            if (plan.Operation != ApplicationOperation.Uninstall && app.SourcePath.Length > 0 && app.SourceFingerprint.Length > 0 &&
                await FingerprintFile(app.SourcePath, token).ConfigureAwait(false) != app.SourceFingerprint)
                return new(false, "The package changed after inspection. Select it again before installing.");
            if (plan.RequiresFlatpakSetup)
            {
                var fresh = await NativePlan("plan-flatpak", "-", token).ConfigureAwait(false);
                if (fresh.Error.Length > 0 || !fresh.Packages.Order().SequenceEqual(plan.AffectedPackages.Order())) return new(false, "Flatpak prerequisites changed. Review setup again.", fresh.Error);
                var setup = await _runner.RunAsync("pkexec", [PackageHelper, "flatpak-tools", "-", TransactionFingerprint(fresh.Packages)], TimeSpan.FromMinutes(20), token).ConfigureAwait(false);
                if (!setup.Succeeded) return Failure(setup);
                if (app.SourcePath.EndsWith(".flatpak", StringComparison.OrdinalIgnoreCase) && id.Reference is null)
                    return new(false, "Flatpak support is ready. Review the bundle’s application details before installing.") { RequiresReview = true };
            }
            if (plan.CleanupOnly)
            {
                await _integration.RemoveAsync(app, token).ConfigureAwait(false);
                _registry.Remove(id);
                var cleanedIds = await RemovedDesktopIds(app, token).ConfigureAwait(false);
                foreach (var desktopId in cleanedIds) OptionalApplicationState.SetRemoved(desktopId, true);
                return new(true, "Application shortcuts cleaned up.") { RemovedDesktopIds = cleanedIds };
            }
            if (id.Backend == ApplicationBackend.Pacman)
            {
                var nativeOperation = plan.Operation switch { ApplicationOperation.Install => "install", ApplicationOperation.Repair => "repair", _ => "remove" };
                var target = plan.Operation == ApplicationOperation.Install ? app.SourcePath : id.PackageId;
                var fresh = await NativePlan("plan-" + nativeOperation, target, token).ConfigureAwait(false);
                if (fresh.Error.Length > 0 || fresh.Protected) return new(false, fresh.Error.Length > 0 ? fresh.Error : "This transaction affects required system components.");
                if (!fresh.Packages.Order().SequenceEqual(plan.AffectedPackages.Order())) return new(false, "The package transaction changed. Review the updated list before continuing.");
                // The root helper validates the plan fingerprint again while holding libalpm's database lock.
                var fingerprint = TransactionFingerprint(fresh.Packages);
                progress?.Report(new("Waiting for authentication"));
                var result = await _runner.RunAsync("pkexec", [PackageHelper, nativeOperation, target, fingerprint], TimeSpan.FromMinutes(20), token).ConfigureAwait(false);
                if (!result.Succeeded) return Failure(result);
            }
            else if (id.Backend == ApplicationBackend.AppImage)
            {
                if (plan.Operation != ApplicationOperation.Uninstall)
                {
                    var installed = await _images.InstallAsync(app.SourcePath, string.IsNullOrWhiteSpace(app.DataDirectory) ? null : app.DataDirectory, token).ConfigureAwait(false);
                    app = app with { Identity = app.Identity with { PackageId = installed.DesktopId }, SourcePath = installed.AppImagePath,
                        DesktopIds = [installed.DesktopId + ".desktop"], OwnedLaunchers = [installed.DesktopFilePath] };
                }
                else if (!await _images.UninstallAsync(id.PackageId, token).ConfigureAwait(false)) return new(false, "The AppImage is no longer registered.");
            }
            else if (id.Backend == ApplicationBackend.Container)
            {
                if (plan.Operation == ApplicationOperation.Install && plan.RequiresCompatibilitySetup)
                {
                    progress?.Report(new("Setting up compatibility support"));
                    var fresh = await NativePlan("plan-tools", "-", token).ConfigureAwait(false);
                    if (fresh.Error.Length > 0 || !fresh.Packages.Order().SequenceEqual(plan.AffectedPackages.Order()))
                        return new(false, "Compatibility prerequisites changed. Review the installation again.", fresh.Error);
                    var setup = await _runner.RunAsync("pkexec", [PackageHelper, "tools", "-", TransactionFingerprint(fresh.Packages)], TimeSpan.FromMinutes(20), token).ConfigureAwait(false);
                    if (!setup.Succeeded) return Failure(setup);
                }
                var result = plan.Operation != ApplicationOperation.Uninstall
                    ? await _containers.InstallAsync(app, progress, token).ConfigureAwait(false)
                    : await _containers.RemoveAsync(app, removeData, token).ConfigureAwait(false);
                if (!result.Succeeded) return result;
                app = _registry.Read().FirstOrDefault(record => record.Identity == id) ?? app;
            }
            else if (id.Backend == ApplicationBackend.Flatpak)
            {
                if (plan.Operation == ApplicationOperation.Uninstall && removeData && plan.SupportsDataRemoval)
                {
                    var fresh = await PrepareUninstallAsync(app, token).ConfigureAwait(false);
                    if (!fresh.CanExecute || !fresh.SupportsDataRemoval) return new(false, "The app’s saved data is now shared with another installation. Review removal again.");
                }
                progress?.Report(new(plan.Operation == ApplicationOperation.Install ? "Installing" : "Uninstalling"));
                if (plan.Operation == ApplicationOperation.Install && app.SourcePath.Length == 0 && app.Origin is "" or "flathub")
                {
                    var remote = await Run("flatpak", ["remote-add", Scope(id.Scope), "--if-not-exists", "flathub", "https://dl.flathub.org/repo/flathub.flatpakrepo"], token).ConfigureAwait(false);
                    if (!remote.Succeeded) return Failure(remote);
                }
                var arguments = new List<string> { plan.Operation != ApplicationOperation.Uninstall ? "install" : "uninstall", Scope(id.Scope), "-y", "--noninteractive" };
                if (plan.Operation == ApplicationOperation.Repair) arguments.Add("--reinstall");
                if (removeData && plan.SupportsDataRemoval) arguments.Add("--delete-data");
                arguments.Add("--");
                if (plan.Operation != ApplicationOperation.Uninstall && app.SourcePath.Length == 0) arguments.Add(app.Origin.Length == 0 ? "flathub" : app.Origin);
                arguments.Add(plan.Operation != ApplicationOperation.Uninstall && app.SourcePath.Length > 0 ? app.SourcePath : id.Reference ?? id.PackageId);
                var result = await _runner.RunAsync("flatpak", arguments, TimeSpan.FromMinutes(20), token).ConfigureAwait(false);
                if (!result.Succeeded) return Failure(result);
                if (plan.Operation == ApplicationOperation.Uninstall)
                {
                    var check = await Run("flatpak", ["list", Scope(id.Scope), "--app", id.Reference is null ? "--columns=application" : "--columns=ref"], token).ConfigureAwait(false);
                    if (!check.Succeeded) return new(false, "Removal could not be verified. Shortcuts were kept; refresh and retry cleanup.", check.StandardError);
                    if (check.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(value => value.Trim() == (id.Reference ?? id.PackageId)))
                        return new(false, "The application is still installed. Its shortcuts have been kept.");
                }
            }
            else
            {
                ApplicationTransactionResult review;
                if (id.Scope == "system")
                {
                    if (app.ReviewedPaths.Length != 1) return new(false, "Review one dedicated application folder first.");
                    var fresh = await NativePlan("plan-portable-remove", app.ReviewedPaths[0], token).ConfigureAwait(false);
                    if (fresh.Error.Length > 0 || !fresh.Packages.Order().SequenceEqual(plan.AffectedPackages.Order())) return new(false, "The reviewed folder changed. Review removal again.", fresh.Error);
                    var result = await _runner.RunAsync("pkexec", [PackageHelper, "portable-remove", app.ReviewedPaths[0], TransactionFingerprint(fresh.Packages)], TimeSpan.FromMinutes(5), token).ConfigureAwait(false);
                    review = result.Succeeded ? new(true, "Application folder removed.") : Failure(result);
                }
                else review = await RemoveReviewedPaths(app, token).ConfigureAwait(false);
                if (!review.Succeeded) return review;
            }
            progress?.Report(new("Refreshing applications"));
            if (plan.Operation == ApplicationOperation.Uninstall)
            {
                _registry.Save(app with { CleanupPending = true });
                var removedIds = await RemovedDesktopIds(app, CancellationToken.None).ConfigureAwait(false);
                foreach (var desktopId in removedIds) OptionalApplicationState.SetRemoved(desktopId, true);
                try
                {
                    await _integration.RemoveAsync(app, CancellationToken.None).ConfigureAwait(false);
                    _registry.Remove(id);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _registry.Save(app with { CleanupPending = true }); // Retain ownership for a cleanup retry.
                    return new(true, "The app was removed, but some shortcuts still need cleanup.", ex.Message) { CleanupPending = true, RemovedDesktopIds = removedIds };
                }
                return new(true, "Application uninstalled.") { RemovedDesktopIds = removedIds };
            }
            foreach (var desktopId in app.DesktopIds) { DesktopApplicationMaskStore.Restore(desktopId); OptionalApplicationState.SetRemoved(desktopId, false); }
            await _integration.RefreshAsync(token).ConfigureAwait(false);
            progress?.Report(new("Completed", 1));
            return new(true, "Application installed.") { Application = app };
        }
        catch (OperationCanceledException) { return new(false, "The operation was cancelled. Refresh the app list to check its current state.") { Cancelled = true }; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or AppImageException or JsonException)
        { return new(false, "The operation could not finish.", ex.Message); }
        finally { Gate.Release(); }
    }
    private async Task<string[]> RemovedDesktopIds(InstalledApplication app, CancellationToken token)
    {
        if (app.Identity.Backend != ApplicationBackend.Flatpak) return app.DesktopIds;
        var remaining = await Run("flatpak", ["list", "--app", "--columns=application"], token).ConfigureAwait(false);
        return !remaining.Succeeded || remaining.StandardOutput.Split('\n').Any(value => value.Trim() == app.Identity.PackageId) ? [] : app.DesktopIds;
    }
    private async Task<ApplicationTransactionResult> RemoveReviewedPaths(InstalledApplication app, CancellationToken token)
    {
        if (app.ReviewedPaths.Length == 0) return new(false, "Review application-owned paths first.");
        foreach (var path in app.ReviewedPaths)
        {
            token.ThrowIfCancellationRequested();
            var full = Path.GetFullPath(path);
            // Only a user-reviewed dedicated installation path may be trashed.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var shared = new[] { home, ".local", ".local/share", ".local/bin", ".config", ".cache", "Applications", "Desktop", "Downloads", "Documents", "Pictures", "Videos", "Music", ".ssh", ".gnupg" }
                .Select(value => Path.IsPathRooted(value) ? value : Path.Combine(home, value)).Select(Path.GetFullPath);
            if (full == _registry.Root || ApplicationRegistry.IsWithin(_registry.Root, full) || !ApplicationRegistry.IsWithin(full, home) || shared.Contains(full.TrimEnd('/'), StringComparer.Ordinal) || full.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
                return new(false, "Choose an app’s dedicated installation folder, not a shared folder or shortcut: " + full);
            for (var current = new DirectoryInfo(Directory.Exists(full) ? full : Path.GetDirectoryName(full)!); current is not null && current.FullName != home; current = current.Parent)
                if (current.LinkTarget is not null) return new(false, "Symbolic links cannot be used for guided removal.");
            if (!File.Exists(full) && !Directory.Exists(full)) return new(false, "The reviewed installation path no longer exists.");
            var result = await Run("gio", ["trash", "--", full], token).ConfigureAwait(false);
            if (!result.Succeeded) return Failure(result);
        }
        return new(true, "Reviewed application files moved to Trash.");
    }
    private Task<CommandResult> Run(string command, IReadOnlyList<string> args, CancellationToken token) => _runner.RunAsync(command, args, TimeSpan.FromSeconds(20), token);
    private async Task<NativeTransaction> NativePlan(string operation, string target, CancellationToken token)
    {
        var result = await Run(PackageHelper, [operation, target], token).ConfigureAwait(false);
        if (!result.Succeeded) return new() { Error = result.StandardError.Length > 0 ? result.StandardError : "The protected package-management helper is unavailable. Install or repair DaisyOS system components." };
        try { return JsonSerializer.Deserialize(result.StandardOutput, DaisySystemJsonContext.Default.NativeTransaction) ?? new() { Error = "Invalid package transaction response." }; }
        catch (JsonException) { return new() { Error = "Invalid package transaction response." }; }
    }
    private static ApplicationTransactionPlan NativePlanResult(ApplicationOperation operation, InstalledApplication app, NativeTransaction native) => new(operation, app with
    { IsProtected = native.Protected, ProtectionReason = native.Error,
      OwnedLaunchers = native.DesktopFiles.Length > 0 ? native.DesktopFiles : app.OwnedLaunchers,
      OwnedCommands = native.DesktopFiles.Length > 0 ? ApplicationIntegration.Commands(native.DesktopFiles) : app.OwnedCommands,
      DesktopIds = native.DesktopFiles.Length > 0 ? native.DesktopFiles.Select(Path.GetFileName).OfType<string>().ToArray() : app.DesktopIds })
    { AffectedPackages = native.Packages, Error = native.Error, DownloadBytes = native.DownloadBytes, InstalledBytes = native.InstalledBytes };
    public static string TransactionFingerprint(IEnumerable<string> packages) => Convert.ToHexString(SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(string.Join('\n', packages.Order(StringComparer.Ordinal))))).ToLowerInvariant();
    private static ApplicationTransactionResult Failure(CommandResult result) => new(false,
        result.ExitCode is 126 or 127 ? "Authentication was cancelled or unavailable." : result.TimedOut ? "The operation timed out. Refresh to check its current state." : "The package manager could not complete the operation.", result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput);
    internal static string Scope(string scope) => scope is "user" or "system" ? "--" + scope : "--installation=" + scope;
}
