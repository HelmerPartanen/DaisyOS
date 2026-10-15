using System.Runtime.InteropServices;

namespace DaisyOS.System.Diagnostics;

/// <summary>Optional read-only NVML sampling without a subprocess per sample.</summary>
public sealed class NvidiaGpuMetrics : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<int> _initialize;
    private readonly Func<(int Result, uint Count)> _count;
    private readonly Func<uint, (int Result, nint Device)> _device;
    private readonly Func<nint, (int Result, uint Usage)> _usage;
    private readonly Action _shutdown;
    private readonly Func<long> _clock;
    private bool _initialized;
    private bool _disposed;
    private long _retryAfter;

    public NvidiaGpuMetrics() : this(Native.Init,
        () => { var result = Native.Count(out var count); return (result, count); },
        index => { var result = Native.Device(index, out var device); return (result, device); },
        device => { var result = Native.Usage(device, out var usage); return (result, usage.Gpu); },
        () => Native.Shutdown(), () => Environment.TickCount64) { }

    public NvidiaGpuMetrics(Func<int> initialize, Func<(int Result, uint Count)> count,
        Func<uint, (int Result, nint Device)> device, Func<nint, (int Result, uint Usage)> usage,
        Action shutdown, Func<long> clock)
    {
        _initialize = initialize; _count = count; _device = device;
        _usage = usage; _shutdown = shutdown; _clock = clock;
    }

    public double? ReadUsage()
    {
        lock (_gate)
        {
            if (_disposed || _clock() < _retryAfter) return null;
            try
            {
                if (!_initialized)
                {
                    if (_initialize() != 0) { BackOff(); return null; }
                    _initialized = true;
                }
                var (result, count) = _count();
                if (result != 0) { Reset(); return null; }
                double? maximum = null;
                for (uint index = 0; index < Math.Min(count, 64u); index++)
                {
                    var handle = _device(index);
                    if (handle.Result != 0) continue;
                    var sample = _usage(handle.Device);
                    if (sample.Result == 0 && sample.Usage <= 100)
                        maximum = Math.Max(maximum ?? 0, sample.Usage);
                }
                if (maximum is null) Reset();
                return maximum;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            { Reset(); return null; }
        }
    }

    private void BackOff() => _retryAfter = _clock() + 30_000;
    private void Reset()
    {
        if (_initialized)
        {
            _initialized = false;
            try { _shutdown(); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { }
        }
        BackOff();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Reset();
        }
    }

    private static class Native
    {
        private const string Library = "libnvidia-ml.so.1";
        [StructLayout(LayoutKind.Sequential)]
        internal struct Utilization { public uint Gpu; public uint Memory; }
        [DllImport(Library, EntryPoint = "nvmlInit_v2", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Init();
        [DllImport(Library, EntryPoint = "nvmlShutdown", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Shutdown();
        [DllImport(Library, EntryPoint = "nvmlDeviceGetCount_v2", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Count(out uint count);
        [DllImport(Library, EntryPoint = "nvmlDeviceGetHandleByIndex_v2", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Device(uint index, out nint device);
        [DllImport(Library, EntryPoint = "nvmlDeviceGetUtilizationRates", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Usage(nint device, out Utilization utilization);
    }
}
