using System.Text.Json;

namespace DaisyOS.Shell.Services.Appearance;

/// <summary>Small, shell-owned store for appearance preferences.</summary>
public sealed class AppearanceSettingsStore
{
    private readonly string _path;

    public AppearanceSettingsStore(string? path = null)
    {
        _path = path ?? GetDefaultPath();
    }

    public string PreferencesPath => _path;

    public AppearanceSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppearanceSettings();
            }

            var settings = JsonSerializer.Deserialize(File.ReadAllText(_path), DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.AppearanceSettings)
                           ?? new AppearanceSettings();

            settings.Taskbar = (settings.Taskbar ?? new()).Normalize();

            // Migrate independent bar preferences to a single consistent setting.
            var autoHide = settings.BottomComponentsAutoHide
                ?? (settings.TaskbarAutoHide || settings.SystemBarAutoHide || settings.MediaWidgetAutoHide);
            settings.BottomComponentsAutoHide = autoHide;
            settings.TaskbarAutoHide = settings.SystemBarAutoHide = settings.MediaWidgetAutoHide = autoHide;

            if ((string.IsNullOrWhiteSpace(settings.ProfilePictureUri) || !File.Exists(settings.ProfilePictureUri)) &&
                ProfilePictureStore.FindSingleRecoverablePicture(_path) is { } recoveredPicture)
            {
                settings.ProfilePictureUri = recoveredPicture;
                try
                {
                    var json = JsonSerializer.Serialize(settings,
                        DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.AppearanceSettings);
                    DaisyOS.Core.AtomicTextFile.Write(_path, json);
                    Console.WriteLine("Recovered the saved DaisyOS profile picture from its local profile store.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.WriteLine($"Couldn’t restore the saved profile picture preference: {ex.Message}");
                }
            }

            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Failed to load appearance preferences: {ex.Message}");
            return new AppearanceSettings();
        }
    }

    public async Task SaveAsync(AppearanceSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var json = JsonSerializer.Serialize(settings, DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.AppearanceSettings);

        await global::DaisyOS.Core.AtomicTextFile.WriteAsync(_path, json, cancellationToken);
    }

    private static string GetDefaultPath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configHome = Path.Combine(home, ".config");
        }

        return Path.Combine(configHome, "DaisyOS", "appearance.json");
    }
}
