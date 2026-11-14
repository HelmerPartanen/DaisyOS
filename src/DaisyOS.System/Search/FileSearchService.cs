using DaisyOS.Core.Search;
using Microsoft.Data.Sqlite;

namespace DaisyOS.System.Search;

/// <summary>Process-lifetime owner; initialization and scanning never belong to a launcher.</summary>
public sealed class FileSearchService : ISearchProvider, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task<FileSearchProvider> _ready;
    private FileSearchIndexer? _indexer;
    private FileSearchDatabase? _database;
    private int _disposed;
    public event EventHandler? Changed;
    public string Id => "files";
    public SearchProviderCapabilities Capabilities => new("Files and folders", [SearchResultType.File, SearchResultType.Folder],
        SearchStage.Extended, SupportsActions: true, RequiresDatabase: true, Timeout: TimeSpan.FromMilliseconds(200));
    public FileIndexDiagnostic Diagnostics => _indexer?.Diagnostics ?? new(_ready.IsFaulted ? "Unavailable" : "Initializing", 0, 0, null, 0, 0, false);
    public FileSearchService(Func<string, bool, CancellationToken, Task> open, SearchIndexOptions? options = null)
    {
        _ready = Task.Run(async () =>
        {
            if (!OperatingSystem.IsLinux() || SearchFileAccess.IsRoot) throw new InvalidOperationException("File indexing requires an ordinary Linux user session.");
            options ??= SearchIndexOptions.LoadDefaults();
            _database = new(options);
            try { _database.Initialize(); }
            catch (Exception ex) when (ex is InvalidDataException || ex is SqliteException { SqliteErrorCode: 11 or 26 })
            {
                _database.Dispose();
                var suffix = ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (var extension in new[] { "", "-wal", "-shm" })
                    if (File.Exists(options.DatabasePath + extension)) File.Move(options.DatabasePath + extension, options.DatabasePath + suffix + extension);
                _database = new(options);
                _database.Initialize();
            }
            _indexer = new(options, _database);
            _indexer.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            await _indexer.StartAsync(_lifetime.Token).ConfigureAwait(false);
            return new FileSearchProvider(_database, _indexer, open);
        });
        // Initialization failures are exposed through diagnostics, not unobserved task exceptions.
        _ = _ready.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }
    public async Task InitializeAsync(CancellationToken cancellationToken) => await _ready.WaitAsync(cancellationToken).ConfigureAwait(false);
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken) =>
        await (await _ready.WaitAsync(cancellationToken).ConfigureAwait(false)).SearchAsync(query, cancellationToken).ConfigureAwait(false);
    public async Task ExecuteAsync(SearchAction action, CancellationToken cancellationToken) =>
        await (await _ready.WaitAsync(cancellationToken).ConfigureAwait(false)).ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
    internal async Task<IReadOnlyList<IndexedFile>> RecentAsync(CancellationToken token)
    {
        await _ready.WaitAsync(token).ConfigureAwait(false);
        return _database!.Recent(token);
    }
    internal bool IsAllowed(string path) => _indexer?.IsAllowed(path) == true;
    public void SetPaused(bool paused) => _indexer?.SetPaused(paused);
    public Task ReindexAsync(CancellationToken token = default) => _indexer?.ReindexAsync(token) ?? Task.CompletedTask;
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        try { await _ready.ConfigureAwait(false); } catch { }
        if (_indexer is not null) await _indexer.DisposeAsync().ConfigureAwait(false);
        else _database?.Dispose();
        _lifetime.Dispose();
    }
}
