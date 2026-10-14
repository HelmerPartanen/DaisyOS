using System.Text.Json;
using DaisyOS.Core;
using DaisyOS.Shell.Services.Serialization;

namespace DaisyOS.Shell.Apps.Files.Picker;

internal sealed record DaisyFilePickerPreferences(
    string SortColumn = "Name",
    bool SortAscending = true,
    bool IsGridView = true);

internal static class DaisyFilePickerPreferencesStore
{
    private static readonly string PreferencesPath = GetPreferencesPath();

    public static DaisyFilePickerPreferences Load()
    {
        try
        {
            if (File.Exists(PreferencesPath))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(PreferencesPath),
                    DaisyShellJsonContext.Default.DaisyFilePickerPreferences)
                    ?? new DaisyFilePickerPreferences();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"[file-picker] Couldn’t load preferences: {ex.Message}");
        }

        return new DaisyFilePickerPreferences();
    }

    public static void Save(DaisyFilePickerPreferences preferences)
    {
        try
        {
            var json = JsonSerializer.Serialize(preferences, DaisyShellJsonContext.Default.DaisyFilePickerPreferences);
            AtomicTextFile.Write(PreferencesPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[file-picker] Couldn’t save preferences: {ex.Message}");
        }
    }

    private static string GetPreferencesPath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configHome, "DaisyOS", "file-picker-preferences.json");
    }
}
