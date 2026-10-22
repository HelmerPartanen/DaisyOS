using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Apps.System.Settings;

public sealed record DisplayModeInfo(
    string Id,
    int Width,
    int Height,
    double RefreshRate);

public sealed record DetectedDisplay(
    string Name,
    int Width,
    int Height,
    double Scale,
    double RefreshRate,
    IReadOnlyList<string> AvailableResolutions,
    IReadOnlyList<double> AvailableRefreshRates,
    IReadOnlyList<DisplayModeInfo> Modes,
    bool IsPrimary = true,
    int PositionX = 0,
    int PositionY = 0,
    int Priority = 1,
    bool HdrEnabled = false,
    bool WideColorGamutEnabled = false,
    bool HdrAvailable = false);

public static partial class DisplaySettingsHelper
{
    public static readonly string[] StandardFallbackResolutions =
    [
        "3840 × 2160",
        "2560 × 1440",
        "1920 × 1080",
        "1680 × 1050",
        "1600 × 900",
        "1440 × 900",
        "1366 × 768",
        "1280 × 1024",
        "1280 × 800",
        "1280 × 720",
        "1024 × 768",
        "800 × 600"
    ];

    public static async Task<DetectedDisplay?> DetectPrimaryDisplayAsync(ICommandRunner? runner = null, CancellationToken cancellationToken = default)
    {
        var all = await DetectAllDisplaysAsync(runner, cancellationToken);
        return all.FirstOrDefault(d => d.IsPrimary) ?? all.FirstOrDefault();
    }

    public static async Task<IReadOnlyList<DetectedDisplay>> DetectAllDisplaysAsync(ICommandRunner? runner = null, CancellationToken cancellationToken = default)
    {
        runner ??= new SafeCommandRunner();

        // 1. Try kscreen-doctor (Wayland / KDE)
        try
        {
            var result = await runner.RunAsync("kscreen-doctor", ["-j"], TimeSpan.FromSeconds(5), cancellationToken);
            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                var detected = ParseAllDisplaysKScreenDoctorJson(result.StandardOutput);
                if (detected.Count > 0)
                {
                    return detected;
                }
            }
        }
        catch
        {
            // Fall back to xrandr
        }

        // 2. Try xrandr (X11)
        try
        {
            var result = await runner.RunAsync("xrandr", ["--query"], TimeSpan.FromSeconds(5), cancellationToken);
            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                var detected = ParseXrandrOutput(result.StandardOutput);
                if (detected is not null)
                {
                    return [detected];
                }
            }
        }
        catch
        {
            // Unavailable
        }

        return Array.Empty<DetectedDisplay>();
    }

    public static async Task<bool> SetPrimaryDisplayAsync(string outputName, ICommandRunner? runner = null, CancellationToken cancellationToken = default)
    {
        runner ??= new SafeCommandRunner();
        var result = await runner.RunAsync("kscreen-doctor", [$"output.{outputName}.priority.1"], TimeSpan.FromSeconds(3), cancellationToken);
        return result.Succeeded;
    }

    public static async Task<bool> ApplyDisplayPositionsAsync(IReadOnlyList<(string OutputName, int X, int Y)> positions, ICommandRunner? runner = null, CancellationToken cancellationToken = default)
    {
        runner ??= new SafeCommandRunner();
        var args = positions.Select(p => $"output.{p.OutputName}.position.{p.X},{p.Y}").ToList();
        var result = await runner.RunAsync("kscreen-doctor", args, TimeSpan.FromSeconds(3), cancellationToken);
        return result.Succeeded;
    }

    public static DetectedDisplay? ParseKScreenDoctorJson(string json)
    {
        var all = ParseAllDisplaysKScreenDoctorJson(json);
        return all.FirstOrDefault(d => d.IsPrimary) ?? all.FirstOrDefault();
    }

    public static IReadOnlyList<DetectedDisplay> ParseAllDisplaysKScreenDoctorJson(string json)
    {
        try
        {
            var firstBrace = json.IndexOf('{');
            var lastBrace = json.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                json = json.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<DetectedDisplay>();
            }

            var list = new List<DetectedDisplay>();
            foreach (var output in outputs.EnumerateArray())
            {
                var connected = output.TryGetProperty("connected", out var c) && c.GetBoolean();
                var enabled = output.TryGetProperty("enabled", out var e) && e.GetBoolean();
                if (!connected || !enabled) continue;

                var parsed = ParseSingleKScreenOutput(output);
                if (parsed is not null)
                {
                    list.Add(parsed);
                }
            }
            return list;
        }
        catch
        {
            return Array.Empty<DetectedDisplay>();
        }
    }

    private static DetectedDisplay? ParseSingleKScreenOutput(JsonElement target)
    {
        try
        {
            var name = target.TryGetProperty("name", out var n) ? n.GetString() ?? "Display" : "Display";
            var scale = target.TryGetProperty("scale", out var s) ? ScalarDouble(s, 1.0) : 1.0;
            var priority = target.TryGetProperty("priority", out var p) ? ScalarInt(p, 1) : 1;
            var isPrimary = priority == 1;

            int posX = 0;
            int posY = 0;
            if (target.TryGetProperty("pos", out var posObj) && posObj.ValueKind == JsonValueKind.Object)
            {
                if (posObj.TryGetProperty("x", out var px)) posX = ScalarInt(px);
                if (posObj.TryGetProperty("y", out var py)) posY = ScalarInt(py);
            }

            // KScreen has emitted both strings and numbers for mode ids across versions.
            // GetString() throws for a numeric id, which used to discard the whole KScreen
            // result and make the XWayland/XRandR fallback incorrectly report 60 Hz.
            var currentModeId = target.TryGetProperty("currentModeId", out var cmid) ? ScalarText(cmid) : null;
            currentModeId ??= target.TryGetProperty("currentMode", out var cm) ? ScalarText(cm) : null;
            var hdrAvailable = target.TryGetProperty("hdr", out var hdrElement) &&
                hdrElement.ValueKind is JsonValueKind.True or JsonValueKind.False;
            var hdrEnabled = hdrAvailable && hdrElement.GetBoolean();
            var wideColorGamutEnabled = target.TryGetProperty("wcg", out var wcgElement) &&
                wcgElement.ValueKind is JsonValueKind.True or JsonValueKind.False && wcgElement.GetBoolean();

            var outputWidth = 0;
            var outputHeight = 0;
            if (target.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Object)
            {
                if (sz.TryGetProperty("width", out var ow)) outputWidth = ScalarInt(ow);
                if (sz.TryGetProperty("height", out var oh)) outputHeight = ScalarInt(oh);
            }

            var modesList = new List<DisplayModeInfo>();
            if (target.TryGetProperty("modes", out var modes) && modes.ValueKind == JsonValueKind.Array)
            {
                foreach (var mode in modes.EnumerateArray())
                {
                    var id = mode.TryGetProperty("id", out var mid) ? ScalarText(mid) ?? string.Empty : string.Empty;
                    var rate = mode.TryGetProperty("refreshRate", out var r) ? ScalarDouble(r, 60.0) : 60.0;
                    var mw = 0;
                    var mh = 0;
                    if (mode.TryGetProperty("size", out var msz) && msz.ValueKind == JsonValueKind.Object)
                    {
                        if (msz.TryGetProperty("width", out var mwidth)) mw = ScalarInt(mwidth);
                        if (msz.TryGetProperty("height", out var mheight)) mh = ScalarInt(mheight);
                    }

                    if (mw > 0 && mh > 0)
                    {
                        modesList.Add(new DisplayModeInfo(id, mw, mh, rate));
                    }
                }
            }

            DisplayModeInfo? activeMode = null;
            if (!string.IsNullOrWhiteSpace(currentModeId))
            {
                activeMode = modesList.FirstOrDefault(m => m.Id == currentModeId);
            }
            if (activeMode is null && outputWidth > 0 && outputHeight > 0)
            {
                activeMode = modesList.FirstOrDefault(m => m.Width == outputWidth && m.Height == outputHeight);
            }

            var width = activeMode?.Width ?? (outputWidth > 0 ? outputWidth : 1920);
            var height = activeMode?.Height ?? (outputHeight > 0 ? outputHeight : 1080);
            var refreshRate = activeMode?.RefreshRate ?? 60.0;

            var availableResolutions = modesList
                .Select(m => (m.Width, m.Height))
                .Distinct()
                .OrderByDescending(res => (long)res.Width * res.Height)
                .ThenByDescending(res => res.Width)
                .Select(res => $"{res.Width} × {res.Height}")
                .ToList();

            if (availableResolutions.Count == 0)
            {
                availableResolutions.Add($"{width} × {height}");
            }

            var availableRates = modesList
                .Where(m => m.Width == width && m.Height == height)
                .Select(m => m.RefreshRate)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            if (availableRates.Count == 0)
            {
                availableRates.Add(refreshRate);
            }

            return new DetectedDisplay(
                name,
                width,
                height,
                scale,
                refreshRate,
                availableResolutions,
                availableRates,
                modesList,
                isPrimary,
                posX,
                posY,
                priority,
                hdrEnabled,
                wideColorGamutEnabled,
                hdrAvailable);
        }
        catch
        {
            return null;
        }
    }

    public static DetectedDisplay? ParseXrandrOutput(string xrandrOutput)
    {
        if (string.IsNullOrWhiteSpace(xrandrOutput)) return null;

        var lines = xrandrOutput.Split('\n');
        string? currentOutput = null;
        bool isPrimary = false;
        bool isConnected = false;
        var modes = new List<DisplayModeInfo>();
        int activeWidth = 0;
        int activeHeight = 0;
        double activeRefreshRate = 60.0;

        string? selectedOutput = null;
        var selectedModes = new List<DisplayModeInfo>();
        int selectedWidth = 0;
        int selectedHeight = 0;
        double selectedRate = 60.0;
        bool selectedIsPrimary = false;

        void SaveCurrentIfEligible()
        {
            if (isConnected && modes.Count > 0)
            {
                if (selectedOutput is null || (isPrimary && !selectedIsPrimary))
                {
                    selectedOutput = currentOutput;
                    selectedModes = [..modes];
                    selectedWidth = activeWidth;
                    selectedHeight = activeHeight;
                    selectedRate = activeRefreshRate;
                    selectedIsPrimary = isPrimary;
                }
            }
        }

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;

            var isIndented = rawLine.StartsWith(' ') || rawLine.StartsWith('\t');
            var line = rawLine.Trim();

            if (!isIndented)
            {
                SaveCurrentIfEligible();

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1] is "connected" or "disconnected")
                {
                    currentOutput = parts[0];
                    isConnected = parts[1] == "connected";
                    isPrimary = parts.Contains("primary", StringComparer.OrdinalIgnoreCase);
                    modes.Clear();
                    activeWidth = 0;
                    activeHeight = 0;
                    activeRefreshRate = 60.0;

                    var match = ResolutionRegex().Match(line);
                    if (match.Success)
                    {
                        var dims = match.Value.Split('x');
                        if (dims.Length == 2 && int.TryParse(dims[0], out var cw) && int.TryParse(dims[1], out var ch))
                        {
                            activeWidth = cw;
                            activeHeight = ch;
                        }
                    }
                }
                else
                {
                    isConnected = false;
                }
            }
            else if (isConnected && currentOutput is not null)
            {
                var match = ModeResolutionRegex().Match(line);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var w) && int.TryParse(match.Groups[2].Value, out var h))
                {
                    var ratesPart = line.Substring(match.Length);
                    var rateMatches = RefreshRateRegex().Matches(ratesPart);
                    foreach (Match rm in rateMatches)
                    {
                        var rateStr = rm.Groups[1].Value;
                        if (double.TryParse(rateStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
                        {
                            var isCurrent = rm.Value.Contains('*');
                            modes.Add(new DisplayModeInfo($"{w}x{h}@{Math.Round(rate)}", w, h, rate));
                            if (isCurrent)
                            {
                                activeWidth = w;
                                activeHeight = h;
                                activeRefreshRate = rate;
                            }
                        }
                    }
                }
            }
        }

        SaveCurrentIfEligible();

        if (selectedOutput is null || selectedModes.Count == 0) return null;

        var availableResolutions = selectedModes
            .Select(m => (m.Width, m.Height))
            .Distinct()
            .OrderByDescending(res => (long)res.Width * res.Height)
            .ThenByDescending(res => res.Width)
            .Select(res => $"{res.Width} × {res.Height}")
            .ToList();

        if (selectedWidth == 0 || selectedHeight == 0)
        {
            selectedWidth = selectedModes[0].Width;
            selectedHeight = selectedModes[0].Height;
            selectedRate = selectedModes[0].RefreshRate;
        }

        var availableRates = selectedModes
            .Where(m => m.Width == selectedWidth && m.Height == selectedHeight)
            .Select(m => (int)Math.Round(m.RefreshRate))
            .Distinct()
            .OrderBy(x => x)
            .Select(x => (double)x)
            .ToList();

        if (availableRates.Count == 0)
        {
            availableRates.Add(Math.Round(selectedRate));
        }

        var fallbackScale = 1.0;
        var savedScalePref = (Avalonia.Application.Current as App)?.GetPreference("display.scale");
        if (!string.IsNullOrWhiteSpace(savedScalePref) &&
            double.TryParse(savedScalePref.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedPct) &&
            parsedPct > 0)
        {
            fallbackScale = parsedPct / 100.0;
        }

        return new DetectedDisplay(
            selectedOutput,
            selectedWidth,
            selectedHeight,
            fallbackScale,
            selectedRate,
            availableResolutions,
            availableRates,
            selectedModes);
    }

    public static bool TryParseResolution(string resolutionText, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(resolutionText)) return false;

        var parts = resolutionText.Split(['×', 'x', 'X', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height))
        {
            return width > 0 && height > 0;
        }

        return false;
    }

    public static DisplayModeInfo? FindBestModeForResolution(
        IReadOnlyList<DisplayModeInfo> modes,
        int targetWidth,
        int targetHeight,
        double preferredRefreshRate)
    {
        if (modes is null || modes.Count == 0) return null;

        var matching = modes.Where(m => m.Width == targetWidth && m.Height == targetHeight).ToList();
        if (matching.Count == 0) return null;

        return matching.OrderBy(m => Math.Abs(m.RefreshRate - preferredRefreshRate)).First();
    }

    public static string BuildKScreenModeArgument(string outputName, DisplayModeInfo? targetMode, int width, int height, double fallbackRate)
    {
        if (targetMode is not null && !string.IsNullOrWhiteSpace(targetMode.Id))
        {
            return $"output.{outputName}.mode.{targetMode.Id}";
        }

        var rate = (targetMode?.RefreshRate ?? fallbackRate).ToString("0.###", CultureInfo.InvariantCulture);
        return $"output.{outputName}.mode.{width}x{height}@{rate}";
    }

    public static string FormatRefreshRate(double refreshRate)
    {
        var nearestInteger = Math.Round(refreshRate);
        return Math.Abs(refreshRate - nearestInteger) < 0.1
            ? nearestInteger.ToString("0", CultureInfo.InvariantCulture)
            : refreshRate.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static int ScalarInt(JsonElement element, int fallback = 0)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt32(out var i)) return i;
            if (element.TryGetDouble(out var d)) return (int)Math.Round(d);
        }
        else if (element.ValueKind == JsonValueKind.String &&
                 double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return (int)Math.Round(d);
        }
        return fallback;
    }

    private static double ScalarDouble(JsonElement element, double fallback = 0.0)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var d)) return d;
        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return fallback;
    }

    private static string? ScalarText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };

    [GeneratedRegex(@"^\s*(\d+)x(\d+)")]
    private static partial Regex ModeResolutionRegex();

    [GeneratedRegex(@"\b\d+x\d+\b")]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)([\*\+]+)?")]
    private static partial Regex RefreshRateRegex();
}

