using System;
using System.IO;
using Avalonia.Media;

namespace DaisyOS.Shell.Services.Wallpaper;

/// <summary>
/// Encodes non-file wallpaper sources without overloading file-system paths.
/// The value is persisted by AppearanceSettings and travels through the same
/// wallpaper change notification as an image or video source.
/// </summary>
public static class WallpaperSource
{
    private const string SolidColorPrefix = "solid:";

    public static string FromSolidColor(Color color) =>
        $"{SolidColorPrefix}#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static bool TryGetSolidColor(string? source, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(source)
            || !source.StartsWith(SolidColorPrefix, StringComparison.OrdinalIgnoreCase)
            || !Color.TryParse(source[SolidColorPrefix.Length..], out var parsed))
        {
            return false;
        }

        // Wallpapers are intentionally opaque. Alpha would expose an empty
        // compositor surface below the shell background layer.
        color = Color.FromRgb(parsed.R, parsed.G, parsed.B);
        return true;
    }

    public static string ResolveWallpaperPath(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return string.Empty;
        if (Uri.TryCreate(uri, UriKind.Absolute, out var fileUri) && fileUri.IsFile)
            uri = fileUri.LocalPath;
        return File.Exists(uri) ? Path.GetFullPath(uri) : uri;
    }
}
