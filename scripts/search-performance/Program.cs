using System.Diagnostics;
using System.Text.Json;
using DaisyOS.Core.Models;
using DaisyOS.Core.Search;

// Synthetic catalog benchmark. Does not claim file-index or live-session performance.
foreach (var size in new[] { 1000, 10000 })
{
    var applications = new ApplicationSearchProvider();
    applications.ReplaceCatalog(Enumerable.Range(0, size).Select(i => new AppEntry($"app-{i}", $"Application {i:00000}", "Document utility", "", false, SearchTerms: ["editor"])));
    var registry = new SearchSettingsRegistry();
    for (var i = 0; i < size / 10; i++) registry.Register(new($"setting-{i}", $"Setting {i:0000}", "Appearance", ["theme"], _ => Task.FromResult(false), (_, _) => Task.CompletedTask));
    var settings = new SettingsSearchProvider(registry);
    var queries = new[] { "Application 00042", "App", "editor", "document utility", "missing" }.Select(SearchQuery.Parse).ToArray();
    for (var i = 0; i < 100; i++) applications.Search(queries[i % queries.Length]);
    var appTimes = new List<double>();
    var settingTimes = new List<double>();
    var aggregateTimes = new List<double>();
    var firstTimes = new List<double>();
    var coordinator = new SearchCoordinator([applications, settings]);
    using var session = coordinator.CreateSession();
    for (var i = 0; i < 250; i++)
    {
        var timer = Stopwatch.StartNew();
        await applications.SearchAsync(queries[i % queries.Length], default);
        appTimes.Add(timer.Elapsed.TotalMilliseconds);
        timer.Restart();
        await settings.SearchAsync(SearchQuery.Parse("theme"), default);
        settingTimes.Add(timer.Elapsed.TotalMilliseconds);
        timer.Restart();
        var first = true;
        await foreach (var snapshot in session.SearchAsync(queries[i % queries.Length].RawText))
        {
            if (first) { firstTimes.Add(timer.Elapsed.TotalMilliseconds); first = false; }
        }
        aggregateTimes.Add(timer.Elapsed.TotalMilliseconds);
    }
    static double P95(List<double> times) => times.Order().ElementAt((int)Math.Ceiling(times.Count * .95) - 1);
    Console.WriteLine(JsonSerializer.Serialize(new { Applications = size, Settings = size / 10, Samples = 250,
        ApplicationP95Ms = P95(appTimes), SettingsP95Ms = P95(settingTimes),
        FirstSnapshotP95Ms = P95(firstTimes), AggregationP95Ms = P95(aggregateTimes), ManagedBytes = GC.GetTotalMemory(false) }));
}
