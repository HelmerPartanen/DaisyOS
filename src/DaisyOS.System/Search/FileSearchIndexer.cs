using System.Threading.Channels;
using DaisyOS.Core.Search;

namespace DaisyOS.System.Search;

public sealed class FileSearchIndexer : ISearchIndexer, IAsyncDisposable
{
    private sealed record Change(string Path, bool Subtree, string? OldPath = null);
    private readonly SearchIndexOptions _options;
    private readonly FileSearchDatabase _database;
    private readonly Channel<Change> _events = Channel.CreateBounded<Change>(new BoundedChannelOptions(8192)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _lifecycle = new();
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private int _reconcile = 1;
    private int _paused;
    private int _watcherFailure;
    private bool _incomplete;
    private bool _watchUnavailable;
    private long _errors;
    private long _count;
    private long _scanned;
    private string _status = "Initializing";
    private long _lastReconciliationTicks;
    private long _databaseBytes;
    public event EventHandler? Changed;
    public FileIndexDiagnostic Diagnostics => new(Volatile.Read(ref _status), Interlocked.Read(ref _count),
        Interlocked.Read(ref _databaseBytes),
        Interlocked.Read(ref _lastReconciliationTicks) is var ticks && ticks != 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null, _watchers.Count, Interlocked.Read(ref _errors), Volatile.Read(ref _paused) != 0, Interlocked.Read(ref _scanned));
    public FileSearchIndexer(SearchIndexOptions options, FileSearchDatabase database) { _options = options; _database = database; }
    public bool IsAllowed(string path)
    {
        path = Path.GetFullPath(path);
        return _options.Roots.Any(r => Within(path, r)) && !_options.Exclusions.Any(e => Within(path, e)) &&
            !path.Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith('.') && part is not "." and not "..");
    }
    private static bool Within(string path, string root) => path == Path.TrimEndingDirectorySeparator(root) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycle)
        {
            if (_worker is not null) return Task.CompletedTask;
            if (SearchFileAccess.IsRoot) throw new InvalidOperationException("File indexing must run as an ordinary user.");
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _worker = Task.Run(() => RunAsync(_lifetime.Token), CancellationToken.None);
        }
        return Task.CompletedTask;
    }
    public void SetPaused(bool paused)
    {
        Interlocked.Exchange(ref _paused, paused ? 1 : 0);
        if (!paused) Interlocked.Exchange(ref _reconcile, 1);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public Task ReindexAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _reconcile, 1);
        return Task.CompletedTask;
    }
    private void Enqueue(Change change)
    {
        if (!_events.Writer.TryWrite(change))
        { Interlocked.Increment(ref _errors); Interlocked.Exchange(ref _reconcile, 1); }
    }
    internal void ReportWatcherFailure()
    {
        Interlocked.Increment(ref _errors);
        Interlocked.Exchange(ref _watcherFailure, 1);
        Interlocked.Exchange(ref _reconcile, 1);
    }
    private void EnsureWatchers()
    {
        _watchUnavailable = false;
        foreach (var root in _options.Roots)
        {
            if (_watchers.Any(w => w.Path == root) || !Directory.Exists(root)) continue;
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true, InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes
                };
                watcher.Created += (_, e) => Enqueue(new(e.FullPath, true));
                watcher.Changed += (_, e) => Enqueue(new(e.FullPath, false));
                watcher.Deleted += (_, e) => Enqueue(new(e.FullPath, true));
                watcher.Renamed += (_, e) => Enqueue(new(e.FullPath, true, e.OldFullPath));
                watcher.Error += (_, _) => ReportWatcherFailure();
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { _watchUnavailable = true; Interlocked.Increment(ref _errors); }
        }
    }
    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            _database.Initialize();
            _count = _database.Count;
            var nextReconciliation = DateTimeOffset.MinValue;
            while (!token.IsCancellationRequested)
            {
                var reconciled = false;
                if (Volatile.Read(ref _paused) != 0)
                {
                    Volatile.Write(ref _status, "Paused");
                    await Task.Delay(200, token).ConfigureAwait(false);
                    continue;
                }
                if (Interlocked.Exchange(ref _watcherFailure, 0) != 0)
                {
                    foreach (var watcher in _watchers) watcher.Dispose();
                    _watchers.Clear();
                }
                EnsureWatchers();
                if (Interlocked.Exchange(ref _reconcile, 0) != 0 || DateTimeOffset.UtcNow >= nextReconciliation)
                {
                    Volatile.Write(ref _status, "Indexing");
                    Changed?.Invoke(this, EventArgs.Empty);
                    _incomplete = false;
                    Interlocked.Exchange(ref _scanned, 0);
                    foreach (var root in _options.Roots)
                    {
                        var scan = Guid.NewGuid().ToString("N");
                        if (await ScanAsync(root, root, scan, token).ConfigureAwait(false)) _database.FinishScan(root, scan);
                        else _incomplete = true;
                    }
                    Interlocked.Exchange(ref _count, _database.Count);
                    Interlocked.Exchange(ref _lastReconciliationTicks, DateTimeOffset.UtcNow.Ticks);
                    RefreshDatabaseSize();
                    reconciled = true;
                    nextReconciliation = DateTimeOffset.UtcNow + _options.ReconcileInterval;
                }
                await Task.Delay(150, token).ConfigureAwait(false);
                if (Volatile.Read(ref _paused) != 0) continue;
                var changes = new Dictionary<string, Change>(StringComparer.Ordinal);
                while (changes.Count < 8192 && _events.Reader.TryRead(out var change))
                {
                    if (change.OldPath is not null) _database.RemoveSubtree(change.OldPath);
                    if (changes.TryGetValue(change.Path, out var old)) change = change with { Subtree = old.Subtree || change.Subtree };
                    changes[change.Path] = change;
                }
                foreach (var change in changes.Values.OrderBy(e => e.Path.Length))
                {
                    token.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref _paused) != 0) break;
                    if (!IsAllowed(change.Path)) { _database.RemoveSubtree(change.Path); continue; }
                    var root = _options.Roots.Where(r => Within(change.Path, r)).OrderByDescending(r => r.Length).First();
                    var metadata = SearchFileAccess.Read(change.Path);
                    if (metadata is null) _database.RemoveSubtree(change.Path);
                    else if (metadata.IsDirectory && change.Subtree)
                    {
                        _database.RemoveSubtree(change.Path);
                        await ScanAsync(change.Path, root, Guid.NewGuid().ToString("N"), token).ConfigureAwait(false);
                    }
                    else _database.Upsert([metadata], root, Guid.NewGuid().ToString("N"), token);
                }
                if (changes.Count > 0) { Interlocked.Exchange(ref _count, _database.Count); RefreshDatabaseSize(); }
                Volatile.Write(ref _status, _incomplete || _watchUnavailable || Volatile.Read(ref _watcherFailure) != 0 ? "Degraded" : "Ready");
                if (changes.Count > 0 || reconciled) Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            Interlocked.Increment(ref _errors);
            Volatile.Write(ref _status, "Unavailable");
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    private void RefreshDatabaseSize()
    {
        try
        {
            var size = new FileInfo(_options.DatabasePath).Length;
            if (File.Exists(_options.DatabasePath + "-wal")) size += new FileInfo(_options.DatabasePath + "-wal").Length;
            Interlocked.Exchange(ref _databaseBytes, size);
        }
        catch (IOException) { }
    }
    private async Task<bool> ScanAsync(string path, string root, string scan, CancellationToken token)
    {
        var batch = new List<IndexedFile>(250);
        var complete = true;
        async Task VisitAsync(string current, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _paused) != 0)
            {
                Volatile.Write(ref _status, "Paused");
                while (Volatile.Read(ref _paused) != 0) await Task.Delay(200, token).ConfigureAwait(false);
                Volatile.Write(ref _status, "Indexing");
            }
            if (!IsAllowed(current)) return;
            if (depth > 128) { complete = false; Interlocked.Increment(ref _errors); return; }
            var metadata = SearchFileAccess.Read(current);
            if (metadata is null) return;
            batch.Add(metadata);
            Interlocked.Increment(ref _scanned);
            if (batch.Count == 250)
            {
                _database.Upsert(batch, root, scan, token);
                batch.Clear();
                Changed?.Invoke(this, EventArgs.Empty);
                await Task.Delay(2, token).ConfigureAwait(false);
            }
            if (!metadata.IsDirectory) return;
            try
            {
                foreach (var child in SearchFileAccess.Children(current))
                    await VisitAsync(child, depth + 1).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { complete = false; Interlocked.Increment(ref _errors); }
        }
        await VisitAsync(path, 0).ConfigureAwait(false);
        if (batch.Count > 0) _database.Upsert(batch, root, scan, token);
        return complete;
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? worker;
        lock (_lifecycle) { _lifetime?.Cancel(); worker = _worker; }
        if (worker is not null) await worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_lifecycle)
        {
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear(); _lifetime?.Dispose(); _lifetime = null; _worker = null;
        }
    }
    public async ValueTask DisposeAsync() { await StopAsync(CancellationToken.None); _database.Dispose(); }
}
