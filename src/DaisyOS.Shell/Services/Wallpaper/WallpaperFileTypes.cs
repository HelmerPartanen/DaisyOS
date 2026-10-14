namespace DaisyOS.Shell.Services.Wallpaper;

/// <summary>Supported user-selectable wallpaper media formats.</summary>
public static class WallpaperFileTypes
{
    public static readonly string[] ImagePatterns =
    [
        "*.jpg", "*.jpeg", "*.png", "*.webp", "*.bmp", "*.gif"
    ];

    public static readonly string[] VideoPatterns =
    [
        "*.mp4", "*.webm", "*.mkv", "*.mov", "*.m4v", "*.avi"
    ];

    public static readonly string[] WallpaperPatterns = [.. ImagePatterns, .. VideoPatterns];

    public static bool IsVideo(string? path) =>
        Path.GetExtension(path)?.ToLowerInvariant() is ".mp4" or ".webm" or ".mkv" or ".mov" or ".m4v" or ".avi";

    public static bool IsImage(string? path) =>
        Path.GetExtension(path)?.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif";
}
