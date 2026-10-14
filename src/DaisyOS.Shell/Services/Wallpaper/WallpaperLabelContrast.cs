using System;
using Avalonia.Media;

namespace DaisyOS.Shell.Services.Wallpaper;

/// <summary>
/// Chooses desktop-caption contrast from the wallpaper itself, independent of shell theme.
/// </summary>
public static class WallpaperLabelContrast
{
    public static readonly Color LightLabel = Color.Parse("#FFF5F5F5");
    public static readonly Color DarkLabel = Color.Parse("#FF1A1A1A");

    /// <summary>
    /// Brightness threshold (relative luminance in [0, 1]) at which labels turn dark.
    /// Increased from 0.60 to 0.75 so labels remain light with a drop shadow on medium and moderately bright wallpapers,
    /// turning dark only on distinctly bright/near-white backgrounds.
    /// </summary>
    public const double BrightnessThreshold = 0.75;

    public static Color ForWallpaper(Color wallpaperAverage, double threshold = BrightnessThreshold) =>
        RelativeLuminance(wallpaperAverage) >= threshold ? DarkLabel : LightLabel;

    public static bool IsDarkLabel(Color wallpaperAverage, double threshold = BrightnessThreshold) =>
        RelativeLuminance(wallpaperAverage) >= threshold;

    public static double RelativeLuminance(Color color) =>
        (0.2126 * Linear(color.R)) +
        (0.7152 * Linear(color.G)) +
        (0.0722 * Linear(color.B));

    private static double Linear(byte component)
    {
        var value = component / 255d;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}

