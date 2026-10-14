using System;
using System.Diagnostics;

namespace DaisyOS.Shell.Services.Diagnostics;

public class AppPerformanceMetrics
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public double CpuUsagePercentage { get; set; }
    public double WorkingSetMB { get; set; }
    public double PrivateMemoryMB { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public double GcMemoryMB { get; set; }
    public TimeSpan Uptime { get; set; }
}

public class AppPerformanceMonitor
{
    private readonly Process _process;
    private TimeSpan _lastCpuTime;
    private DateTime _lastSampleTime;
    private bool _hasLastSample;

    public AppPerformanceMonitor(Process? process = null)
    {
        _process = process ?? Process.GetCurrentProcess();
    }

    public AppPerformanceMetrics Sample()
    {
        _process.Refresh();

        var now = DateTime.UtcNow;
        var cpuTime = _process.TotalProcessorTime;
        double cpuUsage = 0.0;

        if (_hasLastSample)
        {
            var timeDelta = (now - _lastSampleTime).TotalMilliseconds;
            var cpuDelta = (cpuTime - _lastCpuTime).TotalMilliseconds;
            if (timeDelta > 0)
            {
                var processorCount = Environment.ProcessorCount;
                cpuUsage = (cpuDelta / timeDelta / Math.Max(1, processorCount)) * 100.0;
                cpuUsage = Math.Clamp(cpuUsage, 0.0, 100.0);
            }
        }
        else
        {
            _hasLastSample = true;
        }

        _lastCpuTime = cpuTime;
        _lastSampleTime = now;

        TimeSpan uptime = TimeSpan.Zero;
        try
        {
            uptime = DateTime.Now - _process.StartTime;
        }
        catch
        {
            // Fallback if process start time cannot be retrieved
        }

        int handles = 0;
        try
        {
            handles = _process.HandleCount;
        }
        catch
        {
        }

        return new AppPerformanceMetrics
        {
            ProcessId = _process.Id,
            ProcessName = _process.ProcessName,
            CpuUsagePercentage = cpuUsage,
            WorkingSetMB = _process.WorkingSet64 / (1024.0 * 1024.0),
            PrivateMemoryMB = _process.PrivateMemorySize64 / (1024.0 * 1024.0),
            ThreadCount = _process.Threads.Count,
            HandleCount = handles,
            GcMemoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
            Uptime = uptime
        };
    }
}
