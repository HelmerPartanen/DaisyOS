using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Notifications;
using SkiaSharp;

namespace DaisyOS.Shell.Converters;

/// <summary>
/// Decodes the deliberately small preview surface allowed in notifications.
/// Notifications can come from LAN peers, so never decode arbitrary paths or
/// unrestricted inline payloads here.
/// </summary>
public sealed class NotificationPreviewValueConverter : IValueConverter
{
    private const int MaximumInlineBytes = 1024 * 1024;
    private const long MaximumFileBytes = 32L * 1024 * 1024;
    private const int PreviewDecodeWidth = 360;

    private string? _cachedSource;
    private object? _cachedImage;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var source = value is NotificationItem notification ? NotificationArtwork.Resolve(notification).PreviewSource : value as string;
        if (_cachedSource != source)
        {
            _cachedSource = source;
            _cachedImage = string.IsNullOrWhiteSpace(source) ? null : Decode(source);
        }
        return parameter is "Visible" ? _cachedImage is not null : _cachedImage;
    }

    private static object? Decode(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile) source = uri.LocalPath;

        try
        {
            if (TryDecodeInlineImage(source, out var bytes))
            {
                using var stream = new MemoryStream(bytes, writable: false);
                return DecodeBitmap(stream);
            }

            if (!Path.IsPathFullyQualified(source) || !File.Exists(source) || !IsSupportedImage(source))
            {
                return null;
            }

            var info = new FileInfo(source);
            if (info.Length <= 0 || info.Length > MaximumFileBytes)
            {
                return null;
            }

            using var file = File.OpenRead(source);
            return DecodeBitmap(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
        {
            return null;
        }
    }

    private static Bitmap? DecodeBitmap(Stream stream)
    {
        // The current Avalonia decoder dereferences a null codec for corrupt
        // input. Validate its header first, and bound full-resolution PNG work.
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
            (long)codec.Info.Width * codec.Info.Height > 16L * 1024 * 1024) return null;
        stream.Position = 0;
        return Bitmap.DecodeToWidth(stream, Math.Min(PreviewDecodeWidth, codec.Info.Width));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static bool TryDecodeInlineImage(string value, out byte[] bytes)
    {
        bytes = [];
        if (!value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separator = value.IndexOf(',');
        if (separator < 0 || !value[..separator].Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encoded = value[(separator + 1)..];
        if (encoded.Length > (MaximumInlineBytes * 4 / 3) + 4)
        {
            return false;
        }

        bytes = global::System.Convert.FromBase64String(encoded);
        return bytes is { Length: > 0 and <= MaximumInlineBytes };
    }

    private static bool IsSupportedImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp";
}
