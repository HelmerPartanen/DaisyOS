using System.Runtime;
using System.Runtime.InteropServices;
using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.Services.Wallpaper;

namespace DaisyOS.Shell.Services.Diagnostics;

/// <summary>
/// Returns memory the shell no longer needs to the operating system.
///
/// The shell keeps managed heap and native (Skia/HarfBuzz/glibc) allocations
/// around after windows, menus and wallpapers close because neither the .NET
/// GC nor glibc's allocator give pages back eagerly. When nobody can see the
/// shell — a game or fullscreen app covers the desktop, the screen is locked
/// or turned off — that retained memory is pure waste, so this service
/// compacts the managed heap and asks glibc to release free arenas.
/// </summary>
internal sealed partial class ShellMemoryTrimmer : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MinimumAggressiveInterval = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PeriodicInterval = TimeSpan.FromMinutes(5);

    [LibraryImport("libc", EntryPoint = "malloc_trim")]
    private static partial int MallocTrim(nuint pad);

    private readonly object _gate = new();
    private readonly Timer _debounce;
    private readonly Timer _periodic;
    private readonly Func<bool> _isShellHidden;
    private DateTime _lastAggressiveUtc = DateTime.MinValue;
    private int _running;
    private bool _mallocTrimUnavailable;
    private bool _disposed;

    private readonly Action? _reclaimHiddenViews;

    /// <param name="isShellHidden">True while no shell surface is visible to the user.</param>
    /// <param name="reclaimHiddenViews">Synchronously releases caches of hidden views; called off the UI thread.</param>
    public ShellMemoryTrimmer(Func<bool> isShellHidden, Action? reclaimHiddenViews = null)
    {
        _isShellHidden = isShellHidden;
        _reclaimHiddenViews = reclaimHiddenViews;
        _debounce = new Timer(_ => Run(aggressive: true), null, Timeout.Infinite, Timeout.Infinite);
        _periodic = new Timer(_ => OnPeriodic(), null, PeriodicInterval, PeriodicInterval);
    }

    /// <summary>Requests a full trim shortly; repeated requests coalesce.</summary>
    public void RequestTrim()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnPeriodic()
    {
        // While hidden (gaming, locked, screen off) keep the footprint small;
        // when visible, only intervene if the whole system is short of memory.
        if (_isShellHidden()) Run(aggressive: true);
        else if (IsUnderPressure()) Run(aggressive: false);
    }

    private void Run(bool aggressive)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (aggressive && DateTime.UtcNow - _lastAggressiveUtc < MinimumAggressiveInterval)
                    aggressive = false;
                if (aggressive) _lastAggressiveUtc = DateTime.UtcNow;
            }

            if (aggressive)
            {
                // Drop decoded images nobody is rendering before compacting,
                // otherwise the collection would keep them alive.
                try { _reclaimHiddenViews?.Invoke(); }
                catch (Exception ex) { Console.Error.WriteLine($"[daisy-memory] View reclaim failed: {ex.Message}"); }
                ThumbnailCache.TrimAllUnused();
                WallpaperSourceCache.TrimUnused();

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                // Aggressive mode also decommits free GC regions back to the OS.
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
            }
            else
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
            }

            TrimNativeHeap();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[daisy-memory] Trim failed: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private void TrimNativeHeap()
    {
        if (_mallocTrimUnavailable || !OperatingSystem.IsLinux()) return;
        try { MallocTrim(0); }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // Non-glibc libc (e.g. musl) — nothing to trim.
            _mallocTrimUnavailable = true;
        }
    }

    internal static bool IsUnderPressure()
    {
        try
        {
            long total = 0, available = 0;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal)) total = ParseKiB(line);
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal)) { available = ParseKiB(line); break; }
            }
            return IsUnderPressure(total, available);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return false;
        }
    }

    internal static bool IsUnderPressure(long totalKiB, long availableKiB) =>
        totalKiB > 0 && availableKiB < Math.Clamp(totalKiB / 8, 512L * 1024, 2048L * 1024);

    private static long ParseKiB(string line) =>
        long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _debounce.Dispose();
        _periodic.Dispose();
    }
}
