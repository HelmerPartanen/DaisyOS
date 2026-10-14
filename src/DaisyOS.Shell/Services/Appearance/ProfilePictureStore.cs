using SkiaSharp;

namespace DaisyOS.Shell.Services.Appearance;

/// <summary>Imports a bounded, shell-owned avatar rather than retaining an external file reference.</summary>
internal static class ProfilePictureStore
{
    public static async Task<string> ImportAsync(string source, string preferencesPath)
    {
        return await Task.Run(() =>
        {
            using var input = File.OpenRead(source);
            if (input.Length > 16 * 1024 * 1024) throw new InvalidDataException("The picture must be smaller than 16 MB.");
            using var codec = SKCodec.Create(input);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
                (long)codec.Info.Width * codec.Info.Height > 24 * 1024 * 1024)
                throw new InvalidDataException("Choose a supported picture no larger than 24 megapixels.");
            using var decoded = SKBitmap.Decode(codec);
            if (decoded is null) throw new InvalidDataException("The picture could not be decoded.");
            using var oriented = Orient(decoded, codec.EncodedOrigin);
            var scale = Math.Min(1, 512d / Math.Max(oriented.Width, oriented.Height));
            using var resized = oriented.Resize(new SKImageInfo(Math.Max(1, (int)(oriented.Width * scale)),
                Math.Max(1, (int)(oriented.Height * scale))), new SKSamplingOptions(SKCubicResampler.Mitchell));
            if (resized is null) throw new InvalidDataException("The picture could not be resized.");
            var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(preferencesPath))!, "profile-pictures");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using var data = resized.Encode(SKEncodedImageFormat.Png, 100);
                using var output = File.Create(path);
                data.SaveTo(output);
                return path;
            }
            catch { File.Delete(path); throw; }
        }).ConfigureAwait(false);
    }

    public static string? FindSingleRecoverablePicture(string preferencesPath)
    {
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(preferencesPath))!, "profile-pictures");
        try
        {
            var pictures = Directory.GetFiles(directory, "*.png", SearchOption.TopDirectoryOnly);
            if (pictures.Length != 1) return null;

            using var stream = File.OpenRead(pictures[0]);
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
                codec.Info.Width > 512 || codec.Info.Height > 512) return null;
            return Path.GetFullPath(pictures[0]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(result);
        var w = source.Width;
        var h = source.Height;
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity
        };
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }

    public static void RemoveOwned(string path, string preferencesPath)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(preferencesPath))!, "profile-pictures");
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), directory, StringComparison.Ordinal)) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
