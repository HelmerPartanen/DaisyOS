using System.Globalization;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Storage;

public sealed record StorageUsageCategory(string Name, long Bytes, IReadOnlyList<string>? Paths = null, IReadOnlyList<string>? ExcludedPaths = null);
public sealed record StorageFileUsage(string Path, long Bytes, bool IsDirectory);
public sealed record StorageDriveOverview(string Path, string Name, long Capacity, long Used, IReadOnlyList<StorageUsageCategory> Categories);

/// <summary>Visible volumes and allocated file sizes; virtual/firmware mounts stay out of Settings.</summary>
public static class LinuxStorageOverview
{
    public static bool IsUserVolume(string path) => path == "/" || path == "/home" ||
        path.StartsWith("/mnt/", StringComparison.Ordinal) || path.StartsWith("/media/", StringComparison.Ordinal) ||
        path.StartsWith("/run/media/", StringComparison.Ordinal) ||
        (OperatingSystem.IsWindows() && path.Length >= 2 && path[1] == ':');

    public static IReadOnlyList<StorageDriveOverview> ReadDrives() => DriveInfo.GetDrives()
        .Where(d => d.IsReady && IsUserVolume(d.RootDirectory.FullName.TrimEnd('/') is { Length: > 0 } name ? name : "/"))
        .OrderBy(d => d.RootDirectory.FullName == "/" ? 0 : 1)
        .Select(d => new StorageDriveOverview(d.RootDirectory.FullName,
            d.RootDirectory.FullName == "/" ? "Local disk" : (string.IsNullOrWhiteSpace(d.VolumeLabel) || d.VolumeLabel.StartsWith('/') ? Path.GetFileName(d.RootDirectory.FullName.TrimEnd('/')) : d.VolumeLabel),
            d.TotalSize, Math.Max(0, d.TotalSize - d.TotalFreeSpace), []))
        .ToArray();

    public static async Task<IReadOnlyList<StorageDriveOverview>> MeasureAsync(IReadOnlyList<StorageDriveOverview> drives,
        IReadOnlyDictionary<string, string> userDirectories, CancellationToken token, ICommandRunner? runner = null)
    {
        runner ??= new SafeCommandRunner();
        var groups = CategoryPaths(userDirectories);
        var result = new List<StorageDriveOverview>();
        foreach (var drive in drives)
        {
            var paths = groups.Values.SelectMany(p => p).Distinct(StringComparer.Ordinal)
                .Where(Directory.Exists).Where(p => Owner(p, drives)?.Path == drive.Path).ToArray();
            var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
            if (paths.Length > 0)
            {
                var scan = await runner.RunAsync("du", ["-s", "-x", "-B1", "--", .. paths], TimeSpan.FromSeconds(30), token);
                foreach (var line in scan.StandardOutput.Split('\n'))
                {
                    var separator = line.IndexOf('\t');
                    if (separator > 0 && long.TryParse(line[..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
                        sizes[line[(separator + 1)..]] = bytes;
                }
            }
            var remaining = drive.Used;
            var categories = new List<StorageUsageCategory>();
            var accountedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (name, locations) in groups)
            {
                var included = locations.Distinct().Where(p => sizes.ContainsKey(p) && !accountedPaths.Any(parent => p == parent || p.StartsWith(parent.TrimEnd('/') + "/", StringComparison.Ordinal))).ToArray();
                var bytes = Math.Min(remaining, included.Sum(p => sizes[p]));
                foreach (var path in included) accountedPaths.Add(path);
                if (bytes <= 0) continue;
                remaining -= bytes;
                categories.Add(new(name, bytes, included));
            }
            if (remaining > 0) categories.Add(new("Other files", remaining, [drive.Path], accountedPaths.ToArray()));
            result.Add(drive with { Categories = categories });
        }
        return result;
    }

    private static Dictionary<string, string[]> CategoryPaths(IReadOnlyDictionary<string, string> userDirectories)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new Dictionary<string, string[]>
        {
            ["Installed apps"] = ["/opt", "/var/lib/flatpak", Path.Combine(home, ".local/share/flatpak"), Path.Combine(home, ".local/lib/daisyos")],
            ["System"] = ["/usr", "/etc", "/boot"],
            ["Documents"] = [userDirectories["Documents"]],
            ["Downloads"] = [userDirectories["Downloads"]],
            ["Pictures"] = [userDirectories["Pictures"]],
            ["Videos"] = [userDirectories["Videos"]],
            ["Music"] = [userDirectories["Music"]],
            ["Trash"] = [Path.Combine(home, ".local/share/Trash")]
        };
    }

    /// <summary>Allocated sizes of immediate children, including hidden files; NUL delimiters preserve real filenames.</summary>
    public static async Task<IReadOnlyList<StorageFileUsage>> ReadEntriesAsync(StorageUsageCategory category,
        CancellationToken token, ICommandRunner? runner = null)
    {
        runner ??= new SafeCommandRunner();
        var roots = (category.Paths ?? []).Where(Directory.Exists).Distinct(StringComparer.Ordinal).ToArray();
        if (roots.Length == 0) return [];
        var excluded = category.ExcludedPaths ?? [];
        static string Pattern(string path) => path.TrimEnd('/').Replace("\\", "\\\\").Replace("*", "\\*").Replace("?", "\\?").Replace("[", "\\[");
        var result = await runner.RunAsync("du", ["-a", "-x", "-B1", "--max-depth=1", "--null",
            .. excluded.Select(path => "--exclude=" + Pattern(path)), "--", .. roots], TimeSpan.FromSeconds(30), token);
        if (result.TimedOut) throw new IOException("Storage scan timed out.");
        var entries = new Dictionary<string, StorageFileUsage>(StringComparer.Ordinal);
        foreach (var record in result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab <= 0 || !long.TryParse(record[..tab], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) || bytes < 0) continue;
            var path = record[(tab + 1)..];
            if (roots.Any(root => path.TrimEnd('/') == root.TrimEnd('/'))) continue;
            if (!File.Exists(path) && !Directory.Exists(path)) continue; // The file may have disappeared during scanning.
            entries[path] = new(path, bytes, Directory.Exists(path));
        }
        if (!result.Succeeded && entries.Count == 0) throw new IOException("Storage files could not be read.");
        return entries.Values.OrderByDescending(entry => entry.Bytes).ThenBy(entry => entry.Path, StringComparer.CurrentCulture).ToArray();
    }

    private static StorageDriveOverview? Owner(string path, IReadOnlyList<StorageDriveOverview> drives) =>
        drives.Where(d => d.Path == "/" || path == d.Path.TrimEnd('/') || path.StartsWith(d.Path.TrimEnd('/') + "/", StringComparison.Ordinal))
            .OrderByDescending(d => d.Path.Length).FirstOrDefault();
}
