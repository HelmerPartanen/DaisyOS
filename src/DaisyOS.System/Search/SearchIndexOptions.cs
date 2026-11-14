using DaisyOS.Core.Storage;

namespace DaisyOS.System.Search;

public sealed record SearchIndexOptions(string DatabasePath, IReadOnlyList<string> Roots,
    IReadOnlyList<string> Exclusions, TimeSpan ReconcileInterval)
{
    public static SearchIndexOptions LoadDefaults()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        static string Xdg(string variable, string fallback) => Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value && Path.IsPathRooted(value) ? value : fallback;
        var data = Xdg("XDG_DATA_HOME", Path.Combine(home, ".local", "share"));
        var config = Path.Combine(Xdg("XDG_CONFIG_HOME", Path.Combine(home, ".config")), "daisyos", "search");
        static string[] ReadPaths(string file) => File.Exists(file) ? File.ReadLines(file)
            .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#') && Path.IsPathFullyQualified(l))
            .Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray() : [];
        var roots = ReadPaths(Path.Combine(config, "roots.txt"));
        if (!File.Exists(Path.Combine(config, "roots.txt"))) roots = UserDirectoryPaths.Load()
            .Where(p => p.Key != "Home" && p.Value != home).Select(p => p.Value).Distinct(StringComparer.Ordinal).ToArray();
        return new(Path.Combine(data, "daisyos", "search", "search.db"), roots,
            ReadPaths(Path.Combine(config, "exclusions.txt")), TimeSpan.FromMinutes(30));
    }
}

public sealed record IndexedFile(string Path, string Name, string Parent, string Extension,
    bool IsDirectory, long Size, long ModifiedUtcTicks);
public sealed record FileIndexDiagnostic(string Status, long FileCount, long DatabaseBytes,
    DateTimeOffset? LastReconciliation, int WatcherCount, long ErrorCount, bool IsPaused, long ScannedEntries = 0);
