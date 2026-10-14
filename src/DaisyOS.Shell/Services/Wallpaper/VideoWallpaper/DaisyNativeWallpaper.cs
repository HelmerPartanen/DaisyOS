using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace DaisyOS.Shell.Services.Wallpaper.VideoWallpaper;

/// <summary>
/// P/Invoke declarations for libdaisy_live_wallpaper.so.
/// Wraps the C API defined in include/daisy/live_wallpaper.h.
/// </summary>
public static partial class DaisyNativeWallpaper
{
    private const string LibName = "daisy_live_wallpaper";

    static DaisyNativeWallpaper()
    {
        NativeLibrary.SetDllImportResolver(typeof(DaisyNativeWallpaper).Assembly, ResolveNativeLibrary);
    }

    internal static void EnsureResolver() { }

    private static nint ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName is LibName or "daisy_mpv_wallpaper")
        {
            string baseDir = AppContext.BaseDirectory;
            var fileName = libraryName == LibName ? "libdaisy_live_wallpaper.so" : "libdaisy_mpv_wallpaper.so";
            string[] searchPaths = new[]
            {
                Path.Combine(baseDir, fileName),
                Path.Combine(baseDir, "runtimes/linux-x64/native", fileName),
                Path.GetFullPath(Path.Combine(baseDir, "../../../src/native", libraryName == LibName ? "daisy_live_wallpaper" : "daisy_mpv_wallpaper", "build", fileName)),
                Path.GetFullPath(Path.Combine("src/native", libraryName == LibName ? "daisy_live_wallpaper" : "daisy_mpv_wallpaper", "build", fileName)),
                Path.Combine("/usr/local/lib", fileName)
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out nint handle))
                {
                    return handle;
                }
            }
        }

        return nint.Zero;
    }

    // ── Native structs ────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors DaisyVideoFrame from live_wallpaper.h exactly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NativeVideoFrame
    {
        public int Width;
        public int Height;
        public uint PixelFormat;
        public ulong Modifier;

        // int fd[4]
        public int Fd0, Fd1, Fd2, Fd3;
        // uint32_t stride[4]
        public uint Stride0, Stride1, Stride2, Stride3;
        // uint32_t offset[4]
        public uint Offset0, Offset1, Offset2, Offset3;

        public int NumPlanes;
        public long PtsUs;
        public int AcquireFence;
        public int IsHardware;

        // Convenience accessors
        public readonly int[]    Fd     => new[] { Fd0, Fd1, Fd2, Fd3 };
        public readonly uint[]   Stride => new[] { Stride0, Stride1, Stride2, Stride3 };
        public readonly uint[]   Offset => new[] { Offset0, Offset1, Offset2, Offset3 };
    }

    /// <summary>
    /// Mirrors DaisyWallpaperStats from live_wallpaper.h using inline fixed buffers.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NativeWallpaperStats
    {
        public fixed byte DecoderBackend[64];
        public fixed byte Codec[32];

        public int    SrcWidth;
        public int    SrcHeight;
        public double SrcFps;
        public double DisplayedFps;
        public int    DecodedFrames;
        public int    DroppedFrames;
        public int    DecodeTimeUs;
        public int    QueueDepth;
        public int    ZeroCopy;
        public int    DmabufActive;
        public int    EglImportActive;
        public int    DrmOverlay;
        public int    CpuFallbackCount;
        public int    GpuCopyFallback;
        public int    IsPlaying;

        public readonly string DecoderBackendString
        {
            get
            {
                fixed (byte* p = DecoderBackend)
                {
                    return Encoding.UTF8.GetString(p, StrLen(p, 64));
                }
            }
        }

        public readonly string CodecString
        {
            get
            {
                fixed (byte* p = Codec)
                {
                    return Encoding.UTF8.GetString(p, StrLen(p, 32));
                }
            }
        }

        private static int StrLen(byte* p, int maxLen)
        {
            int len = 0;
            while (len < maxLen && p[len] != 0) len++;
            return len;
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    [LibraryImport(LibName, EntryPoint = "daisy_wallpaper_set_frame_callback")]
    public static partial void SetFrameCallback(nint handle, nint callback);

    [LibraryImport(LibName, EntryPoint = "daisy_wallpaper_create")]
    public static partial nint Create();

    [LibraryImport(LibName, EntryPoint = "daisy_wallpaper_load", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Load(nint handle, string path);

    [LibraryImport(LibName, EntryPoint = "daisy_wallpaper_play")]
    public static partial void Play(nint handle);

    [LibraryImport(LibName, EntryPoint = "daisy_wallpaper_pause")]
    public static partial void Pause(nint handle);

    [LibraryImport(LibName, EntryPoint = "daisy_wallpaper_stop")]
    public static partial void Stop(nint handle);

    [LibraryImport(LibName, EntryPoint = "daisy_wallpaper_destroy")]
    public static partial void Destroy(nint handle);

    [LibraryImport(LibName, EntryPoint = "daisy_wallpaper_set_fps_cap")]
    public static partial void SetFpsCap(nint handle, int fps);

    [LibraryImport(LibName, EntryPoint = "daisy_wallpaper_set_output_size")]
    public static partial void SetOutputSize(nint handle, int width, int height);

    // ── Frame access ──────────────────────────────────────────────────────

    [LibraryImport(LibName, EntryPoint = "daisy_wallpaper_try_get_frame")]
    public static partial int TryGetFrame(nint handle, ref NativeVideoFrame frame);

    // ── Diagnostics ───────────────────────────────────────────────────────

    [LibraryImport(LibName, EntryPoint = "daisy_wallpaper_get_stats")]
    public static partial void GetStats(nint handle, ref NativeWallpaperStats stats);

    [LibraryImport(LibName, EntryPoint = "daisy_wallpaper_print_stats")]
    public static partial void PrintStats(nint handle);
}
