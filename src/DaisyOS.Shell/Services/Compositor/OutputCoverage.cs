using System.Text.Json;

namespace DaisyOS.Shell.Services.Compositor;

internal sealed record OutputCoverage(string OutputName, bool Covered, bool Fullscreen)
{
    public static IReadOnlyList<OutputCoverage>? Parse(string? payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload ?? "{}");
            if (!document.RootElement.TryGetProperty("outputCoverage", out var items) || items.ValueKind != JsonValueKind.Array) return null;
            return items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("outputName", out var name) && name.ValueKind == JsonValueKind.String)
                .Select(item => new OutputCoverage(item.GetProperty("outputName").GetString()!,
                    item.TryGetProperty("covered", out var covered) && covered.ValueKind == JsonValueKind.True,
                    item.TryGetProperty("fullscreen", out var full) && full.ValueKind == JsonValueKind.True)).ToArray();
        }
        catch (JsonException) { return null; }
    }

    internal static bool IsCovered(string? output, IEnumerable<CompositorWindowRecord> windows,
        IReadOnlyList<OutputCoverage>? coverage, bool global, bool fullscreenOnly)
    {
        if (string.IsNullOrEmpty(output)) return global;
        if (coverage is not null) return coverage.Any(c => c.OutputName == output && (fullscreenOnly ? c.Fullscreen : c.Covered));
        var covering = windows.Where(w => w.IsOnCurrentWorkspace && !w.IsMinimized &&
            (w.IsFullScreen || !fullscreenOnly && w.IsMaximized)).ToArray();
        // Older bridges still carry output names on ordinary application records.
        if (covering.Any(w => w.OutputName is not null)) return covering.Any(w => w.OutputName == output);
        return global;
    }
}
