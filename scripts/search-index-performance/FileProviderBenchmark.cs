using System.Diagnostics;
using System.Text.Json;
using DaisyOS.Core.Search;
using DaisyOS.System.Search;

internal static class FileProviderBenchmark
{
    internal static async Task RunAsync()
    {
        var directory = Directory.CreateTempSubdirectory("daisy-search-e2e-").FullName;
        try
        {
            var root = Directory.CreateDirectory(Path.Combine(directory, "documents")).FullName;
            for (var i = 0; i < 10000; i++)
            {
                var group = Path.Combine(root, "group-" + i / 100);
                Directory.CreateDirectory(group);
                File.WriteAllBytes(Path.Combine(group, $"Report {i:0000000}.txt"), []);
            }
            var options = new SearchIndexOptions(Path.Combine(directory, "index", "search.db"), [root], [], TimeSpan.FromHours(1));
            await using var service = new FileSearchService((_, _, _) => Task.CompletedTask, options);
            var startup = Stopwatch.StartNew();
            await service.InitializeAsync(default);
            while (service.Diagnostics.Status != "Ready")
            {
                if (startup.Elapsed > TimeSpan.FromMinutes(2) || service.Diagnostics.Status == "Unavailable")
                    throw new InvalidOperationException("The benchmark index did not become ready.");
                await Task.Delay(20);
            }
            var indexingMs = startup.Elapsed.TotalMilliseconds;
            var cases = new[] { "report", "report 0009999", "type:txt", "kind:folder", "absent" }.Select(SearchQuery.Parse).ToArray();
            var measurements = new List<double>();
            for (var i = 0; i < 100; i++)
            {
                var timer = Stopwatch.StartNew();
                var results = await Task.Run(() => service.SearchAsync(cases[i % cases.Length], default));
                measurements.Add(timer.Elapsed.TotalMilliseconds);
                if (results.Count > 50) throw new InvalidOperationException("File provider results are unbounded.");
            }
            Console.WriteLine(JsonSerializer.Serialize(new { ActualFiles = 10000, IndexedEntries = service.Diagnostics.FileCount,
                InitialIndexingMs = indexingMs, ProviderWarmP95Ms = measurements.Order().ElementAt(94), Samples = 100,
                ManagedBytes = GC.GetTotalMemory(false) }));
        }
        finally { Directory.Delete(directory, true); }
    }
}
