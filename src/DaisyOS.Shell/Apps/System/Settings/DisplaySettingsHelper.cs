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
    bool HdrAvailable = false,
    int Rotation = 1,
    int OutputId = 0,
    string? MirrorSource = null,
    int Brightness = -1,
    int MaxBpc = 0,
    string? IccProfilePath = null,
    string? ColorProfileSource = null)
{
    public int LogicalWidth => (int)Math.Ceiling((Rotation is 2 or 4 or 6 or 8 ? Height : Width) / Scale);
    public int LogicalHeight => (int)Math.Ceiling((Rotation is 2 or 4 or 6 or 8 ? Width : Height) / Scale);
}

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

    public static async Task<DetectedDisplay?> DetectPrimaryDisplayAsync(ICommandRunner? runner = null, CancellationToken cancellationToken = default, bool? waylandSession = null)
    {
        var all = await DetectAllDisplaysAsync(runner, cancellationToken, waylandSession);
        return all.FirstOrDefault(d => d.IsPrimary) ?? all.FirstOrDefault();
    }

    public static async Task<IReadOnlyList<DetectedDisplay>> DetectAllDisplaysAsync(ICommandRunner? runner = null, CancellationToken cancellationToken = default, bool? waylandSession = null)
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
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Fall back to xrandr
        }

        cancellationToken.ThrowIfCancellationRequested();
        // XWayland describes virtual compatibility outputs, not the real monitors.
        if (waylandSession ?? (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
            string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)))
            return Array.Empty<DetectedDisplay>();

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
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Unavailable
        }

        return Array.Empty<DetectedDisplay>();
    }

    public static async Task<bool> SetPrimaryDisplayAsync(string outputName, ICommandRunner? runner = null, CancellationToken cancellationToken = default)
    {
        runner ??= new SafeCommandRunner();
        var displays = await DetectAllDisplaysAsync(runner, cancellationToken);
        var selected = displays.FirstOrDefault(d => d.Name == outputName);
        if (selected is null) return false;
        var ordered = new[] { selected }.Concat(displays.Where(d => d.Name != outputName).OrderBy(d => d.Priority)).ToArray();
        var arguments = ordered.Select((d, index) => $"output.{d.Name}.priority.{index + 1}").ToArray();
        var result = await runner.RunAsync("kscreen-doctor", arguments, TimeSpan.FromSeconds(3), cancellationToken);
        return result.Succeeded;
    }

    public static async Task<bool> ApplyDisplayPositionsAsync(IReadOnlyList<(string OutputName, int X, int Y)> positions, ICommandRunner? runner = null, CancellationToken cancellationToken = default)
    {
        runner ??= new SafeCommandRunner();
        var args = positions.Select(p => $"output.{p.OutputName}.position.{p.X},{p.Y}").ToList();
        var result = await runner.RunAsync("kscreen-doctor", args, TimeSpan.FromSeconds(3), cancellationToken);
        return result.Succeeded;
    }

    public static string ConfigurationKey(IReadOnlyList<DetectedDisplay> displays) => string.Join(";",
        displays.OrderBy(d => d.Name, StringComparer.Ordinal).Select(d => FormattableString.Invariant(
            $"{d.Name}:{d.Width}:{d.Height}:{d.Scale}:{d.RefreshRate}:{d.PositionX}:{d.PositionY}:{d.Priority}:{d.Rotation}:{d.HdrEnabled}:{d.WideColorGamutEnabled}:{d.MirrorSource}:" ) +
            string.Join(",", d.Modes.Select(m => FormattableString.Invariant($"{m.Id}/{m.Width}/{m.Height}/{m.RefreshRate}")))));

    public static IReadOnlyList<string> LayoutArguments(IReadOnlyList<DetectedDisplay> displays, bool mirror)
    {
        if (displays.Count < 2) return Array.Empty<string>();
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
        var ordered = new[] { primary }.Concat(displays.Where(d => d.Name != primary.Name)).ToArray();
        var arguments = new List<string>();
        var x = 0;
        foreach (var display in ordered)
        {
            arguments.Add($"output.{display.Name}.mirror.{(mirror && display.Name != primary.Name ? primary.Name : "none")}");
            arguments.Add($"output.{display.Name}.position.{(mirror ? 0 : x)},0");
            x += display.LogicalWidth;
        }
        return arguments;
    }

    public static IReadOnlyList<string> RestoreArguments(IReadOnlyList<DetectedDisplay> previous, IReadOnlyList<DetectedDisplay> connected)
    {
        var arguments = new List<string>();
        foreach (var display in previous)
        {
            var current = connected.FirstOrDefault(d => d.Name == display.Name);
            if (current is null) continue; // Never target a connector that was unplugged.
            var mode = FindBestModeForResolution(current.Modes, display.Width, display.Height, display.RefreshRate);
            arguments.Add(BuildKScreenModeArgument(display.Name, mode, display.Width, display.Height, display.RefreshRate));
            arguments.Add($"output.{display.Name}.scale.{display.Scale.ToString(CultureInfo.InvariantCulture)}");
            var source = connected.Any(d => d.Name == display.MirrorSource) ? display.MirrorSource : "none";
            arguments.Add($"output.{display.Name}.mirror.{source}");
            arguments.Add($"output.{display.Name}.position.{display.PositionX},{display.PositionY}");
            arguments.Add($"output.{display.Name}.priority.{display.Priority}");
        }
        return arguments;
    }

    public static async Task<bool> RestoreConfigurationAsync(IReadOnlyList<DetectedDisplay> previous, ICommandRunner runner)
    {
        var connected = await DetectAllDisplaysAsync(runner);
        if (connected.Count == 0) return false;
        var arguments = RestoreArguments(previous, connected);
        return arguments.Count == 0 || (await runner.RunAsync("kscreen-doctor", arguments, TimeSpan.FromSeconds(5))).Succeeded;
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
                if (output.ValueKind != JsonValueKind.Object) continue;
                var connected = output.TryGetProperty("connected", out var c) && c.ValueKind == JsonValueKind.True;
                var enabled = output.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;
                if (!connected || !enabled) continue;

                var parsed = ParseSingleKScreenOutput(output);
                if (parsed is not null)
                {
                    list.Add(parsed);
                }
            }
            var primary = list.OrderBy(d => d.Priority > 0 ? d.Priority : int.MaxValue).FirstOrDefault();
            return list.Select(d => d with
            {
                IsPrimary = ReferenceEquals(d, primary),
                MirrorSource = int.TryParse(d.MirrorSource, out var sourceId)
                    ? list.FirstOrDefault(source => source.OutputId == sourceId)?.Name : d.MirrorSource
            }).ToArray();
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
            if (!double.IsFinite(scale) || scale <= 0) scale = 1;
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
                hdrAvailable,
                target.TryGetProperty("rotation", out var rotation) ? ScalarInt(rotation, 1) : 1,
                target.TryGetProperty("id", out var outputId) ? ScalarInt(outputId) : 0,
                target.TryGetProperty("replicationSource", out var source) && ScalarText(source) is { } sourceName && sourceName != "0"
                    ? sourceName : null,
                target.TryGetProperty("brightness", out var brightness) ? ScalarInt(brightness, -1) : -1,
                target.TryGetProperty("maxBpc", out var maxBpc) ? ScalarInt(maxBpc) : 0,
                target.TryGetProperty("iccProfilePath", out var iccPath) ? ScalarText(iccPath) : null,
                target.TryGetProperty("colorProfileSource", out var colorSource) ? ScalarText(colorSource) : null);
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

