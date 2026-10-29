using System.Globalization;
using System.Text;
using System.Text.Json;
using DaisyOS.Core;
using DaisyOS.System.Processes;
using DaisyOS.System.Serialization;

namespace DaisyOS.System.AppImage;

/// <summary>
/// Installs AppImages for the current user without executing them. Embedded
/// metadata is read with unsquashfs so the installer can offer a proper name,
/// icon and desktop entry before the application is ever started.
/// </summary>
public sealed class AppImageService : IAppImageInstallerService
{
    private const long MaximumPayloadScanBytes = 64L * 1024 * 1024;
    private const int MaximumIconBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
    private static readonly byte[] SquashFsMagic = "hsqs"u8.ToArray();
    private readonly ICommandRunner _runner;
    private readonly string _dataHome;
    private readonly string _installRoot;
    private readonly string _applicationsDirectory;
    private readonly string _iconsDirectory;
    private readonly string _registryPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppImageService(ICommandRunner? runner = null, string? dataHome = null)
    {
        _runner = runner ?? new SafeCommandRunner();
        _dataHome = ResolveDataHome(dataHome);
        _installRoot = Path.Combine(_dataHome, "daisyos", "appimages");
        _applicationsDirectory = Path.Combine(_dataHome, "applications");
        _iconsDirectory = Path.Combine(_dataHome, "icons", "hicolor");
        _registryPath = Path.Combine(_installRoot, "installed.json");
    }

    public async Task<AppImageInspection> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var source = ValidateSource(path);
        var type = await ReadTypeAsync(source, cancellationToken).ConfigureAwait(false);
        if (type == AppImageType.Unknown)
            throw new AppImageException("This file is not an AppImage.");

        EmbeddedMetadata? embedded = null;
        if (type == AppImageType.Type2)
        {
            var payload = await FindPayloadAsync(source, cancellationToken).ConfigureAwait(false);
            if (payload is not null)
                embedded = await ReadEmbeddedMetadataAsync(source, payload.Offset, payload.Listing, cancellationToken).ConfigureAwait(false);
        }

        var file = new FileInfo(source);
        var fallbackName = DisplayNameFromFileName(source);
        return new AppImageInspection
        {
            SourcePath = source,
            Name = string.IsNullOrWhiteSpace(embedded?.Name) ? fallbackName : embedded.Name,
            Comment = embedded?.Comment ?? "",
            Version = embedded?.Version ?? "",
            Executable = embedded?.Executable ?? source,
            DesktopId = SanitizeDesktopId(embedded?.DesktopId ?? fallbackName),
            Categories = embedded?.Categories ?? "",
            SizeBytes = file.Length,
            HasEmbeddedMetadata = embedded is not null,
            IconData = embedded?.IconData,
            IconExtension = embedded?.IconExtension ?? ""
        };
    }

    public async Task<AppImageInstallation> InstallAsync(string path, string? destinationDirectory = null, CancellationToken cancellationToken = default)
    {
        var inspection = await InspectAsync(path, cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var registry = await LoadRegistryAsync(cancellationToken).ConfigureAwait(false);
            var desktopId = await ChooseDesktopIdAsync(inspection, registry, cancellationToken).ConfigureAwait(false);
            var appDirectory = string.IsNullOrWhiteSpace(destinationDirectory)
                ? Path.Combine(_installRoot, desktopId)
                : Path.GetFullPath(destinationDirectory);
            Directory.CreateDirectory(appDirectory);

            var appImagePath = Path.Combine(appDirectory, desktopId + ".AppImage");
            if (!string.Equals(inspection.SourcePath, appImagePath, StringComparison.Ordinal))
                await CopyFileAsync(inspection.SourcePath, appImagePath, cancellationToken).ConfigureAwait(false);
            MakeExecutable(appImagePath);

            var iconPath = await WriteIconAsync(desktopId, inspection.IconData, inspection.IconExtension, cancellationToken).ConfigureAwait(false);
            var desktopFilePath = Path.Combine(_applicationsDirectory, desktopId + ".desktop");
            var installation = new AppImageInstallation
            {
                DesktopId = desktopId,
                Name = AppImageDesktopEntry.CleanValue(inspection.Name, DisplayNameFromFileName(inspection.SourcePath)),
                Comment = AppImageDesktopEntry.CleanValue(inspection.Comment, ""),
                Version = AppImageDesktopEntry.CleanValue(inspection.Version, ""),
                Categories = AppImageDesktopEntry.CleanCategories(inspection.Categories),
                AppImagePath = appImagePath,
                DesktopFilePath = desktopFilePath,
                IconPath = iconPath,
                InstalledAt = DateTimeOffset.UtcNow
            };

            var previous = registry.Installations.FirstOrDefault(item => item.DesktopId == desktopId);
            await AtomicTextFile.WriteAsync(desktopFilePath, AppImageDesktopEntry.Build(installation), cancellationToken).ConfigureAwait(false);
            if (previous is not null && !string.Equals(previous.IconPath, iconPath, StringComparison.Ordinal))
                RemoveOwnedIcon(previous.IconPath, desktopId);

            registry.Installations.RemoveAll(item => item.DesktopId == desktopId);
            registry.Installations.Add(installation);
            await SaveRegistryAsync(registry, cancellationToken).ConfigureAwait(false);
            _ = RefreshDesktopDatabaseAsync(cancellationToken);
            return installation;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AppImageInstallation>> GetInstalledAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var registry = await LoadRegistryAsync(cancellationToken).ConfigureAwait(false);
            var valid = registry.Installations
                .Where(item => File.Exists(item.AppImagePath) && File.Exists(item.DesktopFilePath))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            if (valid.Length != registry.Installations.Count)
            {
                registry.Installations = valid.ToList();
                await SaveRegistryAsync(registry, cancellationToken).ConfigureAwait(false);
            }
            return valid;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> UninstallAsync(string desktopId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(desktopId) || !string.Equals(desktopId, SanitizeDesktopId(desktopId), StringComparison.Ordinal))
            return false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var registry = await LoadRegistryAsync(cancellationToken).ConfigureAwait(false);
            var installation = registry.Installations.FirstOrDefault(item => item.DesktopId == desktopId);
            if (installation is null) return false;

            await RemoveOwnedDesktopFileAsync(installation.DesktopFilePath, desktopId, cancellationToken).ConfigureAwait(false);
            RemoveOwnedIcon(installation.IconPath, desktopId);
            RemoveInstallDirectory(desktopId);

            registry.Installations.RemoveAll(item => item.DesktopId == desktopId);
            await SaveRegistryAsync(registry, cancellationToken).ConfigureAwait(false);
            _ = RefreshDesktopDatabaseAsync(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static string SanitizeDesktopId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "appimage";
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
                builder.Append(character);
            else if (char.IsWhiteSpace(character) || character is '/' or '\\' or ':')
                builder.Append('-');
        }

        var id = builder.ToString().Trim('-', '.', '_');
        if (id.Length == 0) return "appimage";
        if (id.Length > 80) id = id[..80].TrimEnd('-', '.', '_');
        return id.Length == 0 ? "appimage" : id;
    }

    internal static string DisplayNameFromFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path)
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();
        name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return name.Length == 0 ? "AppImage" : name;
    }

    private static string ResolveDataHome(string? requested)
    {
        var dataHome = string.IsNullOrWhiteSpace(requested)
            ? Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            : requested;
        if (string.IsNullOrWhiteSpace(dataHome) || !Path.IsPathFullyQualified(dataHome))
        {
            dataHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share");
        }
        return Path.GetFullPath(dataHome);
    }

    private static string ValidateSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new AppImageException("Choose an AppImage file (.AppImage).");

        var source = Path.GetFullPath(path);
        if (!File.Exists(source))
            throw new AppImageException("The selected file could not be found.");
        if (!string.Equals(Path.GetExtension(source), ".AppImage", StringComparison.OrdinalIgnoreCase))
            throw new AppImageException("Choose an AppImage file (.AppImage).");
        if (new FileInfo(source).Length < 16)
            throw new AppImageException("This file is not an AppImage.");
        return source;
    }

    private static async Task<AppImageType> ReadTypeAsync(string path, CancellationToken cancellationToken)
    {
        var header = new byte[16];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var read = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);
        if (read < 11) return AppImageType.Unknown;
        if (header[0] != 0x7F || header[1] != (byte)'E' || header[2] != (byte)'L' || header[3] != (byte)'F')
            return AppImageType.Unknown;
        if (header[8] == (byte)'A' && header[9] == (byte)'I')
        {
            return header[10] switch
            {
                1 => AppImageType.Type1,
                2 => AppImageType.Type2,
                _ => AppImageType.Type2
            };
        }
        return AppImageType.Type2;
    }

    private async Task<Payload?> FindPayloadAsync(string source, CancellationToken cancellationToken)
    {
        var candidates = new List<long>();
        const int chunkSize = 1024 * 1024;
        var buffer = new byte[chunkSize + SquashFsMagic.Length - 1];
        await using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var scanLimit = Math.Min(stream.Length, MaximumPayloadScanBytes);
        long position = 0;
        while (position < scanLimit)
        {
            var requested = (int)Math.Min(buffer.Length, scanLimit - position);
            var read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
            if (read <= 0) break;
            for (var index = 0; index <= read - SquashFsMagic.Length; index++)
            {
                if (buffer[index] != SquashFsMagic[0] || buffer[index + 1] != SquashFsMagic[1] ||
                    buffer[index + 2] != SquashFsMagic[2] || buffer[index + 3] != SquashFsMagic[3])
                    continue;
                var candidate = position + index;
                if (!candidates.Contains(candidate)) candidates.Add(candidate);
            }
            if (read < SquashFsMagic.Length) break;
            position += read - (SquashFsMagic.Length - 1);
            stream.Seek(position, SeekOrigin.Begin);
        }

        // AppImage payloads are page-aligned. Check aligned candidates first,
        // then keep the unaligned ones as a defensive fallback.
        var ordered = candidates
            .OrderBy(candidate => candidate % 4096 == 0 ? 0 : 1)
            .ThenBy(candidate => candidate)
            .Take(12)
            .ToArray();
        foreach (var candidate in ordered)
        {
            var listing = await RunAsync("unsquashfs",
                ["-no-progress", "-o", candidate.ToString(CultureInfo.InvariantCulture), "-llc", source],
                cancellationToken).ConfigureAwait(false);
            if (listing.Succeeded) return new Payload(candidate, listing.StandardOutput);
            if (listing.ExitCode == 127) return null;
        }
        return null;
    }

    private async Task<EmbeddedMetadata?> ReadEmbeddedMetadataAsync(
        string source,
        long offset,
        string listing,
        CancellationToken cancellationToken)
    {
        var entries = ParseListing(listing);
        var desktopPath = SelectDesktop(entries);
        if (desktopPath is null) return null;

        var desktopText = await RunAsync("unsquashfs",
            ["-no-progress", "-o", offset.ToString(CultureInfo.InvariantCulture), "-cat", source, desktopPath],
            cancellationToken).ConfigureAwait(false);
        if (!desktopText.Succeeded) return null;

        var fields = AppImageDesktopEntry.Parse(desktopText.StandardOutput);
        var name = AppImageDesktopEntry.CleanValue(
            fields.GetValueOrDefault("Name") ?? fields.GetValueOrDefault("X-AppImage-Name"), "");
        if (name.Length == 0) return null;

        var comment = AppImageDesktopEntry.CleanValue(fields.GetValueOrDefault("Comment"), "");
        // Desktop Entry Version is the file-format version, not the app version.
        var version = AppImageDesktopEntry.CleanValue(fields.GetValueOrDefault("X-AppImage-Version"), "");
        var categories = AppImageDesktopEntry.CleanCategories(fields.GetValueOrDefault("Categories"));
        var executable = FirstExecToken(fields.GetValueOrDefault("Exec"));
        var desktopId = SanitizeDesktopId(Path.GetFileNameWithoutExtension(desktopPath));
        var icon = await ReadIconAsync(source, offset, entries, fields, desktopPath, cancellationToken).ConfigureAwait(false);

        return new EmbeddedMetadata(name, comment, version, executable, categories, desktopId, icon.Data, icon.Extension);
    }

    private async Task<(byte[]? Data, string Extension)> ReadIconAsync(
        string source,
        long offset,
        IReadOnlyList<AppImageEntry> entries,
        IReadOnlyDictionary<string, string> fields,
        string desktopPath,
        CancellationToken cancellationToken)
    {
        var iconPath = ResolveIconPath(entries, fields, desktopPath);
        if (iconPath is null) return (null, "");
        var resolved = ResolveSymlink(entries, iconPath);
        if (resolved is null) return (null, "");

        var extension = Path.GetExtension(resolved).ToLowerInvariant();
        if (extension is not (".png" or ".svg" or ".xpm")) return (null, "");
        var data = await ExtractFileAsync(source, offset, resolved, cancellationToken).ConfigureAwait(false);
        return data is { Length: > 0 and <= MaximumIconBytes } ? (data, extension) : (null, "");
    }

    private static IReadOnlyList<AppImageEntry> ParseListing(string output)
    {
        var entries = new List<AppImageEntry>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("Parallel unsquashfs", StringComparison.Ordinal) ||
                line.StartsWith("Found a valid", StringComparison.Ordinal))
                continue;

            var linkIndex = line.IndexOf(" -> ", StringComparison.Ordinal);
            var pathPart = (linkIndex >= 0 ? line[..linkIndex] : line).Trim();
            var targetPart = linkIndex >= 0 ? line[(linkIndex + 4)..].Trim() : null;

            var rootIndex = pathPart.IndexOf("squashfs-root/", StringComparison.Ordinal);
            if (rootIndex >= 0)
                pathPart = pathPart[(rootIndex + "squashfs-root/".Length)..];
            else if (pathPart.EndsWith("squashfs-root", StringComparison.Ordinal))
                continue;

            var path = ResolveRelative("", pathPart);
            if (path is null) continue;

            string? target = null;
            if (!string.IsNullOrWhiteSpace(targetPart))
            {
                var targetRootIndex = targetPart.IndexOf("squashfs-root/", StringComparison.Ordinal);
                if (targetRootIndex >= 0)
                    targetPart = targetPart[(targetRootIndex + "squashfs-root/".Length)..];
                target = targetPart.StartsWith('/')
                    ? ResolveRelative("", targetPart)
                    : ResolveRelative(Path.GetDirectoryName(path) ?? "", targetPart);
            }
            entries.Add(new AppImageEntry(path, target));
        }
        return entries;
    }

    private static string? SelectDesktop(IReadOnlyList<AppImageEntry> entries) =>
        entries
            .Where(entry => entry.Path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => DesktopScore(entry.Path))
            .ThenBy(entry => entry.Path.Length)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal)
            .Select(entry => entry.Path)
            .FirstOrDefault();

    private static int DesktopScore(string path)
    {
        if (!path.Contains('/')) return 0;
        if (path.StartsWith("usr/share/applications/", StringComparison.Ordinal)) return 1;
        if (path.StartsWith("usr/local/share/applications/", StringComparison.Ordinal)) return 2;
        if (path.Contains("/applications/", StringComparison.Ordinal)) return 3;
        return 4;
    }

    private static string? ResolveIconPath(
        IReadOnlyList<AppImageEntry> entries,
        IReadOnlyDictionary<string, string> fields,
        string desktopPath)
    {
        var dirIcon = entries.FirstOrDefault(entry =>
            string.Equals(entry.Path, ".DirIcon", StringComparison.Ordinal))?.Path;
        if (dirIcon is not null)
        {
            var resolvedDir = ResolveSymlink(entries, dirIcon);
            if (resolvedDir is not null && entries.Any(e => string.Equals(e.Path, resolvedDir, StringComparison.Ordinal)))
            {
                return resolvedDir;
            }
        }

        var declared = fields.GetValueOrDefault("Icon")?.Trim();
        if (!string.IsNullOrWhiteSpace(declared))
        {
            var resolved = ResolveIconReference(entries, declared, desktopPath);
            if (resolved is not null) return resolved;
        }

        var desktopName = Path.GetFileNameWithoutExtension(desktopPath);
        return ResolveIconReference(entries, desktopName, desktopPath);
    }

    private static int IconScore(string path)
    {
        if (path.StartsWith("usr/share/icons/hicolor/1024x1024/apps/", StringComparison.Ordinal)) return 1;
        if (path.StartsWith("usr/share/icons/hicolor/512x512/apps/", StringComparison.Ordinal) ||
            path.StartsWith("usr/share/icons/hicolor/512x512@2/apps/", StringComparison.Ordinal)) return 2;
        if (path.StartsWith("usr/share/icons/hicolor/256x256/apps/", StringComparison.Ordinal) ||
            path.StartsWith("usr/share/icons/hicolor/256x256@2/apps/", StringComparison.Ordinal)) return 3;
        if (path.StartsWith("usr/share/icons/hicolor/128x128/apps/", StringComparison.Ordinal) ||
            path.StartsWith("usr/share/icons/hicolor/128x128@2/apps/", StringComparison.Ordinal)) return 4;
        if (path.StartsWith("usr/share/icons/hicolor/scalable/apps/", StringComparison.Ordinal)) return 5;
        if (path.StartsWith("usr/share/icons/hicolor/64x64/apps/", StringComparison.Ordinal) ||
            path.StartsWith("usr/share/icons/hicolor/64x64@2/apps/", StringComparison.Ordinal)) return 6;
        if (path.StartsWith("usr/share/icons/hicolor/48x48/apps/", StringComparison.Ordinal)) return 7;
        if (!path.Contains('/')) return 8;
        if (path.Contains("/icons/hicolor/", StringComparison.Ordinal)) return 9;
        if (path.Contains("/icons/", StringComparison.Ordinal)) return 10;
        return 11;
    }

    private static string? ResolveIconReference(
        IReadOnlyList<AppImageEntry> entries,
        string reference,
        string desktopPath)
    {
        if (reference.StartsWith('/'))
            reference = reference.TrimStart('/');

        if (reference.Contains('/'))
        {
            var direct = ResolveRelative("", reference);
            return direct is not null && entries.Any(entry => entry.Path == direct) ? direct : null;
        }

        var extensions = new[] { ".png", ".svg", ".xpm" };
        var candidates = new List<(string Path, int Score)>();
        foreach (var extension in extensions)
        {
            var direct = ResolveRelative(Path.GetDirectoryName(desktopPath) ?? "", reference + extension);
            if (direct is not null && entries.Any(entry => entry.Path == direct))
                candidates.Add((direct, IconScore(direct)));
        }

        foreach (var entry in entries)
        {
            if (!string.Equals(Path.GetFileNameWithoutExtension(entry.Path), reference, StringComparison.OrdinalIgnoreCase))
                continue;
            var extension = Path.GetExtension(entry.Path).ToLowerInvariant();
            if (extension is not (".png" or ".svg" or ".xpm")) continue;
            candidates.Add((entry.Path, IconScore(entry.Path)));
        }

        var chosen = candidates
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path.Length)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();

        if (chosen is null) return null;
        var resolved = ResolveSymlink(entries, chosen);
        return resolved is not null && entries.Any(e => string.Equals(e.Path, resolved, StringComparison.Ordinal))
            ? resolved
            : chosen;
    }

    private static string? ResolveSymlink(IReadOnlyList<AppImageEntry> entries, string path)
    {
        var byPath = new Dictionary<string, AppImageEntry>(StringComparer.Ordinal);
        foreach (var entry in entries) byPath.TryAdd(entry.Path, entry);
        var current = path;
        for (var depth = 0; depth < 8; depth++)
        {
            if (!byPath.TryGetValue(current, out var entry) || entry.LinkTarget is null) return current;
            current = entry.LinkTarget;
        }
        return null;
    }

    private async Task<byte[]?> ExtractFileAsync(
        string source,
        long offset,
        string imagePath,
        CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "daisyos-appimage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var extraction = await RunAsync("unsquashfs",
                ["-no-xattrs", "-no-progress", "-o", offset.ToString(CultureInfo.InvariantCulture), "-d", temporary, "-f", source, imagePath],
                cancellationToken).ConfigureAwait(false);
            if (!extraction.Succeeded && extraction.ExitCode != 2) return null;

            var extracted = ResolveWithin(temporary, imagePath);
            if (extracted is null) return null;
            if (File.Exists(extracted))
                return await File.ReadAllBytesAsync(extracted, cancellationToken).ConfigureAwait(false);

            var prefixed = ResolveWithin(Path.Combine(temporary, "squashfs-root"), imagePath);
            return prefixed is not null && File.Exists(prefixed)
                ? await File.ReadAllBytesAsync(prefixed, cancellationToken).ConfigureAwait(false)
                : null;
        }
        finally
        {
            try { Directory.Delete(temporary, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string? ResolveWithin(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? fullPath
            : null;
    }

    private static string? ResolveRelative(string baseDirectory, string path)
    {
        var combined = string.IsNullOrEmpty(baseDirectory)
            ? path
            : baseDirectory.TrimEnd('/') + "/" + path;
        var parts = new List<string>();
        foreach (var part in combined.Replace('\\', '/').Split('/'))
        {
            if (part.Length == 0 || part == ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) return null;
                parts.RemoveAt(parts.Count - 1);
                continue;
            }
            if (part.Contains('\0')) return null;
            parts.Add(part);
        }
        return parts.Count == 0 ? null : string.Join('/', parts);
    }

    private static string FirstExecToken(string? exec)
    {
        if (string.IsNullOrWhiteSpace(exec)) return "";
        var value = exec.Trim();
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            return end > 1 ? value[1..end] : value.Trim('"');
        }
        var space = value.IndexOf(' ');
        return space > 0 ? value[..space] : value;
    }

    private async Task<string> ChooseDesktopIdAsync(
        AppImageInspection inspection,
        AppImageRegistryDocument registry,
        CancellationToken cancellationToken)
    {
        var baseId = SanitizeDesktopId(inspection.DesktopId);
        for (var index = 0; index < 1000; index++)
        {
            var id = index == 0 ? baseId : baseId + "-" + (index + 1).ToString(CultureInfo.InvariantCulture);
            var desktopPath = Path.Combine(_applicationsDirectory, id + ".desktop");
            if (!File.Exists(desktopPath)) return id;
            if (registry.Installations.Any(item => item.DesktopId == id)) return id;
            try
            {
                var text = await File.ReadAllTextAsync(desktopPath, cancellationToken).ConfigureAwait(false);
                if (AppImageDesktopEntry.IsOwnedBy(text, id)) return id;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        throw new AppImageException("The AppImage could not be installed.");
    }

    private async Task<string> WriteIconAsync(
        string desktopId,
        byte[]? data,
        string extension,
        CancellationToken cancellationToken)
    {
        if (data is not { Length: > 0 }) return "";
        var (directory, fileName) = extension.ToLowerInvariant() switch
        {
            ".svg" => (Path.Combine(_iconsDirectory, "scalable", "apps"), desktopId + ".svg"),
            ".xpm" => (Path.Combine(_iconsDirectory, "48x48", "apps"), desktopId + ".xpm"),
            ".png" => (Path.Combine(_iconsDirectory, "1024x1024", "apps"), desktopId + ".png"),
            _ => ("", "")
        };
        if (directory.Length == 0) return "";

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, data, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return path;
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsLinux()) return;
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static async Task RemoveOwnedDesktopFileAsync(
        string path,
        string desktopId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path)) return;
            var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (AppImageDesktopEntry.IsOwnedBy(text, desktopId)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void RemoveOwnedIcon(string path, string desktopId)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path);
        var iconsRoot = Path.GetFullPath(_iconsDirectory);
        if (!fullPath.StartsWith(iconsRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return;
        var fileName = Path.GetFileName(fullPath);
        if (!string.Equals(Path.GetFileNameWithoutExtension(fileName), desktopId, StringComparison.Ordinal)) return;
        try { File.Delete(fullPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void RemoveInstallDirectory(string desktopId)
    {
        var directory = Path.GetFullPath(Path.Combine(_installRoot, desktopId));
        var root = Path.GetFullPath(_installRoot);
        if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return;
        if (!string.Equals(Path.GetFileName(directory), desktopId, StringComparison.Ordinal)) return;
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<AppImageRegistryDocument> LoadRegistryAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_registryPath)) return new AppImageRegistryDocument();
        try
        {
            await using var stream = new FileStream(_registryPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync(
                stream,
                DaisySystemJsonContext.Default.AppImageRegistryDocument,
                cancellationToken).ConfigureAwait(false) ?? new AppImageRegistryDocument();
        }
        catch (JsonException) { return new AppImageRegistryDocument(); }
        catch (IOException) { return new AppImageRegistryDocument(); }
        catch (UnauthorizedAccessException) { return new AppImageRegistryDocument(); }
    }

    private async Task SaveRegistryAsync(AppImageRegistryDocument registry, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(registry, DaisySystemJsonContext.Default.AppImageRegistryDocument);
        await AtomicTextFile.WriteAsync(_registryPath, json, cancellationToken).ConfigureAwait(false);
    }

    private Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        _runner.RunAsync(fileName, arguments, CommandTimeout, cancellationToken);

    private async Task RefreshDesktopDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (Directory.Exists(_applicationsDirectory))
                await _runner.RunAsync("update-desktop-database", [_applicationsDirectory], TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private enum AppImageType
    {
        Unknown,
        Type1,
        Type2
    }

    private sealed record Payload(long Offset, string Listing);

    private sealed record AppImageEntry(string Path, string? LinkTarget);

    private sealed record EmbeddedMetadata(
        string Name,
        string Comment,
        string Version,
        string Executable,
        string Categories,
        string DesktopId,
        byte[]? IconData,
        string IconExtension);
}
