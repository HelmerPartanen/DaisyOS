using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisyOS.Shell.Services.Wallpaper;
using DaisyOS.Shell.Apps.Files.Services;

namespace DaisyOS.Shell.Services.Diagnostics;

/// <summary>Opt-in real process/GC counters; never forces a collection or scans the visual tree.</summary>
internal static class ShellResourceTrace
{
    private const long MaximumFileBytes = 4L * 1024 * 1024;

    public static async Task RunAsync(CancellationToken token)
    {
        var path = Environment.GetEnvironmentVariable("DAISYOS_RESOURCE_TRACE");
        string? marker = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrWhiteSpace(runtime)) return;
            marker = Path.Combine(runtime, "daisyos", "resource-trace.enabled");
            if (!File.Exists(marker)) return;
            path = Path.Combine(runtime, "daisyos", "resource-trace.jsonl");
        }
        try
        {
            using var process = Process.GetCurrentProcess();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            do
            {
                if (marker is not null && !File.Exists(marker)) return;
                if (File.Exists(path) && new FileInfo(path).Length >= MaximumFileBytes) return;
                var sample = Capture(process);
                await File.AppendAllTextAsync(path,
                    JsonSerializer.Serialize(sample, ShellResourceJsonContext.Default.ShellResourceSample) + "\n", token).ConfigureAwait(false);
            } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"[resource-trace] Could not write resource counters: {ex.Message}");
        }
    }

    internal static ShellResourceSample Capture(Process process)
    {
        process.Refresh();
        long? pss = null, privateResident = null, anonymous = null;
        try
        {
            long privateBytes = 0;
            foreach (var line in File.ReadLines($"/proc/{process.Id}/smaps_rollup"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !long.TryParse(parts[1], out var kib)) continue;
                switch (parts[0])
                {
                    case "Pss:": pss = kib * 1024; break;
                    case "Private_Clean:": case "Private_Dirty:": privateBytes += kib * 1024; break;
                    case "Anonymous:": anonymous = kib * 1024; break;
                }
            }
            privateResident = privateBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var gc = GC.GetGCMemoryInfo();
        var thumbnails = ThumbnailCache.GetTotals();
        return new(DateTimeOffset.UtcNow, process.Id, process.WorkingSet64, pss, privateResident, anonymous,
            GC.GetTotalMemory(false), gc.HeapSizeBytes, gc.TotalCommittedBytes, gc.FragmentedBytes,
            GC.GetTotalAllocatedBytes(), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
            process.TotalProcessorTime.TotalMilliseconds, WallpaperSourceCache.CachedBytes,
            thumbnails.CachedBytes, thumbnails.LiveBytes);
    }
}

internal sealed record ShellResourceSample(DateTimeOffset TimestampUtc, int ProcessId, long RssBytes,
    long? PssBytes, long? PrivateResidentBytes, long? AnonymousResidentBytes, long ManagedEstimatedLiveBytes,
    long ManagedHeapAtLastGcBytes, long ManagedCommittedBytes, long ManagedFragmentedAtLastGcBytes,
    long TotalManagedAllocatedBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections,
    double CpuTimeMilliseconds, long WallpaperSourceCachedBytes, long ThumbnailCachedBytes, long ThumbnailLiveBytes);

[JsonSerializable(typeof(ShellResourceSample))]
internal partial class ShellResourceJsonContext : JsonSerializerContext { }
