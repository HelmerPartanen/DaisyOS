using Avalonia.Media;
using Avalonia.Threading;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Core.Models;

namespace DaisyOS.Shell;

public partial class App
{
    private bool _useDesktopSettings;
    private readonly SemaphoreSlim _desktopSettingsQueue = new(1, 1);

    /// <summary>Settings reads shared state, but the desktop remains its sole writer.</summary>
    public void UseDesktopSettings()
    {
        _useDesktopSettings = true;
        SessionState.RemoteWrite = (operation, values) => _ = ApplyDesktopSettingAsync(operation, values);
    }

    private async Task<bool> ApplyDesktopSettingAsync(string operation, params string[] values)
    {
        await _desktopSettingsQueue.WaitAsync();
        try
        {
            var response = await ApplicationPipe.RequestAsync(ApplicationPipe.Name("desktop-settings"), [operation, .. values]);
            SessionState.Reload();
            ReloadApplicationAppearance(force: true);
            if (response == "ok")
            {
                if (operation == "preference" && values.Length == 2) PreferenceChanged?.Invoke(this, (values[0], values[1]));
                return true;
            }
            Feedback.Show(response);
        }
        catch (Exception ex) { ReloadApplicationAppearance(force: true); Feedback.Show(Loc.Format("Couldn’t update desktop settings: {0}", ex.Message)); }
        finally { _desktopSettingsQueue.Release(); }
        return false;
    }

    private async Task<string> ApplyApplicationSettingAsync(string[] request)
    {
        // Only named settings operations are accepted; no arbitrary commands or reflection.
        if (request.Length == 0) return "Missing settings operation.";
        switch (request[0])
        {
            case "preference" when request.Length == 3: SetPreference(request[1], request[2]); break;
            case "dnd" when request.Length == 2: SetDoNotDisturb(bool.Parse(request[1])); break;
            case "show-date" when request.Length == 2: SessionState.ShowDateInSystemBar = bool.Parse(request[1]); break;
            case "gaming" when request.Length == 2: SessionState.GamingMode = bool.Parse(request[1]); break;
            case "volume" when request.Length == 2:
                SessionState.LastKnownVolume = request[1].Length == 0 ? null : double.Parse(request[1], global::System.Globalization.CultureInfo.InvariantCulture); break;
            case "theme" when request.Length == 2 && Enum.TryParse<ThemeMode>(request[1], out var mode) && Enum.IsDefined(mode): await SetThemeModeAsync(mode); break;
            case "accent" when request.Length == 2 && Enum.TryParse<AccentColor>(request[1], out var accent) && Enum.IsDefined(accent): await SetAccentColorAsync(accent); break;
            case "contrast" when request.Length == 2: await SetHighContrastAsync(bool.Parse(request[1])); break;
            case "motion" when request.Length == 2: await SetReduceMotionAsync(bool.Parse(request[1])); break;
            case "taskbar-auto-hide" when request.Length == 2: await SetTaskbarAutoHideAsync(bool.Parse(request[1])); break;
            case "media-widget-auto-hide" when request.Length == 2: await SetMediaWidgetAutoHideAsync(bool.Parse(request[1])); break;
            case "systembar-auto-hide" when request.Length == 2: await SetSystemBarAutoHideAsync(bool.Parse(request[1])); break;
            case "cursor" when request.Length == 3:
                if (!await SetCursorAsync(request[1], int.Parse(request[2]))) return "The cursor could not be changed.";
                break;
            case "language" when request.Length == 2:
                if (!await SetLanguageAsync(request[1])) return "The language could not be changed.";
                break;
            case "typography" when request.Length == 3:
                if (!await SetTypographyAsync(request[1], int.Parse(request[2]))) return "Typography could not be changed.";
                break;
            case "wallpaper" when request.Length == 2:
                await SetWallpaperAsync(request[1]);
                if (UsesSolidWallpaper || CurrentWallpaperUri != request[1]) return "The wallpaper could not be changed.";
                break;
            case "solid-wallpaper" when request.Length == 2 && Color.TryParse(request[1], out var color): await SetSolidWallpaperAsync(color); break;
            case "image-wallpaper" when request.Length == 1: await UseImageWallpaperAsync(); break;
            case "profile-picture" when request.Length == 2:
                await SetProfilePictureAsync(string.IsNullOrWhiteSpace(request[1]) ? null : request[1]);
                break;
            case "pin" when request.Length == 2: PinApplicationToTaskbar(request[1]); break;
            default: return "Unknown or invalid settings operation.";
        }
        return "ok";
    }

    private void StartApplicationSettingsService(CancellationToken token) =>
        _ = ApplicationPipe.ServeAsync(ApplicationPipe.Name("desktop-settings"),
            async request => await Dispatcher.UIThread.InvokeAsync(() => ApplyApplicationSettingAsync(request)), token);
}
