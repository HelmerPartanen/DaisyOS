using System.Diagnostics;
using System.Text.Json;
using DaisyOS.Core.Search;
using DaisyOS.System.Search;

if (args.FirstOrDefault() == "--end-to-end") { await FileProviderBenchmark.RunAsync(); return; }

var directory = Directory.CreateTempSubdirectory("daisy-search-index-benchmark-").FullName;
try
{
    var options = new SearchIndexOptions(Path.Combine(directory, "db", "search.db"), [Path.Combine(directory, "documents")], [], TimeSpan.FromHours(1));
    using var database = new FileSearchDatabase(options);
    database.Initialize();
    var inserted = 0;
    var targets = args.Length > 0 ? args.Select(int.Parse).ToArray() : [10000, 100000, 500000, 1000000];
    var seedTimer = Stopwatch.StartNew();
    foreach (var target in targets)
    {
        while (inserted < target)
        {
            var count = Math.Min(250, target - inserted);
            var batch = Enumerable.Range(inserted, count).Select(i => new IndexedFile(
                Path.Combine(options.Roots[0], $"group-{i / 1000}", $"Project report {i:0000000}.pdf"),
                $"Project report {i:0000000}.pdf", Path.Combine(options.Roots[0], $"group-{i / 1000}"), ".pdf", false, i * 1024L, i)).ToArray();
            database.Upsert(batch, options.Roots[0], "benchmark", default);
            inserted += count;
        }
        var cold = Stopwatch.StartNew();
        database.Search(SearchQuery.Parse($"Project report {target - 1:0000000}.pdf"), default);
        var coldMs = cold.Elapsed.TotalMilliseconds;
        var cases = new[] { "project", "report", $"Project report {target - 1:0000000}.pdf", "report type:pdf", "missing" }.Select(SearchQuery.Parse).ToArray();
        var measurements = new List<double>();
        var perCase = cases.Select(_ => new List<double>()).ToArray();
        for (var i = 0; i < 100; i++)
        {
            var timer = Stopwatch.StartNew();
            var results = database.Search(cases[i % cases.Length], default);
            var duration = timer.Elapsed.TotalMilliseconds;
            measurements.Add(duration);
            perCase[i % cases.Length].Add(duration);
            if (results.Count > 250) throw new InvalidOperationException("Candidate retrieval is unbounded.");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { IndexedFiles = target, ColdRetrievalMs = coldMs,
            WarmP95Ms = measurements.Order().ElementAt(94), Samples = measurements.Count,
            Cases = cases.Select((query, index) => new { Query = query.RawText, P95Ms = perCase[index].Order().ElementAt(18) }).ToArray(),
            ElapsedSeedAndMeasureSeconds = seedTimer.Elapsed.TotalSeconds, ManagedBytes = GC.GetTotalMemory(false),
            DatabaseBytes = new FileInfo(options.DatabasePath).Length }));
    }
}
finally { Directory.Delete(directory, true); }
