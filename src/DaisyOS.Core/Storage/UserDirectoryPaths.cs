using System.Text.RegularExpressions;

namespace DaisyOS.Core.Storage;

/// <summary>Semantic user folders resolved from XDG configuration without executing shell text.</summary>
public static class UserDirectoryPaths
{
    public static IReadOnlyDictionary<string, string> Load(string? homePath = null, string? configPath = null)
    {
        var home = Path.GetFullPath(homePath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var names = new[] { "Desktop", "Documents", "Downloads", "Pictures", "Videos", "Music" };
        var keys = new[] { "DESKTOP", "DOCUMENTS", "DOWNLOAD", "PICTURES", "VIDEOS", "MUSIC" };
        var paths = names.ToDictionary(name => name, name => Path.Combine(home, name), StringComparer.Ordinal);
        paths["Home"] = home;
        var configHome = homePath is null ? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") : null;
        configHome = !string.IsNullOrWhiteSpace(configHome) && Path.IsPathRooted(configHome) ? configHome : Path.Combine(home, ".config");
        configPath ??= Path.Combine(configHome, "user-dirs.dirs");
        try
        {
            foreach (var line in File.ReadLines(configPath))
            {
                var match = Regex.Match(line, "^\\s*XDG_(?<key>[A-Z]+)_DIR\\s*=\\s*\"(?<path>(?:\\\\.|[^\"\\\\])*)\"\\s*(?:#.*)?$");
                if (!match.Success) continue;
                var index = Array.IndexOf(keys, match.Groups["key"].Value);
                if (index < 0) continue;
                var value = Regex.Replace(match.Groups["path"].Value, @"\\([\\""$`])", "$1");
                if (value == "$HOME" || value.StartsWith("$HOME/", StringComparison.Ordinal)) value = home + value[5..];
                else if (value == "${HOME}" || value.StartsWith("${HOME}/", StringComparison.Ordinal)) value = home + value[7..];
                if (!Path.IsPathRooted(value) || value.Contains('$') || value.Contains('`')) continue;
                paths[names[index]] = Path.GetFullPath(value);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return paths;
    }
}
