using DaisyOS.Core.Models;

namespace DaisyOS.Shell.Services.Appearance;

/// <summary>Persisted preferences that affect the rendered shell appearance.</summary>
public sealed class AppearanceSettings
{
    public string ProfilePictureUri { get; set; } = string.Empty;

    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;

    public string WallpaperUri { get; set; } = ShellSettings.DefaultWallpaperUri;

    /// <summary>True when the desktop uses SolidWallpaperColor instead of WallpaperUri.</summary>
    public bool UseSolidWallpaper { get; set; }

    /// <summary>Persisted as an opaque #RRGGBB colour so it is stable across themes.</summary>
    public string SolidWallpaperColor { get; set; } = "#2E3440";

    public AccentColor AccentColor { get; set; } = AccentColor.Default;

    public bool HighContrast { get; set; }

    public bool ReduceMotion { get; set; }

    public string? CursorThemeId { get; set; } = "DaisyOS-Dark";

    public int CursorSize { get; set; } = 32;

    public string SystemFontFamily { get; set; } = "Inter";

    public string LanguageTag { get; set; } = SystemLanguages.DefaultTag;

    public int TextScalePercent { get; set; } = 100;

    public bool TaskbarAutoHide { get; set; }

    public bool SystemBarAutoHide { get; set; }
}
