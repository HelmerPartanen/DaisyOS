using System;
using System.Linq;
using System.Text.Json;
using Directory = global::System.IO.Directory;
using File = global::System.IO.File;
using Path = global::System.IO.Path;

namespace DaisyOS.Shell.Apps.Files.Services;

public sealed record FilesPreferences(
    bool IsGridView = false,
    string SortColumn = "Name",
    bool SortAscending = true,
    bool ShowHiddenFiles = false,
    bool ShowDateColumn = true,
    bool ShowTypeColumn = true,
    bool ShowSizeColumn = true,
    bool IsPreviewPaneVisible = false,
    string[]? StarredPaths = null
);

public static class FilesPreferencesService
{
    private static readonly string ConfigDirectory = GetConfigDirectory();
    private static string GetConfigDirectory()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome)) configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configHome, "DaisyOS");
    }
    private static readonly string PreferencesPath = Path.Combine(ConfigDirectory, "files-preferences.json");
    private static readonly string LegacySidebarPath = Path.Combine(ConfigDirectory, "files-sidebar.json");

    private static readonly object LockObject = new();
    private static FilesPreferences? _cachedPreferences;

    public static FilesPreferences Load()
    {
        lock (LockObject)
        {
            if (_cachedPreferences is not null)
            {
                return _cachedPreferences;
            }

            try
            {
                if (File.Exists(PreferencesPath))
                {
                    var json = File.ReadAllText(PreferencesPath);
                    var prefs = JsonSerializer.Deserialize(json, DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.FilesPreferences);
                    if (prefs is not null)
                    {
                        _cachedPreferences = prefs;
                        return prefs;
                    }
                }

                _cachedPreferences = new FilesPreferences(StarredPaths: []);
                return _cachedPreferences;
            }
            catch
            {
                _cachedPreferences = new FilesPreferences();
                return _cachedPreferences;
            }
        }
    }

    public static void Save(FilesPreferences preferences)
    {
        lock (LockObject)
        {
            _cachedPreferences = preferences;
            try
            {
                if (!Directory.Exists(ConfigDirectory))
                {
                    Directory.CreateDirectory(ConfigDirectory);
                }

                var json = JsonSerializer.Serialize(preferences, DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.FilesPreferences);
                global::DaisyOS.Core.AtomicTextFile.Write(PreferencesPath, json);
            }
            catch
            {
                // Never block UI if file write fails (e.g. read-only disk)
            }
        }
    }

    public sealed record FilesSidebarStateLegacy(string[] StarredPaths);
}

