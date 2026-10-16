using System.Collections.Specialized;
using Avalonia.Input;
using Avalonia.Input.Platform;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Files.Collections;
using DaisyOS.Shell.Apps.Files.Models;
using DaisyOS.Shell.Apps.Files.Services;
using Xunit;
using Directory = global::System.IO.Directory;
using File = global::System.IO.File;
using Path = global::System.IO.Path;
using FileAttributes = global::System.IO.FileAttributes;

namespace DaisyOS.Tests;

internal sealed class ImmediateFilesDispatcher : IFileUiDispatcher
{
    private readonly object _gate = new();
    public Task InvokeAsync(Action action) { lock (_gate) action(); return Task.CompletedTask; }
}

internal sealed class FilesFixture : IDisposable
{
    public string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"daisy-files-test-{Guid.NewGuid():N}")).FullName;
    public string Create(string name, string content = "data")
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path, content);
        return path;
    }
    public FilesTabViewModel Tab(DirectorySnapshotService? service = null, bool watch = false) =>
        new(Root, snapshots: service, navigateImmediately: false, uiDispatcher: new ImmediateFilesDispatcher(), watchFiles: watch);
    public void Dispose() => Directory.Delete(Root, true);
    public static FileEntrySnapshot Snapshot(string path, long size = 1, long ticks = 1, bool directory = false) =>
        new(path, Path.GetFileName(path), directory ? "" : Path.GetExtension(path).ToLowerInvariant(), directory,
            directory ? FileAttributes.Directory : FileAttributes.Normal, directory ? 0 : size, ticks);
}

public sealed class FastObservableCollectionTests
{
    [Fact]
    public void ReplaceAllEmitsOneResetAndCountAndIndexerChanges()
    {
        var collection = new FastObservableCollection<int>();
        List<NotifyCollectionChangedAction> changes = [];
        List<string?> properties = [];
        collection.CollectionChanged += (_, e) => changes.Add(e.Action);
        ((global::System.ComponentModel.INotifyPropertyChanged)collection).PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        collection.ReplaceAll(Enumerable.Range(0, 25000));
        Assert.Equal(25000, collection.Count);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, changes);
        Assert.Equal(new[] { "Count", "Item[]" }, properties);
    }
    [Fact]
    public void RangesAndNestedBatchesEmitOneResetEachButSingleInsertIsPrecise()
    {
        var collection = new FastObservableCollection<int>();
        List<NotifyCollectionChangedAction> actions = [];
        collection.CollectionChanged += (_, e) => actions.Add(e.Action);
        collection.AddRange([1, 2, 3]);
        collection.RemoveRange([1, 3]);
        collection.ApplyBatch(() => { collection.AddRange([4, 5]); collection.Remove(2); });
        collection.Insert(0, 6);
        Assert.Equal(new[] { 6, 4, 5 }, collection);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Add }, actions);
    }
}

public sealed class DirectoryIndexTests
{
    [Fact]
    public void DuplicateProtectionNormalizationRemovalAndRenamePreserveIdentity()
    {
        var index = new DirectoryIndex();
        var snapshot = FilesFixture.Snapshot("/tmp/daisy/a.txt");
        var a = index.Upsert(snapshot);
        Assert.Same(a, index.Find("/tmp/daisy/../daisy/a.txt"));
        Assert.Same(a, index.Upsert(snapshot));
        index.Upsert(FilesFixture.Snapshot("/tmp/daisy/b.txt"));
        var renamed = index.Rename(new(snapshot.FullPath, FilesFixture.Snapshot("/tmp/daisy/c.png")));
        Assert.Same(a, renamed);
        Assert.Null(index.Find(snapshot.FullPath));
        Assert.Equal(".png", renamed.Extension);
        Assert.Equal("Image File", renamed.DisplayType);
        Assert.Same(a, index.Remove("/tmp/daisy/c.png"));
        Assert.Single(index.Entries);
        Assert.NotNull(index.Remove("/tmp/daisy/b.txt"));
        Assert.Empty(index.Entries);
    }
    [Fact]
    public void RenameOverExistingPathKeepsSourceIdentityAndConsistentPositions()
    {
        var index = new DirectoryIndex();
        var a = index.Upsert(FilesFixture.Snapshot("/tmp/daisy/a"));
        index.Upsert(FilesFixture.Snapshot("/tmp/daisy/b"));
        index.Upsert(FilesFixture.Snapshot("/tmp/daisy/c"));
        Assert.Same(a, index.Rename(new("/tmp/daisy/a", FilesFixture.Snapshot("/tmp/daisy/b"))));
        Assert.Equal(2, index.Entries.Count);
        index.Remove("/tmp/daisy/c");
        Assert.Same(a, index.Remove("/tmp/daisy/b"));
        Assert.Empty(index.Entries);
    }
    [Fact]
    public void ViewModelConstructionAndSnapshotUpdatesWorkForNonexistentPaths()
    {
        var entry = new FileEntryViewModel(FilesFixture.Snapshot("/nonexistent/anywhere/a.txt", 1024, 100));
        entry.IsSelected = true;
        entry.IsRenaming = true;
        entry.Name = "editing.txt";
        entry.ApplySnapshot(entry.Snapshot with { Length = 2048 });
        Assert.Equal("editing.txt", entry.Name);
        Assert.True(entry.IsRenaming);
        Assert.True(entry.IsSelected);
        Assert.Equal(2048, entry.Length);
        Assert.False(entry.HasThumbnail);
    }
}

public sealed class DirectoryDeltaTests
{
    [Fact]
    public void ReconcilerFindsAddedRemovedAndChangedAndLeavesUnchangedEntriesAlone()
    {
        var index = new DirectoryIndex();
        var a = FilesFixture.Snapshot("/tmp/daisy/a");
        var b = FilesFixture.Snapshot("/tmp/daisy/b");
        var unchanged = FilesFixture.Snapshot("/tmp/daisy/unchanged");
        index.Upsert(a); index.Upsert(b); index.Upsert(unchanged);
        var delta = DirectoryReconciler.Diff(index, [a with { Length = 5 }, unchanged, FilesFixture.Snapshot("/tmp/daisy/c")]);
        Assert.Equal("/tmp/daisy/c", Assert.Single(delta.Added).FullPath);
        Assert.Equal(b.FullPath, Assert.Single(delta.Removed));
        Assert.Equal(5, Assert.Single(delta.Changed).Length);
        Assert.Empty(delta.Renamed);
    }
    [Fact]
    public async Task MixedBatchKeepsUnchangedSelectionAndEntryIdentity()
    {
        using var fixture = new FilesFixture();
        var keepPath = fixture.Create("keep.txt");
        var removePath = fixture.Create("remove.txt");
        var renamePath = fixture.Create("rename.txt");
        using var tab = fixture.Tab();
        await tab.NavigateToAsync(fixture.Root);
        var keep = tab.CurrentSession!.Index.Find(keepPath)!;
        var rename = tab.CurrentSession.Index.Find(renamePath)!;
        tab.SelectedItems.Add(keep);
        tab.ApplyDelta(new([FilesFixture.Snapshot(Path.Combine(fixture.Root, "add.txt"))], [removePath],
            [new(renamePath, FilesFixture.Snapshot(Path.Combine(fixture.Root, "renamed.png")))], [keep.Snapshot with { Length = 999 }]));
        Assert.Same(keep, tab.CurrentSession.Index.Find(keepPath));
        Assert.Same(keep, Assert.Single(tab.SelectedItems));
        Assert.Same(rename, tab.CurrentSession.Index.Find(Path.Combine(fixture.Root, "renamed.png")));
        Assert.Null(tab.CurrentSession.Index.Find(removePath));
        Assert.Equal(3, tab.FilteredEntries.Count);
    }
}

public sealed class FilesIncrementalUpdateTests
{
    [Fact]
    public async Task CreateRenameDeleteCopyMoveAndDragTransferNeverReenumerate()
    {
        using var fixture = new FilesFixture();
        using var sources = new FilesFixture();
        using var tab = fixture.Tab();
        await tab.NavigateToAsync(fixture.Root);
        var enumerationCount = tab.EnumerationCount;
        FileEntryViewModel? renameRequest = null;
        tab.RenameRequested += entry => renameRequest = entry;
        await tab.CreateNewFolderAsync();
        Assert.True(renameRequest!.IsRenaming);
        Assert.True(renameRequest.IsDirectory);
        await tab.CreateNewDocumentAsync();
        var document = renameRequest!;
        Assert.False(document.IsDirectory);
        document.IsRenaming = false;
        Assert.True(await tab.RenameAsync(document, "renamed.png"));
        Assert.Same(document, tab.CurrentSession!.Index.Find(Path.Combine(fixture.Root, "renamed.png")));
        Assert.Equal(".png", document.Extension);
        await tab.DeletePermanentlySelectedAsync();
        Assert.Null(tab.CurrentSession.Index.Find(document.Path));
        var copy = sources.Create("copy.txt");
        Assert.Equal(1, await tab.TransferFilesAsync([copy], fixture.Root, move: false));
        var move = sources.Create("move.txt");
        Assert.Equal(1, await tab.TransferFilesAsync([move], fixture.Root, move: true));
        Assert.False(File.Exists(move));
        Assert.Equal(enumerationCount, tab.EnumerationCount);
        Assert.Equal(0, tab.CurrentSession.Performance.Counts.GetValueOrDefault("Thumbnails scheduled"));
    }
    [Fact]
    public async Task PasteUsesTargetedDeltaAndPreservesExistingSelection()
    {
        using var fixture = new FilesFixture();
        using var sources = new FilesFixture();
        var keepPath = fixture.Create("keep.txt");
        var source = sources.Create("paste.txt");
        using var tab = fixture.Tab();
        await tab.NavigateToAsync(fixture.Root);
        var keep = tab.CurrentSession!.Index.Find(keepPath)!;
        tab.SelectedItems.Add(keep);
        tab.SystemClipboard = new FilesClipboard(source);
        var count = tab.EnumerationCount;
        await tab.PasteAsync();
        Assert.NotNull(tab.CurrentSession.Index.Find(Path.Combine(fixture.Root, "paste.txt")));
        Assert.Same(keep, Assert.Single(tab.SelectedItems));
        Assert.Equal(count, tab.EnumerationCount);
    }
    [Fact]
    public async Task TrashAndRestoreNeverReenumerateAndRetainUndoForFailures()
    {
        using var fixture = new FilesFixture();
        var path = fixture.Create($"daisy-trash-test-{Guid.NewGuid():N}.txt");
        using var tab = fixture.Tab();
        await tab.NavigateToAsync(fixture.Root);
        var count = tab.EnumerationCount;
        tab.SelectedItems.Add(tab.CurrentSession!.Index.Find(path)!);
        try
        {
            await tab.DeleteSelectedAsync();
            Assert.False(File.Exists(path));
            Assert.True(tab.CanUndoTrash);
            Assert.Null(tab.CurrentSession.Index.Find(path));
            await tab.UndoTrashAsync();
            Assert.True(File.Exists(path));
            Assert.False(tab.CanUndoTrash);
            Assert.NotNull(tab.CurrentSession.Index.Find(path));
            Assert.Equal(count, tab.EnumerationCount);
        }
        finally { if (tab.CanUndoTrash) await tab.UndoTrashAsync(); }
    }
    [Fact]
    public async Task ExplicitRefreshReconcilesWithoutReplacingSurvivingModels()
    {
        using var fixture = new FilesFixture();
        var path = fixture.Create("keep.txt");
        using var tab = fixture.Tab();
        await tab.NavigateToAsync(fixture.Root);
        var entry = tab.CurrentSession!.Index.Find(path)!;
        tab.SelectedItems.Add(entry);
        fixture.Create("added.txt");
        await tab.RefreshAsync();
        Assert.Same(entry, tab.CurrentSession.Index.Find(path));
        Assert.Same(entry, Assert.Single(tab.SelectedItems));
        Assert.Equal(2, tab.FilteredEntries.Count);
        Assert.Equal(2, tab.EnumerationCount);
    }
    private sealed class FilesClipboard(string path) : IClipboard
    {
        public Task ClearAsync() => Task.CompletedTask;
        public Task SetDataAsync(IAsyncDataTransfer? dataTransfer) => Task.CompletedTask;
        public Task FlushAsync() => Task.CompletedTask;
        public Task<IAsyncDataTransfer?> TryGetInProcessDataAsync() => Task.FromResult<IAsyncDataTransfer?>(null);
        public Task<IAsyncDataTransfer?> TryGetDataAsync()
        {
            var transfer = new DataTransfer();
            var item = new DataTransferItem();
            item.Set(ClipboardService.GnomeFilesFormat, global::System.Text.Encoding.UTF8.GetBytes("copy\n" + new Uri(path).AbsoluteUri));
            transfer.Add(item);
            return Task.FromResult<IAsyncDataTransfer?>(transfer);
        }
    }
}

public sealed class FilterMutationTests
{
    [Fact]
    public async Task InsertsAndRenamesRespectGlobFilterWithoutDiskAccess()
    {
        using var fixture = new FilesFixture();
        using var tab = fixture.Tab();
        await tab.NavigateToAsync(fixture.Root);
        tab.SearchQuery = "*.png";
        await tab.ViewUpdateTask;
        var txt = FilesFixture.Snapshot(Path.Combine(fixture.Root, "notes.txt"));
        tab.ApplyDelta(new([txt], [], [], []));
        Assert.Empty(tab.FilteredEntries);
        Assert.Single(tab.Entries);
        var png = txt with { FullPath = Path.Combine(fixture.Root, "screenshot.png"), Name = "screenshot.png", Extension = ".png" };
        tab.ApplyDelta(new([], [], [new(txt.FullPath, png)], []));
        var entry = Assert.Single(tab.FilteredEntries);
        tab.ApplyDelta(new([], [], [new(png.FullPath, txt)], []));
        Assert.Empty(tab.FilteredEntries);
        Assert.Same(entry, Assert.Single(tab.Entries));
        Assert.Equal(1, tab.EnumerationCount);
    }
}

public sealed class SortMutationTests
{
    [Theory]
    [InlineData("Name")]
    [InlineData("Size")]
    [InlineData("Date")]
    [InlineData("Type")]
    public async Task InsertRenameAndMetadataChangesRepositionOnlyAffectedEntry(string sort)
    {
        using var fixture = new FilesFixture();
        using var tab = fixture.Tab();
        await tab.NavigateToAsync(fixture.Root);
        tab.SortColumn = sort;
        tab.SortAscending = true;
        await tab.ViewUpdateTask;
        var a = FilesFixture.Snapshot(Path.Combine(fixture.Root, "a.txt"), 10, 10);
        var b = FilesFixture.Snapshot(Path.Combine(fixture.Root, "b.txt"), 20, 20);
        tab.ApplyDelta(new([b, a], [], [], []));
        Assert.Equal("a.txt", tab.FilteredEntries[0].Name);
        List<NotifyCollectionChangedAction> actions = [];
        tab.FilteredEntries.CollectionChanged += (_, e) => actions.Add(e.Action);
        if (sort == "Name") tab.ApplyDelta(new([], [], [new(a.FullPath, a with { FullPath = Path.Combine(fixture.Root, "z.txt"), Name = "z.txt" })], []));
        else if (sort == "Type") tab.ApplyDelta(new([], [], [new(b.FullPath, b with { FullPath = Path.Combine(fixture.Root, "b.png"), Name = "b.png", Extension = ".png" })], []));
        else tab.ApplyDelta(new([], [], [], [a with { Length = 30, LastWriteUtcTicks = 30 }]));
        Assert.Single(actions);
        Assert.Equal(NotifyCollectionChangedAction.Move, actions[0]);
        actions.Clear();
        var unrelated = tab.FilteredEntries[0];
        tab.ApplyDelta(new([], [], [], [unrelated.Snapshot with { Attributes = FileAttributes.ReadOnly }]));
        Assert.Empty(actions);
        Assert.Equal(1, tab.EnumerationCount);
    }
}

public sealed class WatcherTests
{
    [Fact]
    public void RepeatedEventsAreCoalescedAndRenameOrderIsRetained()
    {
        var events = DirectoryWatcher.Coalesce([new(DirectoryEventKind.Upsert, "/tmp/a"), new(DirectoryEventKind.Upsert, "/tmp/a"),
            new(DirectoryEventKind.Remove, "/tmp/temp"), new(DirectoryEventKind.Rename, "/tmp/b", "/tmp/a"), new(DirectoryEventKind.Upsert, "/tmp/b")]);
        Assert.Equal(3, events.Count);
        Assert.Equal(DirectoryEventKind.Rename, events[0].Kind);
        Assert.Single(events, e => e.Path == "/tmp/b" && e.Kind == DirectoryEventKind.Upsert);
    }
    [Fact]
    public async Task BufferedEventsAndOwnOperationDuplicatesApplyIncrementally()
    {
        using var fixture = new FilesFixture();
        using var tab = fixture.Tab();
        await tab.NavigateToAsync(fixture.Root);
        var path = fixture.Create("external.txt");
        await tab.ReconcilePathsAsync([path]);
        var entry = tab.CurrentSession!.Index.Find(path);
        tab.CurrentSession.Watcher.Enqueue(new(DirectoryEventKind.Upsert, path));
        await WaitFor(() => tab.CurrentSession.Performance.Timings.ContainsKey("Watcher application"));
        Assert.Single(tab.Entries);
        Assert.Same(entry, Assert.Single(tab.Entries));
        var renamed = Path.Combine(fixture.Root, "renamed.txt");
        File.Move(path, renamed);
        tab.CurrentSession.Watcher.Enqueue(new(DirectoryEventKind.Rename, renamed, path));
        await WaitFor(() => tab.CurrentSession.Index.Find(renamed) != null);
        Assert.Same(entry, tab.CurrentSession.Index.Find(renamed));
        File.Delete(renamed);
        tab.CurrentSession.Watcher.Enqueue(new(DirectoryEventKind.Remove, renamed));
        await WaitFor(() => tab.Entries.Count == 0);
        Assert.Equal(1, tab.EnumerationCount);
    }
    [Fact]
    public async Task OverflowUsesBackgroundSnapshotAndDiffPreservingIdentity()
    {
        using var fixture = new FilesFixture();
        var keep = fixture.Create("keep.txt");
        using var tab = fixture.Tab();
        await tab.NavigateToAsync(fixture.Root);
        var entry = tab.CurrentSession!.Index.Find(keep);
        fixture.Create("missed.txt");
        tab.CurrentSession.Watcher.Enqueue(new(DirectoryEventKind.Failure, fixture.Root));
        await WaitFor(() => tab.Entries.Count == 2 && tab.EnumerationCount >= 2);
        Assert.Same(entry, tab.CurrentSession.Index.Find(keep));
    }
    [Fact]
    public async Task RealFileSystemWatcherReportsExternalCreateRenameAndDelete()
    {
        using var fixture = new FilesFixture();
        using var tab = fixture.Tab(watch: true);
        await tab.NavigateToAsync(fixture.Root);
        var path = fixture.Create("external.txt");
        await WaitFor(() => tab.CurrentSession!.Index.Find(path) != null);
        var entry = tab.CurrentSession!.Index.Find(path);
        var renamed = Path.Combine(fixture.Root, "renamed.txt");
        File.Move(path, renamed);
        await WaitFor(() => tab.CurrentSession.Index.Find(renamed) != null);
        Assert.Same(entry, tab.CurrentSession.Index.Find(renamed));
        File.Delete(renamed);
        await WaitFor(() => tab.Entries.Count == 0);
        Assert.Equal(1, tab.EnumerationCount);
    }
    internal static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition(), "Timed out waiting for incremental state.");
    }
}

public sealed class SessionCancellationTests
{
    [Fact]
    public async Task NavigatingFromPopulatedDirectoryPublishesSkeletonUntilTheNewProjectionIsReady()
    {
        using var first = new FilesFixture();
        using var second = new FilesFixture();
        first.Create("old.txt");
        var service = new DelayedSnapshots(second.Root);
        using var tab = first.Tab(service);
        await tab.NavigateToAsync(first.Root);
        Assert.Single(tab.FilteredEntries);
        var observedLoadingSkeleton = false;
        tab.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(tab.ShowSkeleton) && tab.ShowSkeleton) observedLoadingSkeleton = true;
        };
        var navigation = tab.NavigateToAsync(second.Root);
        Assert.True(tab.ShowSkeleton);
        Assert.True(observedLoadingSkeleton);
        Assert.False(tab.IsEmpty);
        service.Release.SetResult([]);
        await navigation;
        Assert.False(tab.ShowSkeleton);
        Assert.True(tab.IsEmpty);
    }

    [Fact]
    public async Task OldEnumerationAndStaleDeltasCannotModifyNewDirectory()
    {
        using var first = new FilesFixture();
        using var second = new FilesFixture();
        var service = new DelayedSnapshots(first.Root);
        using var tab = first.Tab(service);
        var oldNavigation = tab.NavigateToAsync(first.Root);
        var oldSession = tab.CurrentSession!;
        await tab.NavigateToAsync(second.Root);
        service.Release.SetResult([FilesFixture.Snapshot(Path.Combine(first.Root, "old.txt"))]);
        await oldNavigation;
        tab.ApplyDelta(oldSession, new([FilesFixture.Snapshot(Path.Combine(first.Root, "stale.txt"))], [], [], []));
        oldSession.Watcher.Enqueue(new(DirectoryEventKind.Upsert, Path.Combine(first.Root, "late.txt")));
        Assert.Equal(second.Root, tab.CurrentPath);
        Assert.Empty(tab.Entries);
        Assert.False(tab.IsLoading);
        Assert.True(oldSession.IsDisposed);
    }
    [Fact]
    public async Task ChangesBufferedDuringEnumerationAppearAfterActivation()
    {
        using var fixture = new FilesFixture();
        var service = new DelayedSnapshots(fixture.Root);
        using var tab = fixture.Tab(service);
        var navigation = tab.NavigateToAsync(fixture.Root);
        var path = fixture.Create("during.txt");
        tab.CurrentSession!.Watcher.Enqueue(new(DirectoryEventKind.Upsert, path));
        service.Release.SetResult([]);
        await navigation;
        await WatcherTests.WaitFor(() => tab.CurrentSession.Index.Find(path) != null);
        Assert.Single(tab.Entries);
    }
    private sealed class DelayedSnapshots(string delayedPath) : DirectorySnapshotService
    {
        public TaskCompletionSource<IReadOnlyList<FileEntrySnapshot>> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<IReadOnlyList<FileEntrySnapshot>> EnumerateAsync(string path, CancellationToken token) =>
            path == delayedPath ? Release.Task : base.EnumerateAsync(path, token);
    }
}

public sealed class VisualLoadingTests
{
    [Fact]
    public void CacheEvictionAndInvalidationRetainImagesUntilTheLastLeaseEnds()
    {
        var cache = new ThumbnailCache(1);
        var image = new DisposableImage();
        var snapshot = FilesFixture.Snapshot("/tmp/lease.png");
        var first = cache.Acquire(snapshot, 96, () => new FileVisual(image, null, ""));
        var second = cache.Acquire(snapshot, 96, () => throw new Exception("Cached image was decoded again."));
        using var replacement = cache.Acquire(snapshot with { Length = 200 }, 96, () => FileVisual.Empty);
        Assert.Equal(1, cache.Count);
        Assert.False(image.Disposed);
        first.Dispose();
        Assert.False(image.Disposed);
        second.Dispose();
        Assert.True(image.Disposed);
        var liveImage = new DisposableImage();
        var live = cache.Acquire(snapshot, 192, () => new FileVisual(liveImage, null, ""));
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.False(liveImage.Disposed);
        live.Dispose();
        Assert.True(liveImage.Disposed);
    }

    [Fact]
    public async Task RapidViewportChangesBoundWorkersAndDiscardOldSessionResults()
    {
        using var first = new FilesFixture();
        using var second = new FilesFixture();
        for (int i = 0; i < 30; i++) first.Create($"image-{i:00}.png");
        using var tab = first.Tab();
        await tab.NavigateToAsync(first.Root);
        var session = tab.CurrentSession!;
        Assert.Equal(0, session.Visuals.PendingCount);
        Assert.Equal(0, session.Performance.Counts.GetValueOrDefault("Thumbnails scheduled"));
        var completions = new global::System.Collections.Concurrent.ConcurrentQueue<(CancellationToken Token, TaskCompletionSource<VisualLease> Completion)>();
        session.Visuals.Resolver = (_, _, token) =>
        {
            var completion = new TaskCompletionSource<VisualLease>(TaskCreationOptions.RunContinuationsAsynchronously);
            completions.Enqueue((token, completion));
            return completion.Task;
        };
        tab.SetViewport(tab.Entries.Take(10).ToArray());
        Assert.Equal(session.Visuals.WorkerLimit, session.Visuals.ActiveCount);
        Assert.Equal(session.Visuals.WorkerLimit, completions.Count);
        tab.SetViewport(tab.Entries.Skip(20).Take(10).ToArray());
        Assert.True(session.Visuals.PendingCount <= 10);
        Assert.All(completions, job => Assert.True(job.Token.IsCancellationRequested));
        var oldEntries = tab.Entries.ToArray();
        await tab.NavigateToAsync(second.Root);
        var image = new DisposableImage();
        foreach (var job in completions) job.Completion.SetResult(new VisualLease(new FileVisual(image, null, ""), image.Dispose));
        await WatcherTests.WaitFor(() => session.Visuals.ActiveCount == 0);
        Assert.All(oldEntries, entry => Assert.Null(entry.Thumbnail));
        Assert.True(image.Disposed);
        Assert.Empty(tab.Entries);
    }

    [Fact]
    public async Task PendingMetadataReadsCannotEnterTheNextSessionAndViewChangesDoNotReadMetadata()
    {
        using var first = new FilesFixture();
        using var second = new FilesFixture();
        var service = new DelayedRead();
        using var tab = first.Tab(service);
        await tab.NavigateToAsync(first.Root);
        var pending = tab.ReconcilePathsAsync([first.Create("late.txt")]);
        Assert.True(service.Started.Wait(TimeSpan.FromSeconds(5)));
        await tab.NavigateToAsync(second.Root);
        service.Release.Set();
        await pending;
        Assert.Empty(tab.Entries);
        var reads = service.ReadCount;
        foreach (var column in new[] { "Name", "Size", "Date", "Type" })
        {
            tab.SortColumn = column;
            await tab.ViewUpdateTask;
        }
        tab.IsGridView = !tab.IsGridView;
        tab.SearchQuery = "*.png";
        await tab.ViewUpdateTask;
        Assert.Equal(reads, service.ReadCount);
    }

    private sealed class DelayedRead : DirectorySnapshotService
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public int ReadCount;
        public override FileEntrySnapshot? Read(string path)
        {
            Interlocked.Increment(ref ReadCount);
            Started.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return base.Read(path);
        }
    }
    private sealed class DisposableImage : Avalonia.Media.IImage, IDisposable
    {
        public bool Disposed { get; private set; }
        public Avalonia.Size Size => new(1, 1);
        public void Draw(Avalonia.Media.DrawingContext context, Avalonia.Rect sourceRect, Avalonia.Rect destRect) { }
        public void Dispose() => Disposed = true;
    }
}

public sealed class NavigationCacheTests
{
    [Fact]
    public async Task ReturningToCachedDirectoryPreservesModelsAndReconcilesChanges()
    {
        using var first = new FilesFixture();
        using var second = new FilesFixture();
        var path = first.Create("keep.txt");
        using var tab = first.Tab();
        await tab.NavigateToAsync(first.Root);
        var entry = tab.CurrentSession!.Index.Find(path);
        await tab.NavigateToAsync(second.Root);
        first.Create("new.txt");
        await tab.NavigateToAsync(first.Root);
        Assert.Same(entry, tab.CurrentSession!.Index.Find(path));
        Assert.Equal(2, tab.Entries.Count);
    }

    [Fact]
    public async Task CreateDuringEnumerationRequestsRenameAfterActivation()
    {
        using var fixture = new FilesFixture();
        var service = new HeldEnumeration();
        using var tab = fixture.Tab(service);
        var navigation = tab.NavigateToAsync(fixture.Root);
        FileEntryViewModel? renamed = null;
        tab.RenameRequested += entry => renamed = entry;
        await tab.CreateNewDocumentAsync();
        service.Release.SetResult([]);
        await navigation;
        Assert.NotNull(renamed);
        Assert.True(renamed.IsRenaming);
        Assert.Contains(renamed, tab.SelectedItems);
        Assert.Single(tab.Entries);
    }
    private sealed class HeldEnumeration : DirectorySnapshotService
    {
        public TaskCompletionSource<IReadOnlyList<FileEntrySnapshot>> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<IReadOnlyList<FileEntrySnapshot>> EnumerateAsync(string path, CancellationToken token) => Release.Task;
    }
}

public sealed class BatchMutationTests
{
    [Fact]
    public async Task LargeDeltaUsesOneResetAndIncrementalOrderingWithoutFullSort()
    {
        using var fixture = new FilesFixture();
        var path = fixture.Create("keep.txt");
        using var tab = fixture.Tab();
        await tab.NavigateToAsync(fixture.Root);
        var keep = tab.CurrentSession!.Index.Find(path)!;
        tab.SelectedItems.Add(keep);
        var before = tab.FilteredEntries.NotificationCount;
        var sort = tab.CurrentSession.Performance.Timings["Sort"];
        var batch = Enumerable.Range(0, 256).Select(i => FilesFixture.Snapshot(Path.Combine(fixture.Root, $"added-{i:03}.txt"))).Reverse().ToArray();
        tab.ApplyDelta(new(batch, [], [], []));
        Assert.Equal(before + 1, tab.FilteredEntries.NotificationCount);
        Assert.Equal(sort, tab.CurrentSession.Performance.Timings["Sort"]);
        Assert.Same(keep, Assert.Single(tab.SelectedItems));
        Assert.Equal(tab.FilteredEntries.OrderBy(x => x.NameSortKey, StringComparer.CurrentCultureIgnoreCase), tab.FilteredEntries);
        tab.ApplyDelta(new([], batch.Select(x => x.FullPath).ToArray(), [], []));
        Assert.Same(keep, Assert.Single(tab.FilteredEntries));
        Assert.Equal(1, tab.EnumerationCount);
    }
}

public sealed class WatcherRecoveryBoundaryTests
{
    [Fact]
    public void RootAndOtherDirectoryEventsCannotBecomeChildren()
    {
        using var fixture = new FilesFixture();
        using var other = new FilesFixture();
        List<DirectoryEvent> events = [];
        using var watcher = new DirectoryWatcher(fixture.Root, batch => events.AddRange(batch), enabled: false);
        watcher.Enqueue(new(DirectoryEventKind.Upsert, fixture.Root));
        watcher.Enqueue(new(DirectoryEventKind.Upsert, other.Create("unrelated.txt")));
        watcher.Activate();
        Assert.Empty(events);
        var child = fixture.Create("child.txt");
        watcher.Enqueue(new(DirectoryEventKind.Upsert, child));
        watcher.Activate();
        Assert.Equal(child, Assert.Single(events).Path);
    }

    [Fact]
    public async Task MetadataFailurePreservesAnExistingEntryAndReconciles()
    {
        using var fixture = new FilesFixture();
        var path = fixture.Create("keep.txt");
        using var tab = fixture.Tab(new UnreadableMetadata());
        await tab.NavigateToAsync(fixture.Root);
        var keep = tab.CurrentSession!.Index.Find(path)!;
        tab.SelectedItems.Add(keep);
        await tab.ReconcilePathsAsync([path]);
        Assert.Same(keep, tab.CurrentSession.Index.Find(path));
        await WatcherTests.WaitFor(() => tab.EnumerationCount >= 2);
        Assert.Same(keep, tab.CurrentSession.Index.Find(path));
        Assert.Same(keep, Assert.Single(tab.SelectedItems));
    }
    private sealed class UnreadableMetadata : DirectorySnapshotService
    {
        public override FileEntrySnapshot? Read(string path) => throw new UnauthorizedAccessException("Test metadata read denied.");
    }
}

public sealed class PassiveFileEntryTests
{
    [Fact]
    public void SnapshotConstructionNeedsNoFileAndSizeChangesNotifyOnlyAffectedProperties()
    {
        var snapshot = FilesFixture.Snapshot(Path.Combine(Path.GetTempPath(), $"no-file-{Guid.NewGuid():N}.txt"), size: 123);
        Assert.False(File.Exists(snapshot.FullPath));
        var entry = new FileEntryViewModel(snapshot);
        Assert.Equal(123, entry.Length);
        List<string?> properties = [];
        entry.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        entry.ApplySnapshot(snapshot with { Length = 456 });
        Assert.Equal(new[] { "Length", "DisplaySize" }, properties);
        Assert.Equal("456 B", entry.DisplaySize);
        Assert.Equal(snapshot.Name, entry.NameSortKey);
    }
}
