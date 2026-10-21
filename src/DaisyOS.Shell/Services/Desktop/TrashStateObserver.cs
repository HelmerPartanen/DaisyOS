using Avalonia.Threading;

namespace DaisyOS.Shell.Services.Desktop;

/// <summary>Watches only the Trash directory, with a slow recovery check for lost events.</summary>
internal sealed class TrashStateObserver : IDisposable
{
    private readonly string _path;
    private readonly Action<bool> _publish;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _recovery = new() { Interval = TimeSpan.FromSeconds(30) };
    private FileSystemWatcher? _watcher;
    private string? _watchedPath;
    private int _queued;
    private int _revision;
    private bool _refreshing;
    private bool _disposed;
    private bool? _lastState;

    public TrashStateObserver(string path, Action<bool> publish)
    {
        _path = Path.GetFullPath(path);
        _publish = publish;
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RefreshAsync(); };
        _recovery.Tick += (_, _) => QueueRefresh();
        _recovery.Start();
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _queued, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _queued, 0);
            if (_disposed) return;
            _revision++;
            _debounce.Stop();
            _debounce.Start();
        });
    }

    private void EnsureWatcher()
    {
        // If Trash has never been used, observe its nearest existing parent.
        // Never create directories merely to monitor them or watch recursively.
        var directory = _path;
        while (!Directory.Exists(directory))
        {
            directory = Path.GetDirectoryName(directory);
            if (directory is null) return;
        }
        if (_watchedPath == directory && _watcher is not null) return;
        _watcher?.Dispose();
        _watcher = null;
        _watchedPath = null;
        try
        {
            var watcher = new FileSystemWatcher(directory)
            {
                Filter = directory == _path ? "*" : Path.GetRelativePath(directory, _path).Split(Path.DirectorySeparatorChar)[0],
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = false
            };
            _watcher = watcher;
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Renamed += Changed;
            watcher.Error += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                _watchedPath = null;
                QueueRefresh();
            });
            watcher.EnableRaisingEvents = true;
            _watchedPath = directory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _watcher?.Dispose();
            _watcher = null;
            // Retry on the next recovery tick, including after watcher exhaustion.
        }
    }

    private void Changed(object sender, FileSystemEventArgs args) => QueueRefresh();

    private async Task RefreshAsync()
    {
        if (_disposed || _refreshing) return;
        _refreshing = true;
        var revision = _revision;
        try
        {
            EnsureWatcher();
            var populated = await Task.Run<bool?>(() =>
            {
                try { return Directory.Exists(_path) && Directory.EnumerateFileSystemEntries(_path).Any(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
            });
            if (!_disposed && revision == _revision && populated is { } state && _lastState != state)
            {
                _lastState = state;
                _publish(state);
            }
        }
        finally
        {
            _refreshing = false;
            if (!_disposed && revision != _revision) { _debounce.Stop(); _debounce.Start(); }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher?.Dispose();
        _watcher = null;
        _debounce.Stop();
        _recovery.Stop();
    }
}
