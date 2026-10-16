using System.Globalization;
using System.Text.Json;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Processes;

namespace DaisyOS.System.AppStore;

/// <summary>Flathub metadata, scoped Flatpak transactions and ownership-based native application removal.</summary>
public sealed partial class LinuxAppStoreService : IAppStoreService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly ICommandRunner _runner;
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private readonly SemaphoreSlim _transactionGate = new(1, 1);
    private readonly string? _catalogOverride;
    private readonly IReadOnlyList<string> _desktopDirectories;
    private string _locale = CultureInfo.CurrentUICulture.Name;
    private IReadOnlyList<AppStoreItem>? _catalog;
    private readonly ConcurrentDictionary<string, AppPackageInfo> _packageInfo = new(StringComparer.Ordinal);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(20);
    private static string ScopeArgument(string scope) => scope is "user" or "system" ? "--" + scope : "--installation=" + scope;
    private static readonly TimeSpan TransactionTimeout = TimeSpan.FromMinutes(30);
    private static string Architecture => RuntimeInformation.ProcessArchitecture == global::System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x86_64";
    private static string CacheFile => Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "daisyos", "flathub", $"{Architecture}.xml.gz");

    public LinuxAppStoreService(ICommandRunner commandRunner, string? catalogPath = null, IReadOnlyList<string>? desktopDirectories = null)
    {
        _runner = commandRunner; _catalogOverride = catalogPath;
        _desktopDirectories = desktopDirectories ?? (catalogPath is not null ? [] : DefaultDesktopDirectories());
    }
    private static string[] DefaultDesktopDirectories() =>
        new[] { Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "applications") }
        .Concat((Environment.GetEnvironmentVariable("XDG_DATA_DIRS") ?? "/usr/local/share:/usr/share").Split(':', StringSplitOptions.RemoveEmptyEntries).Select(p => Path.Combine(p, "applications"))).ToArray();
    private HashSet<string> NativeDesktopFiles()
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in _desktopDirectories)
        {
            try { if (Directory.Exists(directory)) files.UnionWith(Directory.EnumerateFiles(directory, "*.desktop")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return files;
    }

    private AppStoreItem WithNativeState(AppStoreItem item, IReadOnlySet<string>? desktopFiles = null)
    {
        if (item.InstallState != AppInstallState.NotInstalled) return item;
        var desktopId = string.IsNullOrEmpty(item.DesktopFileId) ? item.Id + ".desktop" : item.DesktopFileId;
        if (Path.GetFileName(desktopId) != desktopId || !desktopId.EndsWith(".desktop", StringComparison.Ordinal)) return item;
        var nativeAlias = item.Id switch
        {
            "org.mozilla.firefox" => "firefox.desktop", "org.gimp.GIMP" => "gimp.desktop",
            "org.videolan.VLC" => "vlc.desktop", "org.libreoffice.LibreOffice" => "libreoffice-startcenter.desktop",
            "com.valvesoftware.Steam" => "steam.desktop", "com.spotify.Client" => "spotify.desktop", _ => desktopId
        };
        foreach (var directory in _desktopDirectories)
        foreach (var candidate in new[] { desktopId, nativeAlias }.Distinct())
        {
            var file = Path.Combine(directory, candidate);
            if (desktopFiles != null && !desktopFiles.Contains(file)) continue;
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length > 256_000) continue;
                var lines = File.ReadAllLines(file);
                var entry = lines.SkipWhile(l => l.Trim() != "[Desktop Entry]").Skip(1).TakeWhile(l => !l.StartsWith('[')).ToArray();
                if (entry.Contains("Hidden=true")) return item;
                if (!entry.Contains("Type=Application") || !entry.Any(l => l.StartsWith("Exec=")) || entry.Any(l => l.StartsWith("X-Flatpak="))) continue;
                return item with { InstallState = AppInstallState.Installed, InstallationScope = "native", NativeDesktopFile = file, Version = "" };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return item;
    }

    internal static bool ValidAppId(string id) => id.Length <= 255 && AppIdRegex().IsMatch(id);
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_-]*(?:\.[A-Za-z_][A-Za-z0-9_-]*){2,}\z")]
    private static partial Regex AppIdRegex();

    private async Task<IReadOnlyList<AppStoreItem>> CatalogAsync(CancellationToken token, bool allowDownload = true)
    {
        // Library navigation must not queue behind a potentially slow catalog download.
        if (!allowDownload)
        {
            if (!await _catalogGate.WaitAsync(0, token).ConfigureAwait(false)) return _catalog ?? [];
        }
        else await _catalogGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var locale = CultureInfo.CurrentUICulture.Name;
            if (_locale != locale) { _locale = locale; _catalog = null; }
            if (_catalog != null) return _catalog;
            var candidates = _catalogOverride != null ? [_catalogOverride] : new[] { CacheFile,
                Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share"),
                    "flatpak/appstream/flathub", Architecture, "active/appstream.xml"),
                $"/var/lib/flatpak/appstream/flathub/{Architecture}/active/appstream.xml" };
            Exception? failure = null;
            foreach (var path in candidates.Where(p => p != null && File.Exists(p)))
            {
                try { _catalog = await ReadCatalogAsync(path!, token).ConfigureAwait(false); return _catalog; }
                catch (Exception ex) when (ex is IOException or global::System.Xml.XmlException or InvalidDataException) { failure = ex; }
            }
            if (_catalogOverride != null) throw new AppStoreException("The application catalog could not be read.", failure);
            if (!allowDownload) return [];
            await DownloadCatalogAsync(token).ConfigureAwait(false);
            _catalog = await ReadCatalogAsync(CacheFile, token).ConfigureAwait(false);
            return _catalog;
        }
        finally { _catalogGate.Release(); }
    }
    private Task<IReadOnlyList<AppStoreItem>> ReadCatalogAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        using var file = File.OpenRead(path);
        using Stream stream = path.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(file, CompressionMode.Decompress) : file;
        var result = FlathubCatalog.Read(stream, _locale, Path.Combine(Path.GetDirectoryName(path)!, "icons"), token);
        if (result.Count == 0) throw new InvalidDataException("The Flathub catalog contains no applications.");
        return result;
    }, token);

    private async Task DownloadCatalogAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
        var temporary = CacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var response = await Http.GetAsync($"https://dl.flathub.org/repo/appstream/{Architecture}/appstream.xml.gz",
                HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[65536]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > 32_000_000) throw new InvalidDataException("The Flathub catalog exceeds the download limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                }
            }
            // Validate before replacing the last usable cache.
            await Task.Run(() =>
            {
                using var file = File.OpenRead(temporary);
                using var zip = new GZipStream(file, CompressionMode.Decompress);
                if (FlathubCatalog.Read(zip, _locale, cancellationToken: token).Count == 0)
                    throw new InvalidDataException("The Flathub catalog contains no applications.");
            }, token).ConfigureAwait(false);
            File.Move(temporary, CacheFile, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task RefreshCatalogAsync(CancellationToken cancellationToken = default)
    {
        await _catalogGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_catalogOverride == null) await DownloadCatalogAsync(cancellationToken).ConfigureAwait(false);
            _catalog = null; _packageInfo.Clear();
        }
        finally { _catalogGate.Release(); }
    }
    private sealed record Installed(string Id, string Name, string Version, string Origin, string Scope);
    private async Task<IReadOnlyList<Installed>> InstalledAsync(CancellationToken token, bool required = false)
    {
        var result = await _runner.RunAsync("flatpak", ["list", "--app", "--columns=application,name,version,origin,installation"], QueryTimeout, token).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            if (required) throw new AppStoreException(result.ExitCode == 127 ? "Flatpak is not installed. Install Flatpak to manage applications." : "Installed applications could not be read. Try again.");
            return [];
        }
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('\t'))
            .Where(parts => parts.Length >= 5 && ValidAppId(parts[0]))
            .Select(p => new Installed(p[0], p[1], p[2], p[3], p[4]))
            .GroupBy(a => a.Id).Select(g => g.OrderBy(a => a.Scope == "user" ? 0 : 1).First()).ToArray();
    }
    private async Task<IReadOnlyList<AppStoreItem>> WithStateAsync(IEnumerable<AppStoreItem> apps, CancellationToken token)
    {
        var installed = (await InstalledAsync(token).ConfigureAwait(false)).ToDictionary(a => a.Id);
        var desktopFiles = NativeDesktopFiles();
        return apps.Select(a => installed.TryGetValue(a.Id, out var i)
            ? a with { InstallState = AppInstallState.Installed, InstallationScope = i.Scope, Origin = i.Origin, Version = i.Version } : WithNativeState(a, desktopFiles)).ToArray();
    }
    public async Task<IReadOnlyList<AppStoreItem>> GetFeaturedAppsAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await CatalogAsync(cancellationToken).ConfigureAwait(false);
        return await WithStateAsync(catalog.Where(a => a.IsFeatured).OrderBy(a => Array.IndexOf(FlathubCatalog.Picks, a.Id)), cancellationToken).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<AppStoreItem>> GetAppsByCategoryAsync(AppCategory category, CancellationToken cancellationToken = default)
    {
        if (category == AppCategory.Installed) return await GetInstalledAppsAsync(cancellationToken).ConfigureAwait(false);
        if (category == AppCategory.Updates) return await GetUpdatesAsync(cancellationToken).ConfigureAwait(false);
        var catalog = await CatalogAsync(cancellationToken).ConfigureAwait(false);
        var apps = catalog.Where(a => category == AppCategory.Discover || a.Category == category)
            .OrderByDescending(a => a.IsFeatured).ThenByDescending(a => a.IsVerified).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
        return await WithStateAsync(apps, cancellationToken).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<AppStoreItem>> SearchAppsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return await GetAppsByCategoryAsync(AppCategory.Discover, cancellationToken).ConfigureAwait(false);
        var words = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var catalog = await CatalogAsync(cancellationToken).ConfigureAwait(false);
        int Score(AppStoreItem a) => a.Name.Equals(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 100
            : a.Name.StartsWith(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 80
            : a.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 60 : a.IsVerified ? 30 : 20;
        var matches = catalog.Where(a => words.All(word =>
            $"{a.Name} {a.Summary} {a.Id} {a.Publisher} {string.Join(' ', a.Keywords)} {string.Join(' ', a.Categories)}".Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(Score).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
        return await WithStateAsync(matches, cancellationToken).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<AppStoreItem>> GetInstalledAppsAsync(CancellationToken cancellationToken = default)
    {
        var installed = await InstalledAsync(cancellationToken, required: true).ConfigureAwait(false);
        if (installed.Count == 0 && _desktopDirectories.Count == 0) return [];
        IReadOnlyList<AppStoreItem> catalog;
        try { catalog = await CatalogAsync(cancellationToken, allowDownload: false).ConfigureAwait(false); }
        catch (Exception ex) when (ex is AppStoreException or IOException or HttpRequestException or global::System.Xml.XmlException or TaskCanceledException && !cancellationToken.IsCancellationRequested) { catalog = []; }
        var lookup = catalog.ToDictionary(a => a.Id);
        var desktopFiles = NativeDesktopFiles();
        return installed.Select(i => (lookup.GetValueOrDefault(i.Id) ?? new AppStoreItem(i.Id, i.Id, i.Name, "Installed application", "", AppCategory.Utilities,
            "", "package", i.Version, "", "", 0, false, false)) with
            { InstallState = AppInstallState.Installed, Version = i.Version, InstallationScope = i.Scope, Origin = i.Origin })
            .Concat(catalog.Select(a => WithNativeState(a, desktopFiles)).Where(a => a.NativeDesktopFile.Length > 0 && !installed.Any(i => i.Id == a.Id)))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    public async Task<IReadOnlyList<AppStoreItem>> GetUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var installed = await GetInstalledAppsAsync(cancellationToken).ConfigureAwait(false);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in installed.Where(a => a.NativeDesktopFile.Length == 0).Select(a => a.InstallationScope).Distinct())
        {
            var result = await _runner.RunAsync("flatpak", ["remote-ls", ScopeArgument(scope), "--updates", "--app", "--columns=application"], QueryTimeout, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded) throw new AppStoreException("Updates could not be checked. Check your connection and try again.");
            foreach (var id in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)) ids.Add(id.Trim());
        }
        return installed.Where(a => ids.Contains(a.Id)).Select(a => a with { InstallState = AppInstallState.UpdateAvailable }).ToArray();
    }
    public async Task<AppPackageInfo?> GetPackageInfoAsync(string appId, CancellationToken cancellationToken = default)
    {
        if (!ValidAppId(appId)) return null;
        if (_packageInfo.TryGetValue(appId, out var cached)) return cached;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await Http.GetAsync("https://flathub.org/api/v2/summary/" + Uri.EscapeDataString(appId),
            HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == global::System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream(); var bytes = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(bytes, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > 2_000_000) throw new InvalidDataException("Application details exceed the response limit.");
            buffer.Write(bytes, 0, count);
        }
        using var json = JsonDocument.Parse(buffer.ToArray());
        var info = ParsePackageInfo(json.RootElement);
        _packageInfo[appId] = info;
        return info;
    }
    internal static AppPackageInfo ParsePackageInfo(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new AppStoreException("Download information is unavailable.");
        long Number(JsonElement e, string key) => e.TryGetProperty(key, out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out var number) ? Math.Max(0, number) : 0;
        var permissions = new List<string>(); long runtimeSize = 0; bool endOfLife = false;
        if (root.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
        {
            runtimeSize = Number(metadata, "runtimeInstalledSize");
            endOfLife = metadata.TryGetProperty("runtimeIsEol", out var eol) && eol.ValueKind == JsonValueKind.True;
            if (metadata.TryGetProperty("permissions", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                string[] Values(string key) => p.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array
                    ? array.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray() : [];
                if (Values("shared").Contains("network")) permissions.Add("Internet access");
                if (Values("sockets").Contains("pulseaudio")) permissions.Add("Audio playback and recording");
                var devices = Values("devices");
                if (devices.Contains("all")) permissions.Add("Device access");
                else if (devices.Contains("dri")) permissions.Add("Graphics acceleration");
                var files = Values("filesystems").Select(v => v.Split(':')[0]).ToArray();
                if (files.Contains("host") || files.Contains("host-os")) permissions.Add("Access to files on your computer");
                else if (files.Contains("home") || files.Contains("~")) permissions.Add("Access to your home folder");
                if (files.Contains("xdg-download")) permissions.Add("Access to your Downloads folder");
            }
        }
        return new AppPackageInfo(Number(root, "download_size"), Number(root, "installed_size"), runtimeSize, permissions, endOfLife);
    }
    public async Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default) =>
        ValidAppId(packageName) && (await InstalledAsync(cancellationToken).ConfigureAwait(false)).Any(a => a.Id == packageName);

    public Task<bool> InstallAppAsync(string packageName, Action<double>? progressCallback = null, CancellationToken cancellationToken = default)
        => TransactionAsync(packageName, "install", progressCallback, cancellationToken);
    public Task<bool> UninstallAppAsync(string packageName, CancellationToken cancellationToken = default)
        => TransactionAsync(packageName, "uninstall", null, cancellationToken);
    public Task<bool> UpdateAppAsync(string appId, Action<double>? progressCallback = null, CancellationToken cancellationToken = default)
        => TransactionAsync(appId, "update", progressCallback, cancellationToken);
    private async Task<bool> TransactionAsync(string id, string operation, Action<double>? progress, CancellationToken token)
    {
        if (!ValidAppId(id)) throw new ArgumentException("Invalid Flatpak application ID.", nameof(id));
        await _transactionGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var installed = await InstalledAsync(token, required: true).ConfigureAwait(false);
            var existing = installed.FirstOrDefault(a => a.Id == id);
            if (existing == null && operation == "uninstall")
            {
                var catalog = await CatalogAsync(token, allowDownload: false).ConfigureAwait(false);
                var native = catalog.FirstOrDefault(a => a.Id == id) is { } item ? WithNativeState(item) : null;
                if (native?.NativeDesktopFile.Length > 0)
                {
                    var package = await NativeApplicationPackages.FindOwnerAsync(_runner, native.NativeDesktopFile, token).ConfigureAwait(false);
                    if (package == null) throw new AppStoreException("This application is not owned by a supported Pacman or Flatpak package.");
                    var removal = await NativeApplicationPackages.RemoveAsync(_runner, package, token).ConfigureAwait(false);
                    if (!removal.Succeeded) throw new AppStoreException("The operation could not finish. Check your connection, available storage and permissions, then try again.");
                    return true;
                }
            }
            var scope = existing?.Scope ?? "user";
            if (operation != "install" && existing == null) throw new AppStoreException("This application is no longer installed. Refresh the library.");
            if (operation == "install")
            {
                if (existing != null) { progress?.Invoke(1); return true; }
                var remote = await _runner.RunAsync("flatpak", ["remote-add", "--user", "--if-not-exists", "flathub",
                    "https://dl.flathub.org/repo/flathub.flatpakrepo"], QueryTimeout, token).ConfigureAwait(false);
                if (!remote.Succeeded) throw new AppStoreException("Flathub could not be configured. Check your connection and try again.");
            }
            progress?.Invoke(0);
            List<string> arguments = [operation, ScopeArgument(scope), "--noninteractive", "-y"];
            if (operation == "install") arguments.Add("flathub");
            arguments.Add(id);
            var result = await _runner.RunAsync("flatpak", arguments, TransactionTimeout, token).ConfigureAwait(false);
            if (!result.Succeeded) throw new AppStoreException(result.TimedOut
                ? "The operation took too long. Check your connection and try again."
                : "The operation could not finish. Check your connection, available storage and permissions, then try again.");
            progress?.Invoke(1);
            return true;
        }
        finally { _transactionGate.Release(); }
    }
}
