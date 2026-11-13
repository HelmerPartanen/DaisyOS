using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DaisyOS.Shell.Services.Diagnostics;

/// <summary>Opt-in startup stage diagnostics; separate from readiness timing.</summary>
internal static class ApplicationStartupTrace
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("DAISYOS_STARTUP_TRACE") == "1";
    private static readonly long Origin = Enabled ? Stopwatch.GetTimestamp() : 0;

    internal static void Mark(string stage)
    {
        if (!Enabled) return;
        var sample = new StartupStage(stage, Stopwatch.GetElapsedTime(Origin).TotalMilliseconds,
            GC.GetTotalAllocatedBytes(), GC.CollectionCount(2));
        Console.Error.WriteLine("[startup-stage] " + JsonSerializer.Serialize(sample,
            StartupTraceJsonContext.Default.StartupStage));
    }
}

internal sealed record StartupStage(string Stage, double ElapsedMs, long AllocatedBytes, int Gen2Collections);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StartupStage))]
internal partial class StartupTraceJsonContext : JsonSerializerContext { }
