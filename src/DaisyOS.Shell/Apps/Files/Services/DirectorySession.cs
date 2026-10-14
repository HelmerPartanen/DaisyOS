using DaisyOS.Shell.Apps.Files.Models;

namespace DaisyOS.Shell.Apps.Files.Services;

public sealed class DirectorySession : IDisposable
{
    private bool _disposed;
    public string Path { get; }
    public long Generation { get; }
    public CancellationTokenSource Lifetime { get; } = new();
    public CancellationToken Token { get; }
    public DirectoryIndex Index { get; internal set; } = new();
    public DirectoryWatcher Watcher { get; }
    public VisualLoadScheduler Visuals { get; }
    public FilesPerformance Performance { get; }
    public bool IsReady { get; internal set; }
    public bool ProjectionReady { get; internal set; }
    public bool IsDisposed => _disposed;
    internal List<DirectoryDelta> BufferedMutations { get; } = [];
    internal SemaphoreSlim WatcherGate { get; } = new(1, 1);
    internal string? PendingRenamePath;
    internal bool ReconciliationRunning;
    internal bool ReconciliationRequested;
    public DirectorySession(string path, long generation, Func<bool> isCurrent, Action<IReadOnlyList<DirectoryEvent>> watcherEvents,
        IFileUiDispatcher? dispatcher = null, bool watchFiles = true)
    {
        Token = Lifetime.Token;
        Path = path;
        Generation = generation;
        Performance = new FilesPerformance(path);
        Visuals = new VisualLoadScheduler(this, isCurrent, dispatcher ?? new AvaloniaFileUiDispatcher());
        Watcher = new DirectoryWatcher(path, watcherEvents, watchFiles);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Lifetime.Cancel();
        Watcher.Dispose();
        Visuals.Dispose();
        BufferedMutations.Clear();
        Lifetime.Dispose();
        // WatcherGate remains valid until any canceled waiters finish.
    }
}
