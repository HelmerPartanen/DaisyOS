using System.IO;
using System.Text;

namespace DaisyOS.System.Gaming;

public static class GameArtworkValidator
{
    private const long MinFileSizeBytes = 512; // Reject suspiciously tiny files (< 512 bytes)

    public static bool IsValidImageFile(string filePath, int minWidth = 0, int minHeight = 0)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < MinFileSizeBytes)
            {
                return false;
            }

            using var stream = File.OpenRead(filePath);
            var buffer = new byte[Math.Min(1024, (int)fileInfo.Length)];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read < 4)
            {
                return false;
            }

            // Reject HTML/XML error payloads
            var prefixString = Encoding.UTF8.GetString(buffer, 0, read).TrimStart();
            if (prefixString.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                prefixString.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
                prefixString.StartsWith("{\"error\"", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Check magic bytes for JPEG, PNG, WEBP, SVG, GIF
            if (!HasValidMagicBytes(buffer, prefixString))
            {
                return false;
            }

            // Optional dimension check via header inspection
            if (minWidth > 0 || minHeight > 0)
            {
                var (width, height) = TryReadDimensions(buffer, filePath);
                if (width.HasValue && height.HasValue)
                {
                    if (width.Value < minWidth || height.Value < minHeight)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsValidImageResponse(string? contentType, byte[] data, int minWidth = 0, int minHeight = 0)
    {
        if (data == null || data.Length < MinFileSizeBytes)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var prefixLength = Math.Min(1024, data.Length);
        var prefixString = Encoding.UTF8.GetString(data, 0, prefixLength).TrimStart();

        if (prefixString.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
            prefixString.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
            prefixString.StartsWith("{\"error\"", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return HasValidMagicBytes(data, prefixString);
    }

    private static bool HasValidMagicBytes(byte[] buffer, string prefixString)
    {
        // JPEG: FF D8 FF
        if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
        {
            return true;
        }

        // PNG: 89 50 4E 47 (89 'P' 'N' 'G')
        if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
        {
            return true;
        }

        // WEBP: RIFF...WEBP
        if (buffer.Length >= 12 &&
            buffer[0] == (byte)'R' && buffer[1] == (byte)'I' && buffer[2] == (byte)'F' && buffer[3] == (byte)'F' &&
            buffer[8] == (byte)'W' && buffer[9] == (byte)'E' && buffer[10] == (byte)'B' && buffer[11] == (byte)'P')
        {
            return true;
        }

        // SVG: starts with <svg or contains <svg
        if (prefixString.Contains("<svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // GIF: GIF87a or GIF89a
        if (buffer.Length >= 6 &&
            buffer[0] == (byte)'G' && buffer[1] == (byte)'I' && buffer[2] == (byte)'F' &&
            buffer[3] == (byte)'8' && (buffer[4] == (byte)'7' || buffer[4] == (byte)'9') && buffer[5] == (byte)'a')
        {
            return true;
        }

        return false;
    }

    private static (int? Width, int? Height) TryReadDimensions(byte[] buffer, string filePath)
    {
        // Simple PNG header dimension parser (bytes 16-23: width and height in big endian)
        if (buffer.Length >= 24 && buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
        {
            var width = (buffer[16] << 24) | (buffer[17] << 16) | (buffer[18] << 8) | buffer[19];
            var height = (buffer[20] << 24) | (buffer[21] << 16) | (buffer[22] << 8) | buffer[23];
            return (width, height);
        }

        return (null, null);
    }
}
