using System.Diagnostics;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.Views.Components.Taskbar;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Services;

/// <summary>Bounded launch feedback correlated with new compositor windows, never presented frames.</summary>
internal sealed class LaunchFeedbackController : IDisposable
{
    private readonly TaskbarView _taskbar;
    private readonly ICompositorWindowService _windows;
    private readonly Dictionary<string, Pending> _pending = new();
    private readonly HashSet<string> _claimedWindows = new();
    private readonly DispatcherTimer _expiry = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _disposed;
    private sealed record Pending(string App, long Started, HashSet<string> ExistingWindows, BusyCursor Cursor);
    public LaunchFeedbackController(TaskbarView taskbar, ICompositorWindowService windows)
    {
        _taskbar = taskbar; _windows = windows;
        DaisyLaunchClient.Shared.StateChanged += OnState;
        _windows.WindowsChanged += OnWindows;
        _expiry.Tick += (_, _) => Reconcile();
    }
    public string BeginLocalLaunch(string app)
    {
        var id = Guid.NewGuid().ToString("N");
        _pending[id] = new(app, Stopwatch.GetTimestamp(), _windows.Windows.Select(w => w.Id).ToHashSet(), BusyCursor.Begin());
        _expiry.Start();
        return id;
    }
    public void LocalLaunchFailed(string id) => Clear(id);
    private static string Normalize(string id) => Path.GetFileName(id).EndsWith(".desktop", StringComparison.Ordinal) ? Path.GetFileName(id)[..^8] : Path.GetFileName(id);
    private void OnState(string id, string app, string state) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed || Environment.GetEnvironmentVariable("DAISYOS_LAUNCH_FEEDBACK") == "0") return;
        if (state == "Requested")
        {
            if (_pending.ContainsKey(id)) Clear(id);
            _pending[id] = new(app, Stopwatch.GetTimestamp(), _windows.Windows.Select(w => w.Id).ToHashSet(), BusyCursor.Begin());
            _taskbar.SetLaunching(app, true); _expiry.Start();
        }
        else if (state != "Accepted") Clear(id);
    }, DispatcherPriority.Input);
    private void OnWindows(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Reconcile);
    private void Reconcile()
    {
        if (_disposed) return;
        _claimedWindows.IntersectWith(_windows.Windows.Select(w => w.Id));
        foreach (var (id, pending) in _pending.ToArray())
        {
            var window = _windows.Windows.FirstOrDefault(w => (!pending.ExistingWindows.Contains(w.Id) || (pending.App.StartsWith("org.daisyos.", StringComparison.Ordinal) && w.IsActive)) && (pending.App.StartsWith("org.daisyos.", StringComparison.Ordinal) || !_claimedWindows.Contains(w.Id)) &&
                (Normalize(w.DesktopFileId ?? "") == Normalize(pending.App) || Normalize(w.AppId) == Normalize(pending.App)));
            if (window is not null)
            {
                _claimedWindows.Add(window.Id);
                if (!pending.App.StartsWith("org.daisyos.", StringComparison.Ordinal))
                    _ = DaisyLaunchClient.Shared.AssociateAsync(id, window.Id);
                Clear(id);
            }
            else if (Stopwatch.GetElapsedTime(pending.Started).TotalSeconds >= 15) Clear(id);
        }
        if (_pending.Count == 0) _expiry.Stop();
    }
    private void Clear(string id)
    {
        if (_pending.Remove(id, out var pending))
        {
            pending.Cursor.Dispose();
            if (!_pending.Values.Any(p => p.App == pending.App)) _taskbar.SetLaunching(pending.App, false);
        }
    }
    public void Dispose()
    {
        _disposed = true; _expiry.Stop(); DaisyLaunchClient.Shared.StateChanged -= OnState;
        _windows.WindowsChanged -= OnWindows;
        foreach (var pending in _pending.Values) { pending.Cursor.Dispose(); _taskbar.SetLaunching(pending.App, false); }
        _pending.Clear();
    }
}
