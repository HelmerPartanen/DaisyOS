using DaisyOS.Core.Search;
using DaisyOS.System.Search;

namespace DaisyOS.Search.Tests;

public sealed class FileSearchTests
{
    [Fact]
    public void DatabasePersistsFtsUpdatesAndPrunesOnlyCompletedScans()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "Café report.pdf");
        var file = new IndexedFile(path, "Café report.pdf", fixture.Root, ".pdf", false, 20, 1);
        using (var database = new FileSearchDatabase(fixture.Options))
        {
            database.Initialize();
            database.Upsert([file], fixture.Root, "first", default);
            Assert.Single(database.Search(SearchQuery.Parse("cafe report"), default));
            database.Upsert([file with { Name = "Budget.pdf" }], fixture.Root, "second", default);
            Assert.Empty(database.Search(SearchQuery.Parse("cafe"), default));
            Assert.Single(database.Search(SearchQuery.Parse("budget"), default));
            database.FinishScan(fixture.Root, "second");
            Assert.Equal(1, database.Count);
        }
        using var reopened = new FileSearchDatabase(fixture.Options);
        reopened.Initialize();
        Assert.Single(reopened.Search(SearchQuery.Parse("budget"), default));
        reopened.FinishScan(fixture.Root, "third");
        Assert.Equal(0, reopened.Count);
        Assert.Empty(reopened.Search(SearchQuery.Parse("budget"), default));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(fixture.Options.DatabasePath));
    }
    [Fact]
    public void CancelledBatchesRollbackAndFtsSyntaxCannotBecomeSql()
    {
        using var fixture = new Fixture();
        using var database = new FileSearchDatabase(fixture.Options);
        database.Initialize();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => database.Upsert([new("path", "test", "", "", false, 0, 0)], fixture.Root, "x", cancellation.Token));
        Assert.Equal(0, database.Count);
        Assert.Empty(database.Search(SearchQuery.Parse("' OR 1=1; DROP TABLE Files; --"), default));
        Assert.Equal(0, database.Count);
    }
    [Fact]
    public async Task WatcherHandlesCreationRenameDeletionAndPopulatedDirectoryMoves()
    {
        using var fixture = new Fixture();
        using var database = new FileSearchDatabase(fixture.Options);
        await using var indexer = new FileSearchIndexer(fixture.Options, database);
        await indexer.StartAsync(default);
        await Eventually(() => indexer.Diagnostics.Status == "Ready");
        var old = Path.Combine(fixture.Root, "original.txt");
        await File.WriteAllTextAsync(old, "test");
        await Eventually(() => database.Search(SearchQuery.Parse("original"), default).Count == 1);
        var renamed = Path.Combine(fixture.Root, "renamed.txt");
        File.Move(old, renamed);
        await Eventually(() => database.Search(SearchQuery.Parse("renamed"), default).Count == 1 && database.Search(SearchQuery.Parse("original"), default).Count == 0);
        File.Delete(renamed);
        await Eventually(() => database.Search(SearchQuery.Parse("renamed"), default).Count == 0);
        var outside = Path.Combine(fixture.Base, "staging");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "nested.txt"), "test");
        Directory.Move(outside, Path.Combine(fixture.Root, "incoming"));
        await Eventually(() => database.Search(SearchQuery.Parse("nested"), default).Count == 1);
        Directory.Move(Path.Combine(fixture.Root, "incoming"), Path.Combine(fixture.Root, "moved"));
        await Eventually(() => database.Search(SearchQuery.Parse("nested"), default).SingleOrDefault()?.Parent == Path.Combine(fixture.Root, "moved"));
        Directory.Delete(Path.Combine(fixture.Root, "moved"), true);
        await Eventually(() => database.Search(SearchQuery.Parse("nested"), default).Count == 0);
    }
    [Fact]
    public async Task IndexRespectsExclusionsHiddenFilesSymlinksAndRevokedPermissions()
    {
        using var fixture = new Fixture();
        var excluded = Path.Combine(fixture.Root, "excluded");
        Directory.CreateDirectory(excluded);
        await File.WriteAllTextAsync(Path.Combine(excluded, "secret.txt"), "private");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".hidden.txt"), "private");
        var outside = Path.Combine(fixture.Base, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "escaped.txt"), "private");
        Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "link"), outside);
        var visible = Path.Combine(fixture.Root, "visible.txt");
        await File.WriteAllTextAsync(visible, "test");
        var options = fixture.Options with { Exclusions = [excluded] };
        using var database = new FileSearchDatabase(options);
        await using var indexer = new FileSearchIndexer(options, database);
        await indexer.StartAsync(default);
        await Eventually(() => indexer.Diagnostics.Status == "Ready");
        Assert.Empty(database.Search(SearchQuery.Parse("secret"), default));
        Assert.Empty(database.Search(SearchQuery.Parse("hidden"), default));
        Assert.Empty(database.Search(SearchQuery.Parse("escaped"), default));
        var provider = new FileSearchProvider(database, indexer, (_, _, _) => Task.CompletedTask);
        Assert.Single(await provider.SearchAsync(SearchQuery.Parse("visible"), default));
        File.SetUnixFileMode(visible, UnixFileMode.None);
        Assert.Empty(await provider.SearchAsync(SearchQuery.Parse("visible"), default));
        await Eventually(() => database.Search(SearchQuery.Parse("visible"), default).Count == 0);
        File.SetUnixFileMode(visible, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await Eventually(() => database.Search(SearchQuery.Parse("visible"), default).Count == 1);
        File.Delete(visible);
        File.CreateSymbolicLink(visible, Path.Combine(outside, "escaped.txt"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ExecuteAsync(new("open", provider.Id, visible, "Open"), default));
    }
    [Fact]
    public async Task ReconciliationRepairsMissedEventsAndPauseResumesWithoutLosingChanges()
    {
        using var fixture = new Fixture();
        using var database = new FileSearchDatabase(fixture.Options);
        await using var indexer = new FileSearchIndexer(fixture.Options, database);
        await indexer.StartAsync(default);
        await Eventually(() => indexer.Diagnostics.Status == "Ready");
        indexer.SetPaused(true);
        await Eventually(() => indexer.Diagnostics.Status == "Paused");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "paused.txt"), "test");
        await Task.Delay(300);
        Assert.Empty(database.Search(SearchQuery.Parse("paused"), default));
        indexer.SetPaused(false);
        await Eventually(() => database.Search(SearchQuery.Parse("paused"), default).Count == 1);
        await indexer.StopAsync(default);
        File.Delete(Path.Combine(fixture.Root, "paused.txt"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "repaired.txt"), "test");
        await indexer.StartAsync(default);
        await indexer.ReindexAsync(default);
        await Eventually(() => database.Search(SearchQuery.Parse("repaired"), default).Count == 1 && database.Search(SearchQuery.Parse("paused"), default).Count == 0);
    }
    [Fact]
    public void FileFiltersAreParameterizedAndRespectDirectoryBoundaries()
    {
        using var fixture = new Fixture();
        using var database = new FileSearchDatabase(fixture.Options);
        database.Initialize();
        database.Upsert([
            new("/Documents/report.pdf", "report.pdf", "/Documents", ".pdf", false, 200000000, 1),
            new("/MyDocuments/report.txt", "report.txt", "/MyDocuments", ".txt", false, 10, 1),
            new("/Documents/project", "project", "/Documents", "", true, 0, 1)], fixture.Root, "x", default);
        Assert.Single(database.Search(SearchQuery.Parse("report type:pdf size:>100MB in:Documents kind:file"), default));
        Assert.Single(database.Search(SearchQuery.Parse("kind:folder"), default));
        Assert.Empty(database.Search(SearchQuery.Parse("report type:txt in:Documents"), default));
        Assert.Single(database.Search(SearchQuery.Parse("size:<100 kind:file"), default));
    }
    [Fact]
    public void ExactNamesAreRetrievedEvenWhenBeyondTheFirstFtsCandidateWindow()
    {
        using var fixture = new Fixture();
        using var database = new FileSearchDatabase(fixture.Options);
        database.Initialize();
        var files = Enumerable.Range(0, 500).Select(i => new IndexedFile($"/Documents/{i}/report.pdf", "report.pdf", $"/Documents/{i}", ".pdf", false, i, 1))
            .Append(new("/Documents/report", "report", "/Documents", "", false, 1, 1)).ToArray();
        database.Upsert(files, fixture.Root, "x", default);
        Assert.Contains(database.Search(SearchQuery.Parse("report"), default), file => file.Path == "/Documents/report");
    }
    [Fact]
    public async Task CorruptDatabaseIsQuarantinedAndRebuilt()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.Options.DatabasePath)!);
        await File.WriteAllTextAsync(fixture.Options.DatabasePath, "not a sqlite database");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "recovered.txt"), "data");
        await using var service = new FileSearchService((_, _, _) => Task.CompletedTask, fixture.Options);
        await service.InitializeAsync(default);
        await Eventually(() => service.Diagnostics.Status == "Ready");
        Assert.Single(await service.SearchAsync(SearchQuery.Parse("recovered"), default));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(fixture.Options.DatabasePath)!, "*.corrupt-*"));
    }

    [Fact]
    public async Task SuccessfulFileActionsPersistRecentHistoryAndDeletedFilesDisappear()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "recent-report.txt");
        await File.WriteAllTextAsync(path, "data");
        var opened = 0;
        await using (var service = new FileSearchService((_, _, _) => { opened++; return Task.CompletedTask; }, fixture.Options))
        {
            await service.InitializeAsync(default);
            await Eventually(() => service.Diagnostics.Status == "Ready");
            var result = Assert.Single(await service.SearchAsync(SearchQuery.Parse("recent-report"), default));
            await service.ExecuteAsync(result.Actions[0], default);
        }
        Assert.Equal(1, opened);
        await using var reopened = new FileSearchService((_, _, _) => Task.CompletedTask, fixture.Options);
        await reopened.InitializeAsync(default);
        var recent = new RecentSearchProvider(reopened);
        Assert.Single(await recent.SearchAsync(SearchQuery.Parse("recent"), default));
        using var session = new SearchCoordinator([reopened, recent]).CreateSession();
        SearchSnapshot? final = null;
        await foreach (var snapshot in session.SearchAsync("recent-report")) final = snapshot;
        Assert.Single(final!.Results);
        File.Delete(path);
        Assert.Empty(await recent.SearchAsync(SearchQuery.Parse("recent"), default));
    }

    [Fact]
    public async Task CompletingASmallInitialScanPublishesAReadyNotification()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "small.txt"), "data");
        using var database = new FileSearchDatabase(fixture.Options);
        await using var indexer = new FileSearchIndexer(fixture.Options, database);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        indexer.Changed += (_, _) => { if (indexer.Diagnostics.Status == "Ready") ready.TrySetResult(); };
        await indexer.StartAsync(default);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, indexer.Diagnostics.ScannedEntries);
        Assert.Single(database.Search(SearchQuery.Parse("small"), default));
    }

    [Fact]
    public async Task WatcherFailureRecreatesWatchesAndReconcilesStaleMetadata()
    {
        using var fixture = new Fixture();
        using var database = new FileSearchDatabase(fixture.Options);
        await using var indexer = new FileSearchIndexer(fixture.Options, database);
        await indexer.StartAsync(default);
        await Eventually(() => indexer.Diagnostics.Status == "Ready");
        database.Upsert([new(Path.Combine(fixture.Root, "stale.txt"), "stale.txt", fixture.Root, ".txt", false, 0, 0)], fixture.Root, "missed-event", default);
        Assert.Single(database.Search(SearchQuery.Parse("stale"), default));
        indexer.ReportWatcherFailure();
        await Eventually(() => database.Search(SearchQuery.Parse("stale"), default).Count == 0 && indexer.Diagnostics.Status == "Ready");
        Assert.True(indexer.Diagnostics.ErrorCount > 0);
        Assert.Equal(1, indexer.Diagnostics.WatcherCount);
    }

    private static async Task Eventually(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (!predicate()) { Assert.True(DateTime.UtcNow < deadline, "Search index did not converge."); await Task.Delay(40); }
    }
    private sealed class Fixture : IDisposable
    {
        public string Base { get; } = Directory.CreateTempSubdirectory("daisy-search-").FullName;
        public string Root { get; }
        public SearchIndexOptions Options { get; }
        public Fixture()
        {
            Root = Directory.CreateDirectory(Path.Combine(Base, "documents")).FullName;
            Options = new(Path.Combine(Base, "index", "search.db"), [Root], [], TimeSpan.FromHours(1));
        }
        public void Dispose() => Directory.Delete(Base, true);
    }
}
