using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DaisyOS.Shell.Services.Idle;

/// <summary>
/// Managed wrapper around libdaisy_idle.so which uses Wayland ext-idle-notify-v1.
/// </summary>
public sealed class DaisyIdleMonitorNative : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeCallback(nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int IsAvailableDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint StartDelegate(uint timeoutMs, NativeCallback onIdle, NativeCallback onResumed, nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StopDelegate(nint handle);

    private readonly nint _libHandle;
    private readonly IsAvailableDelegate? _isAvailable;
    private readonly StartDelegate? _start;
    private readonly StopDelegate? _stop;

    private nint _activeHandle;
    private NativeCallback? _pinnedIdleCallback;
    private NativeCallback? _pinnedResumedCallback;

    public bool IsSupported { get; }

    public DaisyIdleMonitorNative()
    {
        _libHandle = LoadLibrary();
        if (_libHandle != nint.Zero)
        {
            if (NativeLibrary.TryGetExport(_libHandle, "daisy_idle_monitor_is_available", out var isAvailPtr))
            {
                _isAvailable = Marshal.GetDelegateForFunctionPointer<IsAvailableDelegate>(isAvailPtr);
            }
            if (NativeLibrary.TryGetExport(_libHandle, "daisy_idle_monitor_start", out var startPtr))
            {
                _start = Marshal.GetDelegateForFunctionPointer<StartDelegate>(startPtr);
            }
            if (NativeLibrary.TryGetExport(_libHandle, "daisy_idle_monitor_stop", out var stopPtr))
            {
                _stop = Marshal.GetDelegateForFunctionPointer<StopDelegate>(stopPtr);
            }

            if (_isAvailable is not null && _start is not null && _stop is not null)
            {
                try
                {
                    IsSupported = _isAvailable() == 1;
                }
                catch
                {
                    IsSupported = false;
                }
            }
        }
    }

    private static nint LoadLibrary()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "libdaisy_idle.so"),
            Path.Combine(baseDir, "runtimes/linux-x64/native/libdaisy_idle.so"),
            Path.GetFullPath(Path.Combine(baseDir, "../../../src/native/daisy_idle_monitor/build/libdaisy_idle.so")),
            Path.GetFullPath("src/native/daisy_idle_monitor/build/libdaisy_idle.so"),
            "/usr/local/lib/libdaisy_idle.so"
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
            {
                return handle;
            }
        }

        if (NativeLibrary.TryLoad("daisy_idle", typeof(DaisyIdleMonitorNative).Assembly, null, out var defaultHandle))
        {
            return defaultHandle;
        }

        return nint.Zero;
    }

    public bool Start(uint timeoutMs, Action onIdle, Action onResumed)
    {
        if (_start is null || !IsSupported)
        {
            return false;
        }

        Stop();

        _pinnedIdleCallback = _ => onIdle();
        _pinnedResumedCallback = _ => onResumed();

        _activeHandle = _start(timeoutMs, _pinnedIdleCallback, _pinnedResumedCallback, nint.Zero);
        return _activeHandle != nint.Zero;
    }

    public void Stop()
    {
        if (_activeHandle != nint.Zero && _stop is not null)
        {
            var handle = _activeHandle;
            _activeHandle = nint.Zero;
            try
            {
                _stop(handle);
            }
            catch { }
        }

        _pinnedIdleCallback = null;
        _pinnedResumedCallback = null;
    }

    public void Dispose()
    {
        Stop();
        if (_libHandle != nint.Zero)
        {
            NativeLibrary.Free(_libHandle);
        }
    }
}
