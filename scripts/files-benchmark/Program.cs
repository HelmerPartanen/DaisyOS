using System.Diagnostics;
using System.Text.Json;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Files.Models;
using DaisyOS.Shell.Apps.Files.Services;

if (args.Length != 2) throw new ArgumentException("Usage: FilesBenchmark <fixture-root> <result.json>");
var results = new List<object>();
foreach (var dir in Directory.GetDirectories(args[0]).OrderBy(x => x))
{
    var runs = new List<object>();
    for (int run = 0; run < 3; run++)
    {
        GC.Collect();
        var allocations = GC.GetTotalAllocatedBytes(true);
        using var tab = new FilesTabViewModel(dir, navigateImmediately: false, uiDispatcher: new BenchmarkDispatcher(), watchFiles: false);
        var watch = Stopwatch.StartNew();
        await tab.NavigateToAsync(dir);
        var preparation = watch.Elapsed.TotalMilliseconds;
        if (!tab.CurrentSession!.IsReady) throw new Exception(tab.StatusText);
        var performance = tab.CurrentSession.Performance;
        var initialTimings = performance.Timings.ToDictionary(x => x.Key, x => x.Value);
        var initialAllocated = GC.GetTotalAllocatedBytes(true) - allocations;
        var events = tab.FilteredEntries.NotificationCount;
        var count = tab.EnumerationCount;
        watch.Restart();
        tab.SearchQuery = "*123*";
        await tab.ViewUpdateTask;
        var filter = watch.Elapsed.TotalMilliseconds;
        tab.SearchQuery = "";
        await tab.ViewUpdateTask;
        watch.Restart();
        tab.SortColumn = "Size";
        await tab.ViewUpdateTask;
        var sort = watch.Elapsed.TotalMilliseconds;
        if (tab.EnumerationCount != count) throw new Exception("Sort/filter enumerated the directory.");
        var entry = new FileEntrySnapshot(Path.Combine(dir, "mutation-test.txt"), "mutation-test.txt", ".txt", false, FileAttributes.Normal, 10, 10);
        watch.Restart();
        tab.ApplyDelta(new([entry], [], [], []));
        var insert = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        tab.ApplyDelta(new([], [], [new(entry.FullPath, entry with { FullPath = Path.Combine(dir, "renamed-test.txt"), Name = "renamed-test.txt" })], []));
        var rename = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        tab.ApplyDelta(new([], [Path.Combine(dir, "renamed-test.txt")], [], []));
        var remove = watch.Elapsed.TotalMilliseconds;
        var batch = Enumerable.Range(0, 64).Select(i => entry with { FullPath = Path.Combine(dir, $"batch-{i:00}.txt"), Name = $"batch-{i:00}.txt" }).ToArray();
        var beforeBatch = tab.FilteredEntries.NotificationCount;
        watch.Restart();
        tab.ApplyDelta(new(batch, [], [], []));
        var batchInsert = watch.Elapsed.TotalMilliseconds;
        var batchEvents = tab.FilteredEntries.NotificationCount - beforeBatch;
        tab.ApplyDelta(new([], batch.Select(x => x.FullPath).ToArray(), [], []));
        runs.Add(new { run, entries = tab.Entries.Count, preparation, timings = initialTimings,
            initialCollectionEvents = events, filter, sort, insert, rename, remove, batchInsert, batchEvents, initialAllocated,
            allocated = GC.GetTotalAllocatedBytes(true) - allocations, thumbnailsScheduled = performance.Counts.GetValueOrDefault("Thumbnails scheduled"),
            workers = tab.CurrentSession.Visuals.WorkerLimit, pendingVisuals = tab.CurrentSession.Visuals.PendingCount, firstPaint = (double?)null });
    }
    results.Add(new { path = dir, runs });
    Console.WriteLine(dir);
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(args[1], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("First paint and real container counts require the UI acceptance runner.");

internal sealed class BenchmarkDispatcher : IFileUiDispatcher
{
    public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
}
