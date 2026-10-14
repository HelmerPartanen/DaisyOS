using Avalonia.Controls;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Diagnostics;

namespace DaisyOS.Shell.Services.Windows;

/// <summary>Retains recently used system app windows in suspended state for instant reopening.</summary>
internal sealed class LightweightWindowCache : IDisposable
{
    private readonly LinkedList<Window> _hidden = [];
    private readonly HashSet<Window> _evicting = [];
    private readonly DispatcherTimer _pressure = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly int _maxRetainedWindows;
    private readonly bool _retainWindows;

    public LightweightWindowCache(bool retainWindows = true)
    {
        _retainWindows = retainWindows;
        _maxRetainedWindows = GetAdaptiveRetainedWindowCount();
        _pressure.Tick += (_, _) => { if (RecentAppPrefetcher.MemoryPressure()) Clear(); };
    }

    private static int GetAdaptiveRetainedWindowCount()
    {
        try
        {
            var memoryInfo = GC.GetGCMemoryInfo();
            long totalPhysicalBytes = memoryInfo.TotalAvailableMemoryBytes;
            if (totalPhysicalBytes > 0)
            {
                if (totalPhysicalBytes <= 4L * 1024 * 1024 * 1024) return 3;
                if (totalPhysicalBytes <= 8L * 1024 * 1024 * 1024) return 5;
            }
        }
        catch
        {
            // Fallback
        }
        return 6;
    }

    public void Track(Window window, Action suspend, Action resume, Action unregister)
    {
        window.Closing += (_, e) =>
        {
            // A hidden xdg_toplevel is not reliably remappable after KWin has
            // completed its close transaction. In a native Wayland session,
            // let the window really close and recreate it on the next launch.
            if (!_retainWindows || _evicting.Contains(window) ||
                e.CloseReason != WindowCloseReason.WindowClosing ||
                RecentAppPrefetcher.MemoryPressure()) return;
            suspend();
            unregister();
            window.Hide();
            _hidden.Remove(window);
            _hidden.AddFirst(window);
            e.Cancel = true;
            while (_hidden.Count > _maxRetainedWindows) Evict(_hidden.Last!.Value);
            _pressure.Start();
        };
        window.PropertyChanged += (_, e) =>
        {
            if (!_retainWindows) return;
            if (e.Property != Window.IsVisibleProperty || !window.IsVisible) return;
            _hidden.Remove(window);
            if (_hidden.Count == 0) _pressure.Stop();
            resume();
        };
        window.Closed += (_, _) => _hidden.Remove(window);
    }
    private void Evict(Window window)
    {
        _hidden.Remove(window);
        _evicting.Add(window);
        try { window.Close(); } finally { _evicting.Remove(window); }
    }
    private void Clear() { foreach (var window in _hidden.ToArray()) Evict(window); _pressure.Stop(); }
    public void Dispose() => Clear();
}
