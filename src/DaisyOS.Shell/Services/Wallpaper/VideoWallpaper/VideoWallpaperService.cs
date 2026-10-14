using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DaisyOS.Shell.Services.Wallpaper.VideoWallpaper;

/// <summary>
/// Managed wrapper around libdaisy_live_wallpaper.so handle.
/// Provides high-performance 4K live video wallpaper decoding and DMA-BUF frame acquisition.
/// </summary>
public sealed partial class VideoWallpaperService : IDisposable
{
    private readonly object _sync = new();
    private nint _handle;
    private bool _disposed;

    private bool _isPaused;

    public bool IsLoaded
    {
        get
        {
            lock (_sync) return _handle != nint.Zero && !_disposed;
        }
    }
    public bool IsPaused => _isPaused;

    public VideoWallpaperService()
    {
        try
        {
            _handle = DaisyNativeWallpaper.Create();
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            // The desktop remains fully usable without the optional native
            // decoder. Callers treat a zero handle as an unavailable video.
            _handle = nint.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FrameCallback();
    private FrameCallback? _frameCallback;
    public event EventHandler? FrameAvailable;
    public bool HasFrameNotifications { get; private set; }
    public void EnableFrameNotifications()
    {
        lock (_sync)
        {
            if (_disposed || _handle == nint.Zero) return;
            _frameCallback = () => { try { FrameAvailable?.Invoke(this, EventArgs.Empty); } catch { } };
            try
            {
                DaisyNativeWallpaper.SetFrameCallback(_handle, Marshal.GetFunctionPointerForDelegate(_frameCallback));
                HasFrameNotifications = true;
            }
            catch (EntryPointNotFoundException) { HasFrameNotifications = false; }
        }
    }

    public bool Load(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) return false;

        lock (_sync)
        {
            if (_disposed || _handle == nint.Zero) return false;
            try
            {
                _isPaused = true;
                return DaisyNativeWallpaper.Load(_handle, videoPath) == 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    public void Play()
    {
        lock (_sync)
        {
            if (_disposed || _handle == nint.Zero) return;
            try
            {
                DaisyNativeWallpaper.Play(_handle);
                _isPaused = false;
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException) { }
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_disposed || _handle == nint.Zero) return;
            try
            {
                DaisyNativeWallpaper.Pause(_handle);
                _isPaused = true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException) { }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_disposed || _handle == nint.Zero) return;
            try { DaisyNativeWallpaper.Stop(_handle); }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException) { }
        }
    }

    public void SetFpsCap(int fps)
    {
        lock (_sync)
        {
            if (_disposed || _handle == nint.Zero) return;
            try { DaisyNativeWallpaper.SetFpsCap(_handle, Math.Clamp(fps, 1, 60)); }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException) { }
        }
    }

    public void SetOutputSize(int width, int height)
    {
        lock (_sync)
        {
            if (_disposed || _handle == nint.Zero) return;
            try { DaisyNativeWallpaper.SetOutputSize(_handle, Math.Max(0, width), Math.Max(0, height)); }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException) { }
        }
    }

    /// <summary>
    /// Verifies that a selected video can actually produce a frame before the
    /// preference is changed. It runs off the UI thread and always releases the
    /// native frame it probes.
    /// </summary>
    public static async Task<bool> CanPlayAsync(string videoPath, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) return false;

        if (MpvWallpaperSession.Enabled)
        {
            return await MpvWallpaperSession.CanPlayAsync(videoPath, timeout, cancellationToken);
        }

        return await Task.Run(() =>
        {
            using var service = new VideoWallpaperService();
            if (!service.Load(videoPath)) return false;

            service.SetOutputSize(1, 1);
            service.SetFpsCap(1);
            service.Play();
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (service.TryGetFrame(out var frame))
                {
                    ReleaseFrame(ref frame);
                    return true;
                }

                Thread.Sleep(10);
            }

            return false;
        }, cancellationToken).ConfigureAwait(false);
    }

    public static unsafe void ReleaseFrame(ref DaisyNativeWallpaper.NativeVideoFrame frame)
    {
        ulong pointer = (ulong)frame.Stride0 | ((ulong)frame.Stride1 << 32);
        if (frame.Fd0 == -2 && pointer != 0)
        {
            NativeMemory.Free((void*)pointer);
        }

        CloseFileDescriptor(frame.Fd1);
        CloseFileDescriptor(frame.Fd2);
        CloseFileDescriptor(frame.Fd3);
        CloseFileDescriptor(frame.AcquireFence);
        frame = default;
    }

    private static void CloseFileDescriptor(int fileDescriptor)
    {
        if (fileDescriptor < 0) return;
        try { close(fileDescriptor); } catch { }
    }

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int close(int fileDescriptor);

    public bool TryGetFrame(out DaisyNativeWallpaper.NativeVideoFrame frame)
    {
        frame = default;
        lock (_sync)
        {
            if (_disposed || _handle == nint.Zero) return false;
            try { return DaisyNativeWallpaper.TryGetFrame(_handle, ref frame) != 0; }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException) { return false; }
        }
    }

    public bool GetStats(out DaisyNativeWallpaper.NativeWallpaperStats stats)
    {
        stats = default;
        lock (_sync)
        {
            if (_disposed || _handle == nint.Zero) return false;
            try
            {
                DaisyNativeWallpaper.GetStats(_handle, ref stats);
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException) { return false; }
        }
    }

    public void PrintStats()
    {
        lock (_sync)
        {
            if (_disposed || _handle == nint.Zero) return;
            try { DaisyNativeWallpaper.PrintStats(_handle); }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException) { }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle == nint.Zero) return;
            try
            {
                DaisyNativeWallpaper.Stop(_handle);
                DaisyNativeWallpaper.Destroy(_handle);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException) { }
            finally { _handle = nint.Zero; }
        }
    }
}
