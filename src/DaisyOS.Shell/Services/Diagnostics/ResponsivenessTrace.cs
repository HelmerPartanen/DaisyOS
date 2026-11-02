using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace DaisyOS.Shell.Services.Diagnostics;

/// <summary>Opt-in, bounded tracing. Render submission is deliberately not named presentation.</summary>
public static class ResponsivenessTrace
{
    private static readonly string? Output = Environment.GetEnvironmentVariable("DAISYOS_RESPONSIVENESS_TRACE");
    private static readonly ConcurrentQueue<TraceEntry> Events = new();
    private static readonly ConcurrentDictionary<string, long> Pending = new();
    private static readonly SemaphoreSlim FlushGate = new(1);
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly TimeSpan CaptureDuration = TimeSpan.FromSeconds(
        int.TryParse(Environment.GetEnvironmentVariable("DAISYOS_RESPONSIVENESS_SECONDS"), out var seconds)
            ? Math.Clamp(seconds, 1, 600) : 30);
    private static int _count;
    public static bool Enabled => !string.IsNullOrWhiteSpace(Output);
    public static void Begin(string operation)
    {
        if (!Enabled) return;
        Pending[operation] = Stopwatch.GetTimestamp();
        Mark("input.received", operation);
    }
    public static void Complete(string operation, string stage)
    {
        if (Pending.TryRemove(operation, out var start)) Mark(stage, operation, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
    public static async Task RunAsync(CancellationToken token)
    {
        if (!Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (Clock.Elapsed < CaptureDuration && Volatile.Read(ref _count) < 100000 &&
                   await timer.WaitForNextTickAsync(token))
            {
                Mark("runtime.gc.heap-bytes", durationMs: GC.GetTotalMemory(false));
                Mark("runtime.gc.gen2-count", durationMs: GC.CollectionCount(2));
                await FlushAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally { await FlushAsync(); }
    }
    public static void Mark(string stage, string? operation = null, double? durationMs = null)
    {
        if (!Enabled || Clock.Elapsed >= CaptureDuration || Interlocked.Increment(ref _count) > 100000) return;
        Events.Enqueue(new TraceEntry(Clock.Elapsed.TotalMilliseconds, stage, operation, durationMs, Environment.CurrentManagedThreadId));
    }
    public static IDisposable Measure(string stage, string? operation = null) => Enabled ? new Scope(stage, operation) : Empty.Instance;
    public static async Task FlushAsync()
    {
        if (!Enabled) return;
        await FlushGate.WaitAsync();
        try
        {
        var batch = new List<TraceEntry>();
        while (Events.TryDequeue(out var entry)) batch.Add(entry);
        if (batch.Count == 0) return;
        await File.AppendAllLinesAsync(Output!, batch.Select(entry => JsonSerializer.Serialize(entry, ResponsivenessJsonContext.Default.TraceEntry))).ConfigureAwait(false);
        }
        finally { FlushGate.Release(); }
    }
    private sealed class Scope(string stage, string? operation) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        public void Dispose() => Mark(stage, operation, Stopwatch.GetElapsedTime(_start).TotalMilliseconds);
    }
    private sealed class Empty : IDisposable { public static readonly Empty Instance = new(); public void Dispose() { } }
}

internal sealed record TraceEntry(double elapsedMs, string stage, string? operation, double? durationMs, int thread);
[global::System.Text.Json.Serialization.JsonSerializable(typeof(TraceEntry))]
internal partial class ResponsivenessJsonContext : global::System.Text.Json.Serialization.JsonSerializerContext { }
