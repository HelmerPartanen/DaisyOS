using Avalonia.Threading;

namespace DaisyOS.Shell.Apps.Files.Services;

public sealed class VisualLoadScheduler : IDisposable
{
    private readonly DirectorySession _session;
    private readonly Func<bool> _isCurrent;
    private readonly IFileUiDispatcher _dispatcher;
    private readonly Dictionary<FileEntryViewModel, CancellationTokenSource> _wanted = [];
    private readonly Dictionary<FileEntryViewModel, CancellationTokenSource> _running = [];
    private readonly HashSet<FileEntryViewModel> _loaded = [];
    private readonly PriorityQueue<FileEntryViewModel, int> _queue = new();
    private readonly int _workers;
    private int _active;
    private bool _disposed;
    internal Func<DaisyOS.Shell.Apps.Files.Models.FileEntrySnapshot, int, CancellationToken, Task<VisualLease>> Resolver { get; set; } = FileVisualResolver.ResolveLeaseAsync;
    public int PendingCount => _queue.Count;
    public int ActiveCount => _active;
    public int WorkerLimit => _workers;
    internal VisualLoadScheduler(DirectorySession session, Func<bool> isCurrent, IFileUiDispatcher dispatcher)
    {
        _session = session;
        _isCurrent = isCurrent;
        _dispatcher = dispatcher;
        _workers = FilesIoScheduler.VisualWorkerLimit;
    }
    // Called once per realized viewport update. Rebuilding this small queue
    // drops stale requests immediately instead of accumulating a scroll backlog.
    public void SetViewport(IReadOnlyList<FileEntryViewModel> visible, IReadOnlyList<FileEntryViewModel>? nearby = null)
    {
        if (_disposed) return;
        var wanted = new HashSet<FileEntryViewModel>(visible);
        if (nearby != null) wanted.UnionWith(nearby);
        foreach (var entry in _wanted.Keys.Where(e => !wanted.Contains(e)).ToArray())
        {
            var request = _wanted[entry];
            request.Cancel();
            if (!_running.ContainsKey(entry)) request.Dispose();
            _wanted.Remove(entry);
            _loaded.Remove(entry);
            entry.ClearVisual();
        }
        _queue.Clear();
        Queue(visible, 0);
        if (nearby != null) Queue(nearby, 1);
        Pump();
    }
    private void Queue(IReadOnlyList<FileEntryViewModel> entries, int priority)
    {
        foreach (var entry in entries)
        {
            if (!FileVisualResolver.CanResolve(entry.Snapshot)) continue;
            if (!_wanted.ContainsKey(entry)) _wanted[entry] = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
            if (!_loaded.Contains(entry) && !_running.ContainsKey(entry)) _queue.Enqueue(entry, priority);
        }
    }
    public void Invalidate(FileEntryViewModel entry)
    {
        if (_wanted.Remove(entry, out var request))
        {
            request.Cancel();
            if (!_running.ContainsKey(entry)) request.Dispose();
        }
        _loaded.Remove(entry);
        entry.ClearVisual();
    }
    private void Pump()
    {
        while (!_disposed && _active < _workers && _queue.TryDequeue(out var entry, out _))
        {
            if (!_wanted.TryGetValue(entry, out var request) || request.IsCancellationRequested || _running.ContainsKey(entry) || _loaded.Contains(entry)) continue;
            _running.Add(entry, request);
            _active++;
            _session.Performance.Count("Visual jobs scheduled");
            if (FileVisualResolver.IsImage(entry.Extension)) _session.Performance.Count("Thumbnails scheduled");
            _ = LoadAsync(entry, request);
        }
    }
    private async Task LoadAsync(FileEntryViewModel entry, CancellationTokenSource request)
    {
        var snapshot = entry.Snapshot;
        VisualLease? lease = null;
        try
        {
            using (_session.Performance.Measure("Thumbnail/icon loading"))
                lease = await FilesIoScheduler.LoadVisualAsync(() => Resolver(snapshot, 96, request.Token), request.Token);
            await _dispatcher.InvokeAsync(() =>
            {
                if (!_disposed && _isCurrent() && !request.IsCancellationRequested && snapshot == entry.Snapshot &&
                    _wanted.TryGetValue(entry, out var wanted) && ReferenceEquals(wanted, request))
                {
                    entry.ApplyVisualLease(lease!);
                    lease = null;
                    _loaded.Add(entry);
                }
            });
        }
        catch (OperationCanceledException) { }
        catch { /* Generic icon remains available. */ }
        finally
        {
            lease?.Dispose();
            await _dispatcher.InvokeAsync(() =>
            {
                _running.Remove(entry);
                _active--;
                if (!_wanted.TryGetValue(entry, out var wanted) || !ReferenceEquals(wanted, request))
                {
                    request.Dispose();
                    if (wanted != null && !wanted.IsCancellationRequested && !_loaded.Contains(entry)) _queue.Enqueue(entry, 0);
                }
                Pump();
            });
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var pair in _wanted)
        {
            pair.Value.Cancel();
            if (!_running.ContainsKey(pair.Key)) pair.Value.Dispose();
            pair.Key.ClearVisual();
        }
        _wanted.Clear();
        _queue.Clear();
        _loaded.Clear();
    }
}
