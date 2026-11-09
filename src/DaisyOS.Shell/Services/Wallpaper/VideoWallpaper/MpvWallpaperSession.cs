using System.Runtime.InteropServices;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Wayland;

namespace DaisyOS.Shell.Services.Wallpaper.VideoWallpaper;

internal sealed class MpvWallpaperSession : IDisposable
{
    private readonly object _sync = new();
    private nint _handle;
    private IDisposable? _hostLease;
    public bool IsPaused { get; private set; }
    public static bool Enabled => OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("DAISYOS_VIDEO_BACKEND") != "legacy";
    public static bool CanHost(Window? host) =>
        Enabled && string.Equals(host?.PlatformImpl?.GetType().Name, "LayerShellWindowImpl", StringComparison.Ordinal);

    public static async Task<MpvWallpaperSession> CreateAsync(Window host, string path, int width, int height,
        double scale, VideoMetadata metadata, double startSeconds = 0)
    {
        var session = new MpvWallpaperSession();
        try
        {
            session._hostLease = await LayerShellWindow.AttachNativeChildAsync(host, (display, parent) =>
            {
                var color = NativeColor.FromMetadata(metadata);
                color.StartSeconds = startSeconds;
                session._handle = Native.Create(display, parent, Path.GetFullPath(path), width, height, scale, ref color);
                if (session._handle == 0) throw new IOException("The video GPU surface could not be created.");
                return session;
            });
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    public void Pause(bool paused)
    {
        lock (_sync)
        {
            if (_handle == 0 || IsPaused == paused) return;
            Native.Pause(_handle, paused ? 1 : 0);
            IsPaused = paused;
        }
    }
    public void Resize(int width, int height, double scale)
    {
        lock (_sync) if (_handle != 0) Native.Resize(_handle, width, height, scale);
    }
    public void SetFpsCap(int fps)
    {
        lock (_sync) if (_handle != 0) Native.SetFpsCap(_handle, fps);
    }
    public NativeStats Stats
    {
        get
        {
            lock (_sync)
            {
                if (_handle == 0) return new NativeStats { State = 3 };
                Native.GetStats(_handle, out var stats);
                return stats;
            }
        }
    }
    public void Dispose()
    {
        IDisposable? lease;
        lock (_sync)
        {
            var handle = _handle;
            _handle = 0;
            if (handle != 0) Native.Destroy(handle);
            lease = _hostLease;
            _hostLease = null;
        }
        // The parent's lease calls Dispose on this same object. The handle has
        // already been cleared, so either parent-first or owner-first is safe.
        lease?.Dispose();
    }

    public static async Task<bool> CanPlayAsync(string path, TimeSpan timeout, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        try
        {
            return await CanDecodeWithHelperAsync(
                Path.Combine(AppContext.BaseDirectory, "daisy-mpv-preview"), path, 16, 16, deadline.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
    }

    internal static Task<bool> CanDecodeWithHelperAsync(string helper, string path, int width, int height,
        CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return false;
        if (!File.Exists(helper)) return false;
        var temporary = Directory.CreateTempSubdirectory("daisy-video-preview-");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var output = Path.Combine(temporary.FullName, "poster.bgra");
            var start = new ProcessStartInfo(helper)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            foreach (var argument in new[] { Path.GetFullPath(path), width.ToString(global::System.Globalization.CultureInfo.InvariantCulture),
                         height.ToString(global::System.Globalization.CultureInfo.InvariantCulture), output })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return false;
            var errors = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            try { await process.WaitForExitAsync(deadline.Token); }
            finally
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { /* The child exited during cancellation. */ }
                    await process.WaitForExitAsync();
                }
                await Task.WhenAll(errors, stdout);
            }
            if (process.ExitCode != 0 || !File.Exists(output)) return false;
            var expectedBytes = checked(width * height * 4);
            return new FileInfo(output).Length == expectedBytes;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
        catch (Exception exception) when (exception is IOException or global::System.ComponentModel.Win32Exception) { return false; }
        finally { temporary.Delete(recursive: true); }
    }, token);

    public static Task<WriteableBitmap?> PreviewAsync(string path, int width = 960, int height = 540,
        CancellationToken token = default) => PreviewWithHelperAsync(
            Path.Combine(AppContext.BaseDirectory, "daisy-mpv-preview"), path, width, height, token);

    internal static Task<WriteableBitmap?> PreviewWithHelperAsync(string helper, string path, int width, int height,
        CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return null;
        if (!File.Exists(helper)) return null;
        var temporary = Directory.CreateTempSubdirectory("daisy-video-preview-");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var output = Path.Combine(temporary.FullName, "poster.bgra");
            var start = new ProcessStartInfo(helper)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            foreach (var argument in new[] { Path.GetFullPath(path), width.ToString(global::System.Globalization.CultureInfo.InvariantCulture),
                         height.ToString(global::System.Globalization.CultureInfo.InvariantCulture), output })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return null;
            var errors = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            try { await process.WaitForExitAsync(deadline.Token); }
            finally
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { /* The child exited during cancellation. */ }
                    await process.WaitForExitAsync();
                }
                await Task.WhenAll(errors, stdout);
            }
            if (process.ExitCode != 0 || !File.Exists(output)) return null;
            var expectedBytes = checked(width * height * 4);
            if (new FileInfo(output).Length != expectedBytes) return null;
            var pixels = await File.ReadAllBytesAsync(output, deadline.Token);
            var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            try
            {
                using var buffer = bitmap.Lock();
                for (var row = 0; row < height; row++)
                    Marshal.Copy(pixels, row * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
                token.ThrowIfCancellationRequested();
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (Exception exception) when (exception is IOException or global::System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
        finally { temporary.Delete(recursive: true); }
    }, token);

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NativeStats
    {
        public int State, HdrActive, ColorManaged, BufferDepth;
        public long Submitted, Presented, Discarded, LastPresentMicroseconds, MaximumIntervalMicroseconds, EngineDropped;
        public double PositionSeconds;
        private fixed byte _message[256];
        public string Message { get { fixed (byte* text = _message) return Marshal.PtrToStringUTF8((nint)text) ?? ""; } }
    }
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NativeColor
    {
        public int Hdr, PeakNits, MaxCll, MaxFall;
        public fixed int Primaries[8];
        public int MinimumLuminance;
        public double StartSeconds;
        public static NativeColor FromMetadata(VideoMetadata source)
        {
            var color = new NativeColor { Hdr = source.Transfer == "smpte2084" ? 1 : source.Transfer == "arib-std-b67" ? 2 : 0,
                PeakNits = source.PeakNits, MaxCll = source.MaxCll, MaxFall = source.MaxFall, MinimumLuminance = source.MinimumLuminance };
            for (var i = 0; i < 8; i++) color.Primaries[i] = source.MasteringPrimaries[i];
            return color;
        }
    }
    private static class Native
    {
        private const string Library = "daisy_mpv_wallpaper";
        static Native() => DaisyNativeWallpaper.EnsureResolver();
        [DllImport(Library, EntryPoint = "daisy_mpv_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint Create(nint display, nint parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            int width, int height, double scale, ref NativeColor color);
        [DllImport(Library, EntryPoint = "daisy_mpv_resize", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Resize(nint handle, int width, int height, double scale);
        [DllImport(Library, EntryPoint = "daisy_mpv_pause", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Pause(nint handle, int paused);
        [DllImport(Library, EntryPoint = "daisy_mpv_set_fps_cap", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetFpsCap(nint handle, int fps);
        [DllImport(Library, EntryPoint = "daisy_mpv_stats", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void GetStats(nint handle, out NativeStats stats);
        [DllImport(Library, EntryPoint = "daisy_mpv_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Destroy(nint handle);
    }
}
