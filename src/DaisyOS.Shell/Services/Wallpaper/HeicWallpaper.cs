using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DaisyOS.Shell.Services.Wallpaper;

/// <summary>Decode only the selected HEIF image, retaining a reusable PNG outside the source file.</summary>
public static class HeicWallpaper
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private const string Library = "libheif.so.1";
    private const long MaximumCacheBytes = 512L * 1024 * 1024;

    public static async Task<string> ResolveFrameAsync(string path, bool dark, DateTimeOffset now, CancellationToken token = default)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(() => ResolveFrame(path, dark, now, token), token).ConfigureAwait(false); }
        catch (DllNotFoundException error) { throw new NotSupportedException("HEIC wallpapers require libheif and its HEVC decoder.", error); }
        finally { Gate.Release(); }
    }

    private static string ResolveFrame(string path, bool dark, DateTimeOffset now, CancellationToken token)
    {
        path = Path.GetFullPath(path);
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("HEIC wallpaper was not found.", path);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"heic-v1\n{path}\n{file.Length}\n{file.LastWriteTimeUtc.Ticks}")));
        var directory = Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { } configured && Path.IsPathRooted(configured)
            ? configured : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "DaisyOS", "heic-wallpapers");
        Directory.CreateDirectory(directory);
        var context = heif_context_alloc();
        if (context == IntPtr.Zero) throw new InvalidDataException("Could not open HEIC wallpaper decoder.");
        IntPtr primary = IntPtr.Zero, handle = IntPtr.Zero, image = IntPtr.Zero;
        try
        {
            Check(heif_context_read_from_file(context, path, IntPtr.Zero));
            var count = heif_context_get_number_of_top_level_images(context);
            if (count is < 1 or > 8192) throw new InvalidDataException("Unsupported HEIC wallpaper image count.");
            var ids = new uint[count];
            if (heif_context_get_list_of_top_level_image_IDs(context, ids, count) != count) throw new InvalidDataException("Incomplete HEIC wallpaper image list.");
            Check(heif_context_get_primary_image_ID(context, out var primaryId));
            Check(heif_context_get_primary_image_handle(context, out primary));
            var primaryIndex = Math.Max(0, Array.IndexOf(ids, primaryId));
            var index = primaryIndex;
            var metadataIds = new uint[64];
            var metadataCount = heif_image_handle_get_list_of_metadata_block_IDs(primary, IntPtr.Zero, metadataIds, metadataIds.Length);
            foreach (var id in metadataIds.Take(Math.Clamp(metadataCount, 0, metadataIds.Length)))
            {
                if (Marshal.PtrToStringUTF8(heif_image_handle_get_metadata_content_type(primary, id)) != "application/rdf+xml") continue;
                var size = heif_image_handle_get_metadata_size(primary, id);
                if (size is 0 or > 2 * 1024 * 1024) continue;
                var bytes = new byte[(int)size];
                Check(heif_image_handle_get_metadata(primary, id, bytes));
                index = HeicWallpaperSchedule.FromXmp(Encoding.UTF8.GetString(bytes)).SelectFrame(count, primaryIndex, dark, now);
                break;
            }
            var target = Path.Combine(directory, $"{identity}-{ids[index]}.png");
            token.ThrowIfCancellationRequested();
            if (File.Exists(target) && new FileInfo(target).Length > 0) return target;
            Check(heif_context_get_image_handle(context, ids[index], out handle));
            Check(heif_decode_image(handle, out image, 1 /* RGB */, 11 /* RGBA */, IntPtr.Zero));
            var width = heif_image_get_width(image, 10 /* interleaved */);
            var height = heif_image_get_height(image, 10);
            if (width <= 0 || height <= 0 || (long)width * height > 100_000_000) throw new InvalidDataException("Unsupported HEIC wallpaper dimensions.");
            var plane = heif_image_get_plane_readonly(image, 10, out var stride);
            if (plane == IntPtr.Zero || stride < checked(width * 4)) throw new InvalidDataException("Invalid decoded HEIC wallpaper.");
            token.ThrowIfCancellationRequested();
            using var bitmap = new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul, plane, new PixelSize(width, height), new Vector(96, 96), stride);
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                bitmap.Save(temporary, PngBitmapEncoderOptions.Default);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, target, overwrite: true);
            }
            finally { File.Delete(temporary); }
            TrimCache(directory, target);
            return target;
        }
        finally
        {
            if (image != IntPtr.Zero) heif_image_release(image);
            if (handle != IntPtr.Zero) heif_image_handle_release(handle);
            if (primary != IntPtr.Zero) heif_image_handle_release(primary);
            heif_context_free(context);
        }
    }

    private static void TrimCache(string directory, string active)
    {
        try
        {
            var files = new DirectoryInfo(directory).GetFiles("*.png").OrderBy(file => file.LastWriteTimeUtc).ToArray();
            var bytes = files.Sum(file => file.Length);
            foreach (var file in files)
            {
                if (bytes <= MaximumCacheBytes) break;
                if (file.FullName == active) continue;
                var length = file.Length;
                file.Delete();
                bytes -= length;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HeifError { public int Code; public int Subcode; public IntPtr Message; }
    private static void Check(HeifError error)
    {
        if (error.Code != 0) throw new InvalidDataException("HEIC wallpaper: " + Marshal.PtrToStringUTF8(error.Message));
    }

    [DllImport(Library)] private static extern IntPtr heif_context_alloc();
    [DllImport(Library)] private static extern void heif_context_free(IntPtr context);
    [DllImport(Library)] private static extern HeifError heif_context_read_from_file(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr options);
    [DllImport(Library)] private static extern int heif_context_get_number_of_top_level_images(IntPtr context);
    [DllImport(Library)] private static extern int heif_context_get_list_of_top_level_image_IDs(IntPtr context, [Out] uint[] ids, int count);
    [DllImport(Library)] private static extern HeifError heif_context_get_primary_image_ID(IntPtr context, out uint id);
    [DllImport(Library)] private static extern HeifError heif_context_get_primary_image_handle(IntPtr context, out IntPtr handle);
    [DllImport(Library)] private static extern HeifError heif_context_get_image_handle(IntPtr context, uint id, out IntPtr handle);
    [DllImport(Library)] private static extern void heif_image_handle_release(IntPtr handle);
    [DllImport(Library)] private static extern int heif_image_handle_get_list_of_metadata_block_IDs(IntPtr handle, IntPtr type, [Out] uint[] ids, int count);
    [DllImport(Library)] private static extern nuint heif_image_handle_get_metadata_size(IntPtr handle, uint id);
    [DllImport(Library)] private static extern IntPtr heif_image_handle_get_metadata_content_type(IntPtr handle, uint id);
    [DllImport(Library)] private static extern HeifError heif_image_handle_get_metadata(IntPtr handle, uint id, [Out] byte[] bytes);
    [DllImport(Library)] private static extern HeifError heif_decode_image(IntPtr handle, out IntPtr image, int colorspace, int chroma, IntPtr options);
    [DllImport(Library)] private static extern int heif_image_get_width(IntPtr image, int channel);
    [DllImport(Library)] private static extern int heif_image_get_height(IntPtr image, int channel);
    [DllImport(Library)] private static extern IntPtr heif_image_get_plane_readonly(IntPtr image, int channel, out int stride);
    [DllImport(Library)] private static extern void heif_image_release(IntPtr image);
}
