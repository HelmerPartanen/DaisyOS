using System;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;

using System.Collections.Concurrent;

namespace DaisyOS.Shell.ViewModels;

public static class DesktopItemLoader
{
    private static readonly ConcurrentDictionary<string, Avalonia.Media.IImage?> _imageCache = new();
    private static readonly ConcurrentDictionary<string, string> _iconPathCache = new();
    private static readonly ConcurrentDictionary<string, string?> _desktopEntryIconNameCache = new();

    public static bool IsCurrentThemeLight()
    {
        // Icon discovery can run before an application and its dispatcher exist.
        // There is no theme to read in that case, and dispatching would wait forever.
        if (Avalonia.Application.Current is null) return false;

        try
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                return Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;
            }

            return Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light);
        }
        catch
        {
            return false;
        }
    }

    public static string GetAppIconPath(string iconName, bool isLight)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return string.Empty;
        var suffix = isLight ? "Light" : "Dark";
        var clean = iconName.Trim();
        if (clean.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[..^8];
        }
        if (clean.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean["org.daisyos.".Length..];
        }
        clean = Path.GetFileNameWithoutExtension(clean).ToLowerInvariant();
        if (clean.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean["org.daisyos.".Length..];
        }

        return clean switch
        {
            "calculator" or "calculatoricon" or "calculator_dark" or "calculator_light" => "avares://DaisyOS.Shell/Assets/AppIcons/Calculator.png",
            "calendar" or "calendaricon" or "calendar_dark" or "calendar_light" => "avares://DaisyOS.Shell/Assets/AppIcons/Calendar.png",
            "files" or "filesicon" or "files_dark" or "files_light" or "filemanager" or "file-manager" or "file_manager" => "avares://DaisyOS.Shell/Assets/AppIcons/Files.png",
            "launcher" or "launchericon" or "launcher_dark" or "launcher_light" or "start" => "avares://DaisyOS.Shell/Assets/AppIcons/Launcher.png",
            "notes" or "notesicon" or "notes_dark" or "notes_light" => "avares://DaisyOS.Shell/Assets/AppIcons/Notes.png",
            "settings" or "settingsicon" or "settings_dark" or "settings_light" => "avares://DaisyOS.Shell/Assets/AppIcons/Settings.png",
            "uikit" or "ui-kit" or "uikit_dark" or "uikit_light" => "avares://DaisyOS.Shell/Assets/AppIcons/UiKit.png",
            "diskmanager" or "disk manager" or "disk_manager" or "disk-manager" or "disk_manager_dark" or "disk_manager_light" => $"avares://DaisyOS.Shell/Assets/AppIcons/Disk_Manager_{suffix}.png",
            "appinstaller" or "app installer" or "app_installer" or "app-installer" => "avares://DaisyOS.Shell/Assets/AppIcons/AppInstaller.png",
            "taskmanager" or "task manager" or "systemmonitor" or "system-monitor" or "system monitor" or "system_monitor" or "system_monitor_dark" or "system_monitor_light" => $"avares://DaisyOS.Shell/Assets/AppIcons/System_Monitor_{suffix}.png",
            "appstore" or "app-store" or "app_store" or "store" or "store_dark" or "store_light" => "avares://DaisyOS.Shell/Assets/AppIcons/Store.png",
            "trash" or "trash_dark" or "trash_light" => $"avares://DaisyOS.Shell/Assets/AppIcons/Trash_{suffix}.png",
            "trash_item" or "trashitem" or "trash_item_dark" or "trash_item_light" or "desktop.trash" => $"avares://DaisyOS.Shell/Assets/AppIcons/Trash_Item_{suffix}.png",
            _ => string.Empty
        };
    }

    internal static void InvalidateIconCaches()
    {
        _iconPathCache.Clear();
        _imageCache.Clear();
    }

    /// <summary>Forces eager construction of the icon theme index on the current thread.</summary>
    public static void WarmUp() => _ = _iconIndex.Value;

    internal static string ResolveFileIconPath(string path) => ResolveFileIconPath(path, IsCurrentThemeLight());

    internal static string ResolveFileIconPath(string path, bool isLight)
    {
        var directory = Directory.Exists(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!directory && string.Equals(extension, ".appimage", StringComparison.OrdinalIgnoreCase))
        {
            var appImageIcon = DaisyOS.Shell.Services.AppImageIconCache.GetOrExtractIconPath(path);
            if (!string.IsNullOrWhiteSpace(appImageIcon))
            {
                return appImageIcon;
            }
        }
        var mimeIcon = directory ? "folder" :
            DaisyOS.Shell.Apps.Files.Services.FileVisualResolver.GetMimeIconName(extension);
        var resolved = ResolveIconPath(mimeIcon, isLight);
        if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
        var genericIcon = directory ? "inode-directory" :
            mimeIcon.StartsWith("text-", StringComparison.Ordinal) ? "text-x-generic" :
            mimeIcon.StartsWith("image-", StringComparison.Ordinal) ? "image-x-generic" :
            mimeIcon.StartsWith("audio-", StringComparison.Ordinal) ? "audio-x-generic" :
            mimeIcon.StartsWith("video-", StringComparison.Ordinal) ? "video-x-generic" : "application-x-generic";
        resolved = ResolveIconPath(genericIcon, isLight);
        return !string.IsNullOrWhiteSpace(resolved) ? resolved :
            $"avares://DaisyOS.Shell/Assets/FileIcons/{(directory ? "Folder" : "Document")}.svg";
    }

    public static string ResolveIconPath(string iconName) => ResolveIconPath(iconName, IsCurrentThemeLight());

    internal static string ResolveIconPath(string iconName, bool isLight)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return string.Empty;
        var builtIn = GetAppIconPath(iconName, isLight);
        if (!string.IsNullOrWhiteSpace(builtIn)) return builtIn;

        return _iconPathCache.GetOrAdd(iconName, ResolveIconPathUncached);
    }

    private static readonly Lazy<ConcurrentDictionary<string, string>> _iconIndex = new(BuildIconIndex);

    private static ConcurrentDictionary<string, string> BuildIconIndex()
    {
        var index = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var existingDirs = GetIconSearchPaths().Where(Directory.Exists).Distinct(StringComparer.Ordinal);

        foreach (var dir in existingDirs)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    var ext = Path.GetExtension(file);
                    if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".svg", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".xpm", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        var score = GetIconQualityScore(file);
                        if (!scores.TryGetValue(name, out var currentScore) || score > currentScore)
                        {
                            scores[name] = score;
                            index[name] = file;
                        }
                    }
                }
            }
            catch { }
        }

        return index;
    }

    internal static int GetIconQualityScore(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        var score = 0;

        if (ext.Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            score += 10000;
        }
        else if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }
        else if (ext.Equals(".xpm", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        var normalized = filePath.Replace('\\', '/');
        // Hicolor is where applications install their own artwork. Preserve
        // that identity before comparing resolution or format with theme
        // replacements (a themed SVG is not necessarily the application's icon).
        if (normalized.Contains("/icons/hicolor/", StringComparison.OrdinalIgnoreCase))
        {
            score += 100000;
        }
        if (normalized.Contains("/scalable/", StringComparison.OrdinalIgnoreCase)) score += 2000;
        else if (normalized.Contains("/512x512/", StringComparison.OrdinalIgnoreCase)) score += 512;
        else if (normalized.Contains("/256x256/", StringComparison.OrdinalIgnoreCase)) score += 256;
        else if (normalized.Contains("/128x128/", StringComparison.OrdinalIgnoreCase)) score += 128;
        else if (normalized.Contains("/96x96/", StringComparison.OrdinalIgnoreCase)) score += 96;
        else if (normalized.Contains("/64x64/", StringComparison.OrdinalIgnoreCase)) score += 64;
        else if (normalized.Contains("/48x48/", StringComparison.OrdinalIgnoreCase)) score += 48;
        else if (normalized.Contains("/32x32/", StringComparison.OrdinalIgnoreCase)) score += 32;
        else if (normalized.Contains("/24x24/", StringComparison.OrdinalIgnoreCase)) score += 24;
        else if (normalized.Contains("/16x16/", StringComparison.OrdinalIgnoreCase)) score += 16;

        if (normalized.Contains("/pixmaps/", StringComparison.OrdinalIgnoreCase))
        {
            score -= 200;
        }

        return score;
    }

    private static string ResolveIconPathUncached(string iconName)
    {
        if (File.Exists(iconName)) return iconName;

        var cleanName = Path.GetFileNameWithoutExtension(iconName);
        if (_iconIndex.Value.TryGetValue(cleanName, out var indexedPath))
        {
            return indexedPath;
        }
        if (_iconIndex.Value.TryGetValue(iconName, out var exactPath))
        {
            return exactPath;
        }

        return string.Empty;
    }

    /// <summary>
    /// Resolves an icon for a compositor window. Wayland app IDs, X11
    /// StartupWMClass values, Flatpak IDs, and icon names frequently differ;
    /// desktop entries are the common metadata bridge between those identities.
    /// </summary>
    /// <summary>
    /// Resolves the best on-disk icon for a compositor window without decoding it.
    /// Keeping the path lets callers retain vector icons as vectors at their final
    /// presentation size instead of first rasterizing them at an arbitrary size.
    /// </summary>
    public static string ResolveApplicationIconPath(IEnumerable<string?> identities)
    {
        var rawList = new List<string>();
        foreach (var identity in identities)
        {
            if (string.IsNullOrWhiteSpace(identity)) continue;
            var trimmed = identity.Trim();
            rawList.Add(trimmed);
            var lower = trimmed.ToLowerInvariant();
            if (lower.Contains("steam"))
            {
                rawList.Add("steam");
                rawList.Add("com.valvesoftware.Steam");
            }
        }

        var rawCandidates = rawList.Distinct(StringComparer.Ordinal).ToArray();
        var candidates = rawCandidates
            .Select(NormalizeIdentity)
            .Where(candidate => candidate.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            return string.Empty;
        }

        foreach (var candidate in rawCandidates)
        {
            var builtIn = GetAppIconPath(candidate, IsCurrentThemeLight());
            if (!string.IsNullOrWhiteSpace(builtIn)) return builtIn;
        }

        var cacheKey = string.Join('|', candidates.OrderBy(candidate => candidate, StringComparer.Ordinal));
        var iconName = _desktopEntryIconNameCache.GetOrAdd(
            cacheKey,
            _ => FindDesktopEntryIconName(candidates, EnumerateDesktopEntryFiles()));
        if (!string.IsNullOrWhiteSpace(iconName))
        {
            // Desktop-entry metadata identifies the application, while KWin's
            // icon property can be a low-resolution compatibility alias. Prefer
            // the entry's scalable/theme icon whenever it is available.
            var desktopEntryIconPath = ResolveIconPath(iconName);
            if (!string.IsNullOrWhiteSpace(desktopEntryIconPath))
            {
                return desktopEntryIconPath;
            }
        }

        foreach (var candidate in rawCandidates)
        {
            var directIconPath = ResolveIconPath(candidate);
            if (!string.IsNullOrWhiteSpace(directIconPath))
            {
                return directIconPath;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Loads an application icon for image-only callers. Taskbar callers should
    /// prefer <see cref="ResolveApplicationIconPath"/> so SVGs stay vector-backed.
    /// </summary>
    public static Avalonia.Media.IImage? LoadApplicationIcon(IEnumerable<string?> identities)
    {
        return LoadBitmapSafe(ResolveApplicationIconPath(identities));
    }

    /// <summary>
    /// Finds a desktop-entry icon using the identifiers window managers expose.
    /// Kept public for deterministic resolver tests with fixture desktop files.
    /// </summary>
    public static string? FindDesktopEntryIconName(
        IEnumerable<string?> identities,
        IEnumerable<string> desktopEntryFiles)
    {
        var candidates = identities
            .Select(NormalizeIdentity)
            .Where(candidate => candidate.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (candidates.Count == 0)
        {
            return null;
        }

        string? bestIconName = null;
        var bestScore = 0;
        foreach (var desktopEntryFile in desktopEntryFiles)
        {
            var entry = ReadDesktopEntry(desktopEntryFile);
            if (entry.Count == 0 || !entry.TryGetValue("Icon", out var iconName) || string.IsNullOrWhiteSpace(iconName))
            {
                continue;
            }

            var score = ScoreDesktopEntry(candidates, desktopEntryFile, entry);
            if (score > bestScore)
            {
                bestScore = score;
                bestIconName = iconName;
            }
        }

        return bestIconName;
    }

    /// <summary>
    /// Finds a desktop-entry file path matching the given identities.
    /// </summary>
    public static string? FindDesktopEntryFilePath(IEnumerable<string?> identities) =>
        FindDesktopEntryFilePath(identities, EnumerateDesktopEntryFiles());

    internal static string? FindDesktopEntryFilePath(IEnumerable<string?> identities, IEnumerable<string> desktopEntryFiles)
    {
        var candidates = identities
            .Where(identity => !string.IsNullOrWhiteSpace(identity))
            .Select(NormalizeIdentity)
            .Where(candidate => candidate.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (candidates.Count == 0)
        {
            return null;
        }

        string? bestPath = null;
        var bestScore = 0;
        foreach (var desktopEntryFile in desktopEntryFiles)
        {
            var entry = ReadDesktopEntry(desktopEntryFile);
            if (entry.Count == 0)
            {
                continue;
            }

            var score = ScoreDesktopEntry(candidates, desktopEntryFile, entry);
            if (score > bestScore)
            {
                bestScore = score;
                bestPath = desktopEntryFile;
            }
        }

        return bestPath;
    }

    private static int ScoreDesktopEntry(
        HashSet<string> candidates,
        string desktopEntryFile,
        IReadOnlyDictionary<string, string> entry)
    {
        var fileId = NormalizeIdentity(Path.GetFileNameWithoutExtension(desktopEntryFile));
        if (candidates.Contains(fileId)) return 5;

        if (entry.TryGetValue("X-Flatpak", out var flatpakId)
            && candidates.Contains(NormalizeIdentity(flatpakId))) return 5;

        if (entry.TryGetValue("StartupWMClass", out var startupWmClass)
            && candidates.Contains(NormalizeIdentity(startupWmClass))) return 4;

        if (entry.TryGetValue("Icon", out var icon)
            && candidates.Contains(NormalizeIdentity(icon))) return 3;

        if (entry.TryGetValue("Exec", out var exec))
        {
            var executable = exec.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(executable)
                && candidates.Contains(NormalizeIdentity(Path.GetFileName(executable)))) return 2;

            var flatpakCommand = exec.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(argument => argument.StartsWith("--command=", StringComparison.Ordinal));
            if (flatpakCommand is not null
                && candidates.Contains(NormalizeIdentity(flatpakCommand["--command=".Length..]))) return 2;
        }

        if (entry.TryGetValue("Name", out var name) && candidates.Contains(NormalizeIdentity(name))) return 1;

        // Older DaisyOS AppImage installs omitted StartupWMClass. Recover their
        // window identity from the owned desktop ID, ignoring word separators.
        if (entry.ContainsKey("X-DaisyOS-AppImage"))
        {
            static string Compact(string value) => new(value.Where(char.IsLetterOrDigit).ToArray());
            var compactId = Compact(fileId);
            if (compactId.Length > 0 && candidates.Any(candidate => Compact(candidate) == compactId)) return 1;
        }
        return 0;
    }

    private static Dictionary<string, string> ReadDesktopEntry(string path)
    {
        var entry = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var inDesktopEntry = false;
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (line.StartsWith('['))
                {
                    inDesktopEntry = string.Equals(line, "[Desktop Entry]", StringComparison.Ordinal);
                    continue;
                }

                if (!inDesktopEntry) continue;
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                entry[line[..separator]] = line[(separator + 1)..].Trim();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An icon lookup must never make the taskbar fail to render.
        }

        return entry;
    }

    private static IEnumerable<string> EnumerateDesktopEntryFiles()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string>
        {
            Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(home, ".local", "share"), "applications"),
            Path.Combine(home, ".local", "share", "applications"),
            Path.Combine(home, ".local", "share", "flatpak", "exports", "share", "applications"),
            "/var/lib/flatpak/exports/share/applications",
            "/var/lib/snapd/desktop/applications",
            "/usr/local/share/applications",
            "/usr/share/applications"
        };

        var configuredRoots = Environment.GetEnvironmentVariable("XDG_DATA_DIRS")
            ?.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        foreach (var dir in configuredRoots)
        {
            roots.Add(Path.Combine(dir, "applications"));
        }

        return roots.Distinct(StringComparer.Ordinal)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.desktop", SearchOption.TopDirectoryOnly));
    }

    private static IEnumerable<string> GetIconSearchPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataRoots = new List<string>
        {
            Path.Combine(home, ".local", "share"),
            Path.Combine(home, ".local", "share", "flatpak", "exports", "share"),
            "/var/lib/flatpak/exports/share",
            "/var/lib/snapd/desktop",
            "/usr/local/share",
            "/usr/share"
        };
        var configuredRoots = Environment.GetEnvironmentVariable("XDG_DATA_DIRS")
            ?.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        dataRoots.AddRange(configuredRoots);

        var themes = new[] { "hicolor", "Qogir", "Qogir-Dark", "Qogir-Light", "breeze", "breeze-dark", "Adwaita", "Papirus" };
        var sizes = new[] { "scalable", "512x512", "256x256", "128x128", "96x96", "64x64", "48x48", "32x32", "24x24", "16x16", "64", "48", "32" };

        foreach (var root in dataRoots.Distinct(StringComparer.Ordinal))
        {
            foreach (var theme in themes)
            {
                foreach (var size in sizes)
                {
                    yield return Path.Combine(root, "icons", theme, size, "apps");
                    yield return Path.Combine(root, "icons", theme, "apps", size);
                    foreach (var context in new[] { "mimetypes", "places" })
                    {
                        yield return Path.Combine(root, "icons", theme, size, context);
                        yield return Path.Combine(root, "icons", theme, context, size);
                    }
                }
            }
        }

        yield return "/usr/share/pixmaps";
        foreach (var root in dataRoots.Distinct(StringComparer.Ordinal))
        {
            yield return Path.Combine(root, "pixmaps");
        }
    }

    private static string NormalizeIdentity(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^".desktop".Length];
        }

        normalized = normalized.ToLowerInvariant();
        if (normalized.Contains("steamwebhelper", StringComparison.Ordinal) ||
            normalized.StartsWith("steam_app_", StringComparison.Ordinal))
        {
            return "steam";
        }

        return normalized;
    }

    public static Avalonia.Media.IImage? LoadBitmapSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return _imageCache.GetOrAdd(path, LoadBitmapInternal);
    }

    private static Avalonia.Media.IImage? LoadBitmapInternal(string path)
    {
        try
        {
            if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(path);
                if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = Avalonia.Platform.AssetLoader.Open(uri);
                    return LoadSvgBitmap(stream);
                }
                else
                {
                    using var stream = Avalonia.Platform.AssetLoader.Open(uri);
                    return new Bitmap(stream);
                }
            }
            else if (File.Exists(path))
            {
                if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = File.OpenRead(path);
                    return LoadSvgBitmap(stream);
                }
                else
                {
                    return new Bitmap(path);
                }
            }
        }
        catch 
        { 
            // Ignore format exceptions
        }
        return null;
    }

    // SvgImage is an AvaloniaObject and belongs to its creating thread.
    // Decode to a high-resolution bitmap before publishing from worker threads.
    internal static Bitmap? LoadSvgBitmap(Stream stream, int pixelSize = 256)
    {
        using var svg = Avalonia.Svg.Skia.SvgSource.LoadFromStream(stream);
        if (svg?.Picture is not { } picture) return null;
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        var scale = Math.Min((float)pixelSize / bounds.Width, (float)pixelSize / bounds.Height);
        var info = new SkiaSharp.SKImageInfo(
            Math.Max(1, (int)Math.Ceiling(bounds.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(bounds.Height * scale)),
            SkiaSharp.SKColorType.Bgra8888,
            SkiaSharp.SKAlphaType.Premul);
        using var pixels = new SkiaSharp.SKBitmap(info);
        using (var canvas = new SkiaSharp.SKCanvas(pixels))
        {
            canvas.Clear(SkiaSharp.SKColors.Transparent);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        }
        return new Bitmap(
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul,
            pixels.GetPixels(),
            new Avalonia.PixelSize(pixels.Width, pixels.Height),
            new Avalonia.Vector(96, 96),
            pixels.RowBytes);
    }

    public static (string name, string iconPath) ParseDesktopFile(string filePath) =>
        ParseDesktopFile(filePath, IsCurrentThemeLight());

    public static (string name, string iconPath) ParseDesktopFile(string filePath, bool isLight)
    {
        string name = Path.GetFileNameWithoutExtension(filePath);
        string iconPath = string.Empty;
        var entry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var resolvedFile = File.Exists(filePath) ? filePath : FindDesktopEntryFilePath([Path.GetFileNameWithoutExtension(filePath), filePath]);
            if (!string.IsNullOrWhiteSpace(resolvedFile) && File.Exists(resolvedFile))
            {
                bool inDesktopEntry = false;
                foreach (var line in File.ReadLines(resolvedFile))
                {
                    var trimmed = line.Trim();
                    if (trimmed == "[Desktop Entry]")
                    {
                        inDesktopEntry = true;
                        continue;
                    }
                    else if (trimmed.StartsWith("["))
                    {
                        inDesktopEntry = false;
                    }

                    if (inDesktopEntry)
                    {
                        var separator = trimmed.IndexOf('=');
                        if (separator > 0) entry[trimmed[..separator]] = trimmed[(separator + 1)..].Trim();
                        if (trimmed.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                        {
                            name = trimmed.Substring(5).Trim();
                        }
                        else if (trimmed.StartsWith("Icon=", StringComparison.OrdinalIgnoreCase))
                        {
                            var iconValue = trimmed.Substring(5).Trim();
                            iconPath = ResolveIconPath(iconValue, isLight);
                        }
                    }
                }
            }
        }
        catch { }

        name = DaisyOS.Core.Models.DesktopEntryLocalization.GetValue(entry, "Name") ?? name;
        if (entry.TryGetValue("Icon", out var icon) && icon.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase))
            name = Loc.Get(name);
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            var fallback = GetAppIconPath(Path.GetFileNameWithoutExtension(filePath), isLight);
            if (!string.IsNullOrWhiteSpace(fallback)) iconPath = fallback;
        }
        return (name, iconPath);
    }
}
