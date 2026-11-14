using DaisyOS.Core.Search;
using DaisyOS.System.Search;

namespace DaisyOS.Shell.Services;

/// <summary>Privacy-preserving status sent from the desktop owner to standalone Settings.</summary>
public sealed record SearchStatusReport(FileIndexDiagnostic? Index, int ApplicationCount, int SettingCount,
    SearchEngineDiagnostics Metrics);

internal static class SearchStatusProtocol
{
    // ApplicationPipe is line framed; the normal persistence context uses indented JSON.
    private static readonly DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext Wire = new(
        new global::System.Text.Json.JsonSerializerOptions(DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.Options)
        { WriteIndented = false });
    internal static string Serialize(SearchStatusReport report) =>
        global::System.Text.Json.JsonSerializer.Serialize(report, Wire.SearchStatusReport);
    internal static SearchStatusReport Deserialize(string json) =>
        global::System.Text.Json.JsonSerializer.Deserialize(json, Wire.SearchStatusReport)
        ?? throw new IOException("Search status is unavailable.");
}
