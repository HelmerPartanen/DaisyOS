using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Processes;
using System.Text.RegularExpressions;

namespace DaisyOS.System.Displays;

public sealed partial class LinuxDisplayStatusService : IDisplayStatusService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private readonly ICommandRunner _commandRunner;

    public LinuxDisplayStatusService(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<DisplayStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase))
        {
            var kscreenResult = await _commandRunner.RunAsync("kscreen-doctor", ["-j"], CommandTimeout, cancellationToken);
            if (kscreenResult.Succeeded && !string.IsNullOrWhiteSpace(kscreenResult.StandardOutput))
            {
                try
                {
                    using var doc = global::System.Text.Json.JsonDocument.Parse(kscreenResult.StandardOutput);
                    if (doc.RootElement.TryGetProperty("outputs", out var outputsArray))
                    {
                        var connectedList = new List<DisplayInfo>();
                        foreach (var output in outputsArray.EnumerateArray())
                        {
                            var isConnected = output.TryGetProperty("connected", out var conn) && conn.GetBoolean();
                            if (!isConnected) continue;

                            var name = output.TryGetProperty("name", out var n) ? n.GetString() ?? "Display" : "Display";
                            var isPrimary = output.TryGetProperty("priority", out var p) && p.GetInt32() == 1;
                            var sizeProp = output.TryGetProperty("size", out var s) ? s : default;
                            var width = sizeProp.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                            var height = sizeProp.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                            var resolution = (width > 0 && height > 0) ? $"{width}x{height}" : "Unknown";

                            connectedList.Add(new DisplayInfo(name, isConnected, isPrimary, resolution));
                        }

                        if (connectedList.Count > 0)
                        {
                            var waylandPrimary = connectedList.FirstOrDefault(d => d.IsPrimary) ?? connectedList[0];
                            return new DisplayStatus(
                                true,
                                waylandPrimary.Name,
                                waylandPrimary.Resolution ?? "Unknown",
                                connectedList.Count,
                                BuildDetail(connectedList.ToArray()));
                        }
                    }
                }
                catch
                {
                    // Fall back to xrandr below
                }
            }
            return new DisplayStatus(false, "Unavailable", "Unavailable", 0, "Display status unavailable on Wayland.");
        }

        var result = await _commandRunner.RunAsync("xrandr", ["--query"], CommandTimeout, cancellationToken);
        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? "Display status is unavailable."
                : result.StandardError.Trim();
            return new DisplayStatus(false, "Unavailable", "Unavailable", 0, detail);
        }

        var displays = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseDisplayLine)
            .Where(display => display is not null)
            .Cast<DisplayInfo>()
            .ToArray();

        var connected = displays.Where(display => display.IsConnected).ToArray();
        if (connected.Length == 0)
        {
            return new DisplayStatus(false, "No display", "Unavailable", 0, result.StandardOutput.Trim());
        }

        var primary = connected.FirstOrDefault(display => display.IsPrimary) ?? connected[0];
        return new DisplayStatus(
            true,
            primary.Name,
            primary.Resolution ?? "Unknown",
            connected.Length,
            BuildDetail(connected));
    }

    private static DisplayInfo? ParseDisplayLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[1] is not ("connected" or "disconnected"))
        {
            return null;
        }

        var isConnected = parts[1] == "connected";
        var isPrimary = parts.Contains("primary", StringComparer.OrdinalIgnoreCase);
        var resolution = isConnected
            ? parts.Select(part => ResolutionRegex().Match(part))
                .FirstOrDefault(match => match.Success)
                ?.Value
            : null;

        return new DisplayInfo(parts[0], isConnected, isPrimary, resolution);
    }

    private static string BuildDetail(IReadOnlyList<DisplayInfo> displays)
    {
        var label = displays.Count == 1 ? "display" : "displays";
        var summaries = displays.Select(display =>
            string.IsNullOrWhiteSpace(display.Resolution)
                ? display.Name
                : $"{display.Name} at {display.Resolution}");
        return $"{displays.Count} connected {label}: {string.Join(", ", summaries)}.";
    }

    [GeneratedRegex(@"^\d+x\d+")]
    private static partial Regex ResolutionRegex();

    private sealed record DisplayInfo(string Name, bool IsConnected, bool IsPrimary, string? Resolution);
}
