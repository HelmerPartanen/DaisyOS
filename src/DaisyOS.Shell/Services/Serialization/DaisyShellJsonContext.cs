using System.Collections.Generic;
using System.Text.Json.Serialization;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Apps.Calendar;
using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.Apps.Files.Picker;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Shell.Services.Desktop;

namespace DaisyOS.Shell.Services.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppearanceSettings))]
[JsonSerializable(typeof(DaisyOS.Shell.Services.Wallpaper.VideoWallpaper.VideoOptimizationStatus))]
[JsonSerializable(typeof(DesktopLayoutConfig))]
[JsonSerializable(typeof(ShellSessionState.PersistedShellState))]
[JsonSerializable(typeof(FilesPreferences))]
[JsonSerializable(typeof(DaisyFilePickerPreferences))]
[JsonSerializable(typeof(CalendarEventRecord))]
[JsonSerializable(typeof(List<CalendarEventRecord>))]
[JsonSerializable(typeof(ControllerKeybindingsConfig))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(DaisyOS.Shell.Services.Compositor.TaskbarPreviewRequest))]
internal sealed partial class DaisyShellJsonContext : JsonSerializerContext
{
}
