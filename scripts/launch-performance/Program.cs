using System.Diagnostics;
using System.Text.Json;
using DaisyOS.System.Processes;

var client = DaisyLaunchClient.Shared;
var app = "org.daisyos.dispatchbenchmark.desktop";
var samples = int.Parse(args[1]);
Environment.SetEnvironmentVariable("DAISYOS_LAUNCH_DEDUPE", "0");
await client.CatalogAsync();
var random = new Random(57);
var rows = new List<object>();
for (var iteration = -3; iteration < samples; iteration++)
{
    var order = new[] { "service", "kio-fallback", "legacy-direct" };
    random.Shuffle(order);
    foreach (var backend in order)
    {
        var start = Stopwatch.GetTimestamp();
        var result = backend switch
        {
            "service" => await client.LaunchAsync(app),
            "kio-fallback" => await DaisyLaunchClient.FallbackAsync(app),
            _ => new SafeProcessLauncher().Launch("/bin/true", [])
        };
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (!result.Succeeded) throw new InvalidOperationException($"{backend}: {result.ErrorCode}: {result.Message}");
        Dictionary<string, string?>? trace = null;
        if (result.RequestId is { } id) trace = JsonSerializer.Deserialize<Dictionary<string, string?>>(await client.TraceAsync(id));
        if (iteration >= 0) rows.Add(new { backend, elapsedMs = elapsed, trace,
            surface_associated = (long?)null, first_committed = (long?)null, first_presented = (long?)null, ready = (long?)null });
        await Task.Delay(75); // Stay below caller rate limit; outside every measured interval.
    }
}
await File.WriteAllTextAsync(args[0], JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
