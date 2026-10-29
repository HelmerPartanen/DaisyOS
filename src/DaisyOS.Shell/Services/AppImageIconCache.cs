using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Avalonia.Media;
using DaisyOS.Shell.ViewModels;
using DaisyOS.System.AppImage;

namespace DaisyOS.Shell.Services;

/// <summary>
/// Extracts and caches application icons from AppImage files so that file managers
/// and desktops can display the actual application artwork, using the system
/// AppImage package icon only as a fallback when no embedded icon is present.
/// </summary>
public static class AppImageIconCache
{
    private static readonly ConcurrentDictionary<string, string?> CachedPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache", "daisyos", "appimage-icons");

    public static string? GetOrExtractIconPath(string appImagePath)
    {
        if (string.IsNullOrWhiteSpace(appImagePath)) return null;

        var fullPath = Path.GetFullPath(appImagePath);
        if (!File.Exists(fullPath)) return null;
        if (!string.Equals(Path.GetExtension(fullPath), ".AppImage", StringComparison.OrdinalIgnoreCase))
            return null;

        var fileInfo = new FileInfo(fullPath);
        var cacheKey = ComputeKey(fullPath, fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks);

        return CachedPaths.GetOrAdd(cacheKey, _ => ExtractOrLookupPath(fullPath, cacheKey));
    }

    public static IImage? GetOrExtractIcon(string appImagePath)
    {
        var iconPath = GetOrExtractIconPath(appImagePath);
        return !string.IsNullOrWhiteSpace(iconPath) ? DesktopItemLoader.LoadBitmapSafe(iconPath) : null;
    }

    private static string? ExtractOrLookupPath(string fullPath, string cacheKey)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);

            var pngPath = Path.Combine(CacheDirectory, cacheKey + ".png");
            if (File.Exists(pngPath)) return pngPath;

            var svgPath = Path.Combine(CacheDirectory, cacheKey + ".svg");
            if (File.Exists(svgPath)) return svgPath;

            var nonePath = Path.Combine(CacheDirectory, cacheKey + ".none");
            if (File.Exists(nonePath)) return null;

            // Extract embedded icon metadata from the AppImage
            var service = new AppImageService();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var inspection = service.InspectAsync(fullPath, cts.Token).GetAwaiter().GetResult();

            if (inspection.IconData is { Length: > 0 } data && !string.IsNullOrWhiteSpace(inspection.IconExtension))
            {
                var targetPath = Path.Combine(CacheDirectory, cacheKey + inspection.IconExtension.ToLowerInvariant());
                var tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(tempPath, data);
                File.Move(tempPath, targetPath, overwrite: true);
                return targetPath;
            }

            // Mark as having no embedded icon so subsequent lookups don't re-probe
            File.WriteAllText(nonePath, string.Empty);
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeKey(string fullPath, long length, long lastWriteTicks)
    {
        var raw = $"{fullPath}:{length}:{lastWriteTicks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
