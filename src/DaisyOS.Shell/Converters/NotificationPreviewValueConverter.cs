using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

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
        var source = value as string;
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
                return Bitmap.DecodeToWidth(stream, PreviewDecodeWidth);
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
            return Bitmap.DecodeToWidth(file, PreviewDecodeWidth);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
        {
            return null;
        }
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
