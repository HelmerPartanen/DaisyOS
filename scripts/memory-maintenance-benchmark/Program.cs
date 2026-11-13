using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using DaisyOS.Shell.Services.Diagnostics;

// Link the production service to isolate maintenance from GUI/driver activity.
// Cache stubs below leave GC, finalizer waiting and malloc_trim real.
var guarded = typeof(ShellMemoryTrimmer).GetConstructors().Single().GetParameters().Length == 3;
var run = typeof(ShellMemoryTrimmer).GetMethod("Run", BindingFlags.Instance | BindingFlags.NonPublic)!;
var scenarios = new List<object>();
foreach (var scenario in new[] { "gaming", "returned-visible", "locked", "pressure-during-game" })
{
    var samples = new List<double>();
    int collections = 0, reclaims = 0;
    for (int iteration = -2; iteration < 15; iteration++)
    {
        bool hidden = scenario != "returned-visible", gaming = scenario.Contains("gam");
        Action reclaim = () => { if (iteration >= 0) reclaims++; };
        using var trimmer = (ShellMemoryTrimmer)Activator.CreateInstance(typeof(ShellMemoryTrimmer), guarded
            ? new object?[] { (Func<bool>)(() => hidden), reclaim, (Func<bool>)(() => gaming) }
            : new object?[] { (Func<bool>)(() => hidden), reclaim })!;
        var heap = Enumerable.Range(0, 100000).Select(i => new byte[128 + i % 128]).ToArray();
        int before = GC.CollectionCount(2);
        var watch = Stopwatch.StartNew();
        run.Invoke(trimmer, new object[] { scenario != "pressure-during-game" });
        watch.Stop();
        if (iteration >= 0) { samples.Add(watch.Elapsed.TotalMilliseconds); collections += GC.CollectionCount(2) - before; }
        GC.KeepAlive(heap);
    }
    samples.Sort();
    scenarios.Add(new { scenario, samplesMs = samples, medianMs = samples[7], p95Ms = samples[14], gen2Collections = collections, cacheReclaims = reclaims });
    if (args.Contains("--verify") && (scenario == "locked" ? reclaims != 15 : reclaims != 0 || collections != 0))
        throw new Exception($"Maintenance policy failed: {scenario}, collections={collections}, reclaims={reclaims}");
}
Console.WriteLine(JsonSerializer.Serialize(new { protocol = 1, guarded, samples = 15, warmups = 2, scenarios }, new JsonSerializerOptions { WriteIndented = true }));

namespace DaisyOS.Shell.Apps.Files.Services { static class ThumbnailCache { public static void TrimAllUnused() { } } }
namespace DaisyOS.Shell.Services.Wallpaper { static class WallpaperSourceCache { public static void TrimUnused() { } } }
