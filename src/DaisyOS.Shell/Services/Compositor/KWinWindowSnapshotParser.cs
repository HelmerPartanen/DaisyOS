using System.Text.Json;

namespace DaisyOS.Shell.Services.Compositor;

/// <summary>Strict parser for the compact JSON payload published by the KWin script.</summary>
public static class KWinWindowSnapshotParser
{
    public static bool TryParse(string? payload, out IReadOnlyList<CompositorWindowRecord> windows)
        => TryParse(payload, out windows, out _, out _);

    internal static bool TryParse(string? payload, out IReadOnlyList<CompositorWindowRecord> windows,
        out bool desktopOccluded, out bool fullscreenActive)
    {
        windows = Array.Empty<CompositorWindowRecord>();
        desktopOccluded = fullscreenActive = false;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("windows", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var parsed = new List<CompositorWindowRecord>();
            foreach (var item in items.EnumerateArray())
            {
                var id = GetString(item, "id");
                var appId = GetString(item, "appId");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(appId))
                {
                    continue;
                }

                parsed.Add(new CompositorWindowRecord(
                    id,
                    appId,
                    GetString(item, "title") ?? appId,
                    GetString(item, "desktopFileId"),
                    GetString(item, "iconName"),
                    GetString(item, "outputName"),
                    GetInt32(item, "workspace"),
                    GetBoolean(item, "isActive"),
                    GetBoolean(item, "isMinimized"),
                    GetBoolean(item, "isMaximized"),
                    GetBoolean(item, "isFullScreen"),
                    !item.TryGetProperty("canClose", out var canClose) || canClose.ValueKind != JsonValueKind.False,
                    GetString(item, "daisyAppId"),
                    Math.Max(0, GetInt32(item, "processId")),
                    GetBoolean(item, "isUnresponsive"),
                    !item.TryGetProperty("isOnCurrentWorkspace", out var currentWorkspace) || currentWorkspace.ValueKind != JsonValueKind.False));
            }

            windows = parsed;
            desktopOccluded = document.RootElement.TryGetProperty("desktopOccluded", out var covered)
                ? covered.ValueKind == JsonValueKind.True : DesktopOcclusion.IsCovered(parsed);
            fullscreenActive = document.RootElement.TryGetProperty("fullscreenActive", out var fullscreen)
                ? fullscreen.ValueKind == JsonValueKind.True : DesktopOcclusion.HasFullscreen(parsed);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBoolean(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static int GetInt32(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
}
