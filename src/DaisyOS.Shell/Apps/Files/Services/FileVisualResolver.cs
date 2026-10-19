using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Apps.Files.Models;

namespace DaisyOS.Shell.Apps.Files.Services;

/// <summary>
/// Produces bounded visuals for the file list. Image files get a
/// real thumbnail; other files use the icon of their preferred application
/// whenever that application advertises one through mimeapps.
/// </summary>
internal static class FileVisualResolver
{
    private const long MaximumPreviewSourceBytes = 64L * 1024 * 1024;

    private static Lazy<IReadOnlyDictionary<string, string>> DefaultApplications = new(LoadDefaultApplications);
    private static readonly ConcurrentDictionary<(string Extension, long Generation, int Size), Lazy<IImage?>> ApplicationIcons = new();
    private static long _associationGeneration;
    private static readonly ThumbnailCache Thumbnails = new();
    internal static int ThumbnailCacheCount => Thumbnails.Count;
    public static void InvalidateAssociations()
    {
        Interlocked.Increment(ref _associationGeneration);
        DefaultApplications = new(LoadDefaultApplications);
        ApplicationIcons.Clear();
        Thumbnails.Clear();
        DesktopItemLoader.InvalidateIconCaches();
    }

    private static readonly IReadOnlyDictionary<string, string> MimeTypesByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "text/javascript",
        [".ts"] = "text/plain",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".flac"] = "audio/flac",
        [".ogg"] = "audio/ogg",
        [".aac"] = "audio/aac",
        [".mp4"] = "video/mp4",
        [".mkv"] = "video/x-matroska",
        [".avi"] = "video/x-msvideo",
        [".mov"] = "video/quicktime",
        [".webm"] = "video/webm",
        [".zip"] = "application/zip",
        [".7z"] = "application/x-7z-compressed",
        [".rar"] = "application/vnd.rar",
        [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip"
    };

    private static readonly IReadOnlyDictionary<string, string[]> FallbackApplicationIdsByExtension = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ["org.gnome.Evince", "okular", "org.pwmt.zathura"],
        [".txt"] = ["org.gnome.TextEditor", "org.kde.kate", "code"],
        [".md"] = ["org.gnome.TextEditor", "org.kde.kate", "code"],
        [".json"] = ["code", "org.kde.kate", "org.gnome.TextEditor"],
        [".xml"] = ["code", "org.kde.kate", "org.gnome.TextEditor"],
        [".html"] = ["firefox", "google-chrome", "code"],
        [".htm"] = ["firefox", "google-chrome", "code"],
        [".css"] = ["code", "org.kde.kate"],
        [".js"] = ["code", "org.kde.kate"],
        [".ts"] = ["code", "org.kde.kate"],
        [".mp3"] = ["spotify", "org.videolan.VLC", "vlc"],
        [".wav"] = ["org.videolan.VLC", "vlc"],
        [".flac"] = ["org.videolan.VLC", "vlc"],
        [".ogg"] = ["org.videolan.VLC", "vlc"],
        [".mp4"] = ["org.videolan.VLC", "vlc", "mpv"],
        [".mkv"] = ["org.videolan.VLC", "vlc", "mpv"],
        [".avi"] = ["org.videolan.VLC", "vlc", "mpv"],
        [".mov"] = ["org.videolan.VLC", "vlc", "mpv"],
        [".webm"] = ["org.videolan.VLC", "vlc", "mpv"],
        [".zip"] = ["org.gnome.FileRoller", "org.kde.ark"],
        [".7z"] = ["org.gnome.FileRoller", "org.kde.ark"],
        [".rar"] = ["org.gnome.FileRoller", "org.kde.ark"],
        [".tar"] = ["org.gnome.FileRoller", "org.kde.ark"],
        [".gz"] = ["org.gnome.FileRoller", "org.kde.ark"]
    };

    internal static string GetMimeIconName(string extension) =>
        MimeTypesByExtension.TryGetValue(extension, out var mime) ? mime.Replace('/', '-') :
        IsImage(extension) ? "image-x-generic" : "application-octet-stream";

    internal static bool IsImage(string extension) => extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg";
    internal static bool CanResolve(FileEntrySnapshot snapshot) => !snapshot.IsDirectory &&
        (IsImage(snapshot.Extension) || snapshot.Extension == ".desktop" || MimeTypesByExtension.ContainsKey(snapshot.Extension));

    internal static Task<VisualLease> ResolveLeaseAsync(FileEntrySnapshot snapshot, int size, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Thumbnails.Acquire(snapshot, size, () => Resolve(snapshot, size, cancellationToken));
        }, cancellationToken);

    private static FileVisual Resolve(FileEntrySnapshot snapshot, int size, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.IsDirectory) return FileVisual.Empty;
        try
        {
            if (IsImage(snapshot.Extension))
            {
                var dimensions = TryReadImageDimensions(snapshot.FullPath);
                return TryLoadThumbnail(snapshot, size, out var thumbnail)
                    ? new FileVisual(thumbnail, null, dimensions)
                    : new FileVisual(null, null, dimensions);
            }
            if (snapshot.Extension == ".desktop")
            {
                var (_, iconPath) = DesktopItemLoader.ParseDesktopFile(snapshot.FullPath);
                return new FileVisual(null, DesktopItemLoader.LoadBitmapSafe(iconPath), string.Empty);
            }
            var generation = Interlocked.Read(ref _associationGeneration);
            var icon = ApplicationIcons.GetOrAdd((snapshot.Extension, generation, size), key =>
                new Lazy<IImage?>(() => ResolveDefaultApplicationIcon(key.Extension), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            return new FileVisual(null, icon, string.Empty);
        }
        catch (OperationCanceledException) { throw; }
        catch { return FileVisual.Empty; }
    }

    private static bool TryLoadThumbnail(FileEntrySnapshot snapshot, int size, out IImage? image)
    {
        image = null;
        try
        {
            var path = snapshot.FullPath;
            if (snapshot.Length <= 0 || snapshot.Length > MaximumPreviewSourceBytes)
            {
                return false;
            }

            if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                using var svg = Avalonia.Svg.Skia.SvgSource.Load(path, null);
                if (svg?.Picture is not null)
                {
                    var cullRect = svg.Picture.CullRect;
                    if (cullRect.Width > 0 && cullRect.Height > 0)
                    {
                        var scale = Math.Min((float)size / cullRect.Width, (float)size / cullRect.Height);
                        var width = Math.Max(1, (int)Math.Round(cullRect.Width * scale));
                        var height = Math.Max(1, (int)Math.Round(cullRect.Height * scale));

                        using var skBitmap = new SkiaSharp.SKBitmap(width, height);
                        using (var canvas = new SkiaSharp.SKCanvas(skBitmap))
                        {
                            canvas.Clear(SkiaSharp.SKColors.Transparent);
                            canvas.Scale(scale);
                            canvas.DrawPicture(svg.Picture);
                            canvas.Flush();
                        }

                        using var imageStream = new MemoryStream();
                        skBitmap.Encode(imageStream, SkiaSharp.SKEncodedImageFormat.Png, 100);
                        imageStream.Position = 0;
                        image = new Bitmap(imageStream);
                        return true;
                    }
                }
                return false;
            }

            using var stream = File.OpenRead(path);
            image = Bitmap.DecodeToWidth(stream, size);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Header-only probing gives useful image dimensions without decoding a
    // huge source at preview time. Unsupported image formats simply omit this
    // optional bit of metadata rather than risking a costly full decode.
    private static string TryReadImageDimensions(string path)
    {
        try
        {
            if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                return TryReadSvgDimensions(path);
            }

            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            var header = reader.ReadBytes(32);
            if (header.Length < 10) return string.Empty;

            if (header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && header.Length >= 24)
            {
                return $"{ReadBigEndian(header, 16)} × {ReadBigEndian(header, 20)}";
            }

            if (header.AsSpan(0, 3).SequenceEqual("GIF"u8) && header.Length >= 10)
            {
                return $"{ReadLittleEndian(header, 6, 2)} × {ReadLittleEndian(header, 8, 2)}";
            }

            if (header[0] == 'B' && header[1] == 'M' && header.Length >= 26)
            {
                return $"{ReadLittleEndian(header, 18, 4)} × {ReadLittleEndian(header, 22, 4)}";
            }

            if (header[0] == 0xFF && header[1] == 0xD8)
            {
                stream.Position = 2;
                while (stream.Position < stream.Length)
                {
                    if (reader.ReadByte() != 0xFF) continue;
                    var marker = reader.ReadByte();
                    while (marker == 0xFF) marker = reader.ReadByte();
                    if (marker is 0xD8 or 0xD9) continue;
                    var length = (reader.ReadByte() << 8) | reader.ReadByte();
                    if (length < 2 || stream.Position + length - 2 > stream.Length) break;
                    if (marker is (>= 0xC0 and <= 0xC3) or (>= 0xC5 and <= 0xC7) or (>= 0xC9 and <= 0xCB) or (>= 0xCD and <= 0xCF))
                    {
                        _ = reader.ReadByte();
                        var height = (reader.ReadByte() << 8) | reader.ReadByte();
                        var width = (reader.ReadByte() << 8) | reader.ReadByte();
                        return $"{width} × {height}";
                    }
                    stream.Position += length - 2;
                }
            }
        }
        catch
        {
            // Preview metadata is optional.
        }

        return string.Empty;
    }

    private static int ReadBigEndian(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private static int ReadLittleEndian(byte[] bytes, int offset, int length)
    {
        var value = 0;
        for (var index = 0; index < length; index++) value |= bytes[offset + index] << (8 * index);
        return value;
    }

    private static IImage? ResolveDefaultApplicationIcon(string extension)
    {
        try
        {
            string? defaultDesktopId = null;
            if (MimeTypesByExtension.TryGetValue(extension, out var mimeType))
            {
                DefaultApplications.Value.TryGetValue(mimeType, out defaultDesktopId);
            }

            var candidates = new List<string?>();
            if (!string.IsNullOrWhiteSpace(defaultDesktopId))
            {
                candidates.Add(defaultDesktopId);
            }
            if (FallbackApplicationIdsByExtension.TryGetValue(extension, out var fallbackIds))
            {
                candidates.AddRange(fallbackIds);
            }

            return candidates.Count == 0 ? null : DesktopItemLoader.LoadApplicationIcon(candidates);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadDefaultApplications()
    {
        var paths = new List<string>();
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        paths.Add(Path.Combine(string.IsNullOrWhiteSpace(configHome) ? Path.Combine(home, ".config") : configHome, "mimeapps.list"));
        paths.Add(Path.Combine(string.IsNullOrWhiteSpace(dataHome) ? Path.Combine(home, ".local", "share") : dataHome, "applications", "mimeapps.list"));

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Where(File.Exists))
        {
            var inDefaults = false;
            try
            {
                foreach (var rawLine in File.ReadLines(path))
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith('['))
                    {
                        inDefaults = string.Equals(line, "[Default Applications]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!inDefaults || line.Length == 0 || line.StartsWith('#'))
                    {
                        continue;
                    }

                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var desktopId = line[(separator + 1)..]
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(desktopId))
                    {
                        result[line[..separator]] = desktopId;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // File presentation must remain available even when user configuration is unreadable.
            }
        }

        return result;
    }

    private static string TryReadSvgDimensions(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new StreamReader(stream);
            var buffer = new char[4096];
            var read = reader.Read(buffer, 0, buffer.Length);
            var content = new string(buffer, 0, read);

            var viewBoxIndex = content.IndexOf("viewBox", StringComparison.OrdinalIgnoreCase);
            if (viewBoxIndex >= 0)
            {
                var quoteStart = content.IndexOfAny(['"', '\''], viewBoxIndex);
                if (quoteStart >= 0)
                {
                    var quoteEnd = content.IndexOf(content[quoteStart], quoteStart + 1);
                    if (quoteEnd > quoteStart)
                    {
                        var val = content.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).Trim();
                        var parts = val.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 4 && double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var w) &&
                            double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var h))
                        {
                            return $"{(int)Math.Round(w)} × {(int)Math.Round(h)}";
                        }
                    }
                }
            }

            var wMatch = TryReadSvgAttribute(content, "width");
            var hMatch = TryReadSvgAttribute(content, "height");
            if (!string.IsNullOrEmpty(wMatch) && !string.IsNullOrEmpty(hMatch))
            {
                return $"{wMatch} × {hMatch}";
            }
        }
        catch { }
        return string.Empty;
    }

    private static string TryReadSvgAttribute(string content, string attr)
    {
        try
        {
            var idx = content.IndexOf(attr, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                var eq = content.IndexOf('=', idx);
                if (eq > idx && eq <= idx + attr.Length + 2)
                {
                    var qStart = content.IndexOfAny(['"', '\''], eq);
                    if (qStart > eq && qStart <= eq + 2)
                    {
                        var qEnd = content.IndexOf(content[qStart], qStart + 1);
                        if (qEnd > qStart)
                        {
                            var val = content.Substring(qStart + 1, qEnd - qStart - 1).Trim();
                            var numStr = new string(val.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
                            if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                            {
                                return ((int)Math.Round(d)).ToString();
                            }
                        }
                    }
                }
                idx = content.IndexOf(attr, idx + 1, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }
        return string.Empty;
    }
}

internal sealed record FileVisual(IImage? Thumbnail, IImage? ApplicationIcon, string ImageDimensions)
{
    public static readonly FileVisual Empty = new(null, null, string.Empty);
}
