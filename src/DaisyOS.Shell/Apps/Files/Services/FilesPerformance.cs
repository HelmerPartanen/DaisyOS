using System.Collections.Concurrent;
using System.Diagnostics;

namespace DaisyOS.Shell.Apps.Files.Services;

public sealed class FilesPerformance(string path)
{
    private readonly ConcurrentDictionary<string, double> _timings = new();
    private readonly ConcurrentDictionary<string, long> _counts = new();
    private readonly long _started = Stopwatch.GetTimestamp();
    public IReadOnlyDictionary<string, double> Timings => _timings;
    public IReadOnlyDictionary<string, long> Counts => _counts;
    public void Record(string name, double milliseconds) => _timings[name] = milliseconds;
    public IDisposable Measure(string name) => new Measurement(this, name);
    public void Count(string name, long count = 1) => _counts.AddOrUpdate(name, count, (_, old) => old + count);
    public bool FirstFrame() => _timings.TryAdd("First frame", Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
    public void Emit()
    {
        if (Environment.GetEnvironmentVariable("DAISYOS_FILES_PERF") != "1") return;
        Console.WriteLine($"FILES PERF\nPath: {path}\n" + string.Join("\n", _timings.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value:F2} ms")) +
            "\n" + string.Join("\n", _counts.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value}")));
    }
    private sealed class Measurement(FilesPerformance owner, string name) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        public void Dispose() => owner._timings[name] = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
    }
}
