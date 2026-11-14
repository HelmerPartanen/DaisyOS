using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;

namespace DaisyOS.System.Processes;

public sealed class LinuxAppLauncherService : IAppLauncherService
{
    private static readonly HashSet<string> SupportedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name", "Comment", "Icon", "Exec", "TryExec", "NoDisplay", "Hidden", "Terminal", "Type", "Categories", "X-DaisyOS-Removed"
    };

    private readonly ILogService _log;
    private readonly SafeProcessLauncher _launcher;
    private readonly object _lock = new();
    private IReadOnlyList<AppEntry>? _cachedApps;
    private long _catalogVersion = -1;
    private long _maskVersion = -1;

    public LinuxAppLauncherService(ILogService? log = null)
    {
        _log = log ?? NullLogService.Instance;
        _launcher = new SafeProcessLauncher(_log);
    }

    public IReadOnlyList<AppEntry> GetAvailableApps()
    {
        lock (_lock)
        {
            var version = DaisyLaunchClient.Shared.CatalogVersion;
            var maskVersion = DesktopApplicationMaskStore.Version;
            if (_cachedApps is null || _catalogVersion != version || _maskVersion != maskVersion)
            {
                _cachedApps = DiscoverApps();
                _catalogVersion = version;
                _maskVersion = maskVersion;
            }
            return _cachedApps;
        }
    }

    public void Refresh()
    {
        lock (_lock)
        {
            var version = DaisyLaunchClient.Shared.CatalogVersion;
            var maskVersion = DesktopApplicationMaskStore.Version;
            _cachedApps = DiscoverApps();
            _catalogVersion = version;
            _maskVersion = maskVersion;
        }
    }

    public Task<AppLaunchResult> LaunchAsync(AppEntry app, CancellationToken cancellationToken = default) => LaunchWithActivationAsync(app, "", cancellationToken);

    public Task<AppLaunchResult> LaunchWithActivationAsync(AppEntry app, string activationToken, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AppLaunchResult>(cancellationToken);
        }

        if (app.LaunchKind == AppLaunchKind.DesktopEntry)
            return DaisyLaunchClient.Enabled
                ? DaisyLaunchClient.Shared.LaunchAsync(app.Id, activationToken: activationToken, token: cancellationToken)
                : DaisyLaunchClient.FallbackAsync(app.Id, activationToken: activationToken, token: cancellationToken);
        // Process.Start can perform synchronous filesystem and OS work. Keep it
        // off the caller's UI thread, including URI and developer command launches.
        return Task.Run(() => app.LaunchKind switch
        {
            AppLaunchKind.Internal => new AppLaunchResult(true, $"{app.Name} opened internally."),
            AppLaunchKind.Uri => LaunchUri(app),
            AppLaunchKind.DirectCommand => LaunchCommand(app),
            _ => new AppLaunchResult(false, $"{app.Name} cannot be launched by the system service.")
        }, cancellationToken);
    }

    /// <summary>
    /// Launches a user-visible desktop shortcut using the same validated Exec
    /// parsing as the application launcher. This intentionally does not defer to
    /// a file opener: on some desktops a .desktop file is treated as a document
    /// instead of an application, leaving a double-click with no visible result.
    /// </summary>
    public Task<AppLaunchResult> LaunchDesktopEntryFileAsync(
        string desktopFilePath,
        CancellationToken cancellationToken = default, string activationToken = "")
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AppLaunchResult>(cancellationToken);
        }

        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(desktopFilePath)
                || !desktopFilePath.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(desktopFilePath))
                return new AppLaunchResult(false, "This desktop shortcut is no longer available.");
            return await DaisyLaunchClient.Shared.LaunchAsync(Path.GetFullPath(desktopFilePath),
                activationToken: activationToken, token: cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    private AppLaunchResult LaunchUri(AppEntry app)
    {
        if (string.IsNullOrWhiteSpace(app.LaunchTarget))
        {
            return new AppLaunchResult(false, $"{app.Name} has no launch target.");
        }

        var result = _launcher.Launch("xdg-open", [app.LaunchTarget]);
        return result.Succeeded
            ? new AppLaunchResult(true, $"{app.Name} launch requested.", result.ProcessId)
            : result;
    }

    private AppLaunchResult LaunchCommand(AppEntry app)
    {
        if (string.IsNullOrWhiteSpace(app.LaunchTarget))
        {
            return new AppLaunchResult(false, $"{app.Name} has no launch command.");
        }

        var result = _launcher.Launch(app.LaunchTarget, app.LaunchArguments ?? []);
        return result.Succeeded
            ? new AppLaunchResult(true, $"{app.Name} launch requested.", result.ProcessId)
            : result;
    }

    private static readonly IReadOnlyList<AppEntry> BuiltInSystemApps =
    [
        new(
            "org.daisyos.mail.desktop",
            "Mail",
            "Read and send email",
            "org.daisyos.mail",
            IsPinned: false,
            AppLaunchKind.Internal,
            Categories: ["Network", "Email"],
            SearchTerms: ["Email", "Gmail", "Outlook", "Inbox", "Microsoft 365"]),
        new(
            "org.daisyos.calculator.desktop",
            "Calculator",
            "Perform standard and scientific calculations",
            "org.daisyos.calculator",
            IsPinned: false,
            AppLaunchKind.Internal,
            Categories: ["Utility", "Calculator"]),
        new(
            "org.daisyos.appstore.desktop",
            "App Store",
            "Discover and install applications for DaisyOS",
            "org.daisyos.appstore",
            IsPinned: false,
            AppLaunchKind.Internal,
            Categories: ["System", "PackageManager"]),
        new(
            "org.daisyos.diskmanager.desktop",
            "Disk Manager",
            "Manage disks, partitions and filesystems",
            "org.daisyos.diskmanager",
            IsPinned: false,
            AppLaunchKind.Internal,
            Categories: ["System", "Utility"]),
        new(
            "org.daisyos.appinstaller.desktop",
            "App Installer",
            "Install Linux application packages",
            "org.daisyos.appinstaller",
            IsPinned: false,
            AppLaunchKind.Internal,
            Categories: ["System", "PackageManager"],
            SearchTerms: ["AppImage", "Flatpak", "DEB", "RPM", "Installer", "Package"]),
        new(
            "org.daisyos.taskmanager.desktop",
            "System Monitor",
            "Monitor system performance and manage running processes",
            "org.daisyos.taskmanager",
            IsPinned: false,
            AppLaunchKind.Internal,
            Categories: ["System", "Monitor", "Utility"],
            SearchTerms: ["Task Manager", "Diagnostics", "Performance", "Processes", "CPU", "Memory"]),
        new(
            "org.daisyos.uikit.desktop",
            "UI Kit",
            "Explore DaisyOS reusable interface components and states",
            "org.daisyos.uikit",
            IsPinned: false,
            AppLaunchKind.Internal,
            Categories: ["Development", "Utility"]),
    ];

    private IReadOnlyList<AppEntry> DiscoverApps()
    {
        var maskedIds = DesktopApplicationMaskStore.GetMaskedIds();
        var apps = new List<AppEntry>();
        try
        {
            var catalog = DaisyLaunchClient.Enabled
                ? DaisyLaunchClient.Shared.CatalogAsync().GetAwaiter().GetResult()
                : DaisyLaunchClient.CatalogFallbackAsync().GetAwaiter().GetResult();
            apps.AddRange(catalog.Where(app => !maskedIds.Contains(app.Id)));
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or Tmds.DBus.Protocol.DBusExceptionBase or global::System.Text.Json.JsonException)
        {
            try { apps.AddRange(DaisyLaunchClient.CatalogFallbackAsync().GetAwaiter().GetResult().Where(app => !maskedIds.Contains(app.Id))); }
            catch (Exception fallback) when (fallback is IOException or TimeoutException or global::System.Text.Json.JsonException)
            { _log.Log(LogLevel.Warning, "Native catalog unavailable; using legacy discovery."); }
        }
        var nativeCatalog = apps.Count > 0;
        var seenIds = new HashSet<string>(apps.Select(app => app.Id).Concat(maskedIds), StringComparer.OrdinalIgnoreCase);
        var seenNameAndTarget = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in nativeCatalog ? [] : GetApplicationDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            IEnumerable<string> desktopFiles;
            try
            {
                desktopFiles = Directory.EnumerateFiles(directory, "*.desktop", SearchOption.AllDirectories);
            }
            catch
            {
                desktopFiles = Directory.EnumerateFiles(directory, "*.desktop", SearchOption.TopDirectoryOnly);
            }

            foreach (var file in desktopFiles)
            {
                var fileId = Path.GetFileName(file);
                if (DesktopApplicationMaskStore.IsDesktopFileHiddenOrMasked(file))
                {
                    seenIds.Add(fileId);
                    continue;
                }

                var app = TryReadDesktopEntry(file);
                if (app is null || !seenIds.Add(app.Id))
                {
                    continue;
                }

                // Deduplicate identical apps (e.g. google-chrome.desktop and com.google.Chrome.desktop)
                var nameTargetKey = $"{app.Name}|{Path.GetFileName(app.LaunchTarget)}";
                if (!seenNameAndTarget.Add(nameTargetKey))
                {
                    continue;
                }

                apps.Add(app);
            }
        }

        apps.RemoveAll(app => Applications.OptionalApplicationState.IsRemoved(app.Id));
        foreach (var builtIn in BuiltInSystemApps)
        {
            if (!Applications.OptionalApplicationState.IsPayloadAvailable(builtIn.Id))
            {
                apps.RemoveAll(app => NativeIdsMatch(app.Id, builtIn.Id));
                continue;
            }
            var normalizedId = builtIn.Id.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
                ? builtIn.Id[..^8]
                : builtIn.Id;

            if (maskedIds.Contains(builtIn.Id) || maskedIds.Contains(normalizedId))
            {
                continue;
            }

            // The daemon and installed desktop files can carry metadata from an
            // older shell release. Built-ins are the source of truth for native
            // app names, search aliases, icons, and internal launch routing.
            // Replace every matching catalog entry so System Monitor cannot
            // remain hidden behind the former Task Manager desktop entry.
            var matchingIndexes = apps
                .Select((app, index) => (app, index))
                .Where(item => NativeIdsMatch(item.app.Id, builtIn.Id)
                    || string.Equals(item.app.Name, builtIn.Name, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.index)
                .ToArray();

            if (matchingIndexes.Length > 0)
            {
                apps[matchingIndexes[0]] = builtIn;
                for (var i = matchingIndexes.Length - 1; i > 0; i--)
                    apps.RemoveAt(matchingIndexes[i]);
            }
            else
            {
                apps.Add(builtIn);
            }

            seenIds.Add(builtIn.Id);
            seenIds.Add(normalizedId);
        }

        return apps
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool NativeIdsMatch(string left, string right)
    {
        static string Normalize(string id) => id.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
            ? id[..^8]
            : id;
        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private AppEntry? TryReadDesktopEntry(string file)
    {
        if (Path.GetFileName(file).Equals("winetricks.desktop", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Dictionary<string, string>? entry = null;
        var inDesktopEntry = false;

        try
        {
            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inDesktopEntry = string.Equals(line, "[Desktop Entry]", StringComparison.Ordinal);
                    if (inDesktopEntry)
                    {
                        entry ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }

                    continue;
                }

                if (!inDesktopEntry || entry is null)
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator];
                if (!SupportedKeys.Contains(key) && !key.StartsWith("Name[", StringComparison.OrdinalIgnoreCase) && !key.StartsWith("Comment[", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entry[key] = line[(separator + 1)..].Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Log(LogLevel.Warning, $"Could not read desktop entry: {file}", ex);
            return null;
        }

        if (entry is null
            || !IsDesktopApplication(entry)
            || !TryGet(entry, "Name", out var name)
            || !TryGet(entry, "Exec", out var exec))
        {
            return null;
        }

        if (TryGet(entry, "TryExec", out var tryExec) && !DesktopApplicationMaskStore.CommandExists(tryExec))
        {
            return null;
        }

        var command = DesktopExecParser.Parse(exec);
        if (command is null || command.Count == 0 || !IsSafeCommand(command[0]))
        {
            return null;
        }

        var id = Path.GetFileName(file);
        name = DesktopEntryLocalization.GetValue(entry, "Name") ?? name;
        var description = DesktopEntryLocalization.GetValue(entry, "Comment") ?? "Desktop application";
        var icon = TryGet(entry, "Icon", out var iconValue) ? iconValue : string.Empty;
        var categories = TryGet(entry, "Categories", out var catValue)
            ? catValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        return new AppEntry(
            id,
            name,
            description,
            icon,
            IsPinned: false,
            AppLaunchKind.DesktopEntry,
            command[0],
            command.Skip(1).ToArray(),
            categories);
    }

    private static IEnumerable<string> GetApplicationDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            dataHome = Path.Combine(home, ".local", "share");
        }

        var searchDirs = new List<string>
        {
            Path.Combine(dataHome, "applications"),
            Path.Combine(home, ".local", "share", "applications"),
            Path.Combine(home, ".local", "share", "flatpak", "exports", "share", "applications"),
            "/var/lib/flatpak/exports/share/applications",
            "/var/lib/snapd/desktop/applications",
            "/usr/local/share/applications",
            "/usr/share/applications"
        };

        var xdgDataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS")
            ?.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

        foreach (var dir in xdgDataDirs)
        {
            searchDirs.Add(Path.Combine(dir, "applications"));
        }

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var devAppDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "native", "daisy_window_decoration", "applications"));
            if (Directory.Exists(devAppDir))
            {
                searchDirs.Add(devAppDir);
            }
        }
        catch
        {
            // Ignore path inspection errors
        }

        return searchDirs.Distinct(StringComparer.Ordinal);
    }

    private static bool IsDesktopApplication(Dictionary<string, string> entry)
    {
        return TryGet(entry, "Type", out var type)
            && string.Equals(type, "Application", StringComparison.OrdinalIgnoreCase)
            && !IsTruthy(entry, "NoDisplay")
            && !IsTruthy(entry, "Hidden")
            && !IsTruthy(entry, "Terminal");
    }

    private static bool IsTruthy(Dictionary<string, string> entry, string key)
    {
        return TryGet(entry, key, out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGet(Dictionary<string, string> entry, string key, out string value)
    {
        return entry.TryGetValue(key, out value!) && !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsSafeCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        return Path.IsPathFullyQualified(command) || !command.Contains('/', StringComparison.Ordinal);
    }
}
