using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Input;

public sealed record InputHardwareStatus(
    bool NaturalScroll,
    bool TapToClick,
    string PointerSpeed,
    int TrackpadScrollSpeed = 0);

public sealed class LinuxInputHardwareService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(3);
    private readonly ICommandRunner _commandRunner;
    private readonly string _configFilePath;

    public static readonly double[] ScrollFactors = [0.4, 0.7, 1.0, 1.4, 1.8];

    public static int FactorToScrollSpeedIndex(double factor)
    {
        int closest = 0;
        double minDiff = double.MaxValue;
        for (int i = 0; i < ScrollFactors.Length; i++)
        {
            double diff = Math.Abs(ScrollFactors[i] - factor);
            if (diff < minDiff)
            {
                minDiff = diff;
                closest = i;
            }
        }
        return closest;
    }

    public static double ScrollSpeedIndexToFactor(int index)
    {
        if (index < 0) return ScrollFactors[0];
        if (index >= ScrollFactors.Length) return ScrollFactors[^1];
        return ScrollFactors[index];
    }

    public LinuxInputHardwareService(ICommandRunner? commandRunner = null, string? configFilePath = null)
    {
        _commandRunner = commandRunner ?? new SafeCommandRunner();
        _configFilePath = configFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "kcminputrc");
    }

    public async Task<InputHardwareStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        bool naturalScroll = false;
        bool tapToClick = true;
        string pointerSpeed = "Normal";
        int trackpadScrollSpeed = 0;

        try
        {
            if (File.Exists(_configFilePath))
            {
                var lines = await File.ReadAllLinesAsync(_configFilePath, cancellationToken);
                string currentGroup = "";
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                    {
                        currentGroup = trimmed[1..^1];
                        continue;
                    }

                    var equalsIdx = trimmed.IndexOf('=');
                    if (equalsIdx <= 0) continue;

                    var key = trimmed[..equalsIdx].Trim();
                    var value = trimmed[(equalsIdx + 1)..].Trim();

                    if (string.Equals(key, "NaturalScroll", StringComparison.OrdinalIgnoreCase))
                    {
                        naturalScroll = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(key, "TapToClick", StringComparison.OrdinalIgnoreCase))
                    {
                        tapToClick = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(key, "PointerAcceleration", StringComparison.OrdinalIgnoreCase) &&
                             double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var accel))
                    {
                        pointerSpeed = accel switch
                        {
                            <= -0.2 => "Slow",
                            >= 0.2 => "Fast",
                            _ => "Normal"
                        };
                    }
                    else if (string.Equals(key, "ScrollFactor", StringComparison.OrdinalIgnoreCase) &&
                             (currentGroup.Contains("Touchpad", StringComparison.OrdinalIgnoreCase) ||
                              string.IsNullOrEmpty(currentGroup)) &&
                             double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor))
                    {
                        trackpadScrollSpeed = FactorToScrollSpeedIndex(factor);
                    }
                }
            }
        }
        catch
        {
            // Use defaults on read failure
        }

        // KWin stores input preferences per physical device. Prefer the value
        // reported by the active touchpad so the UI always reflects what is
        // actually in use, while retaining the file value as an offline
        // fallback.
        var liveScrollFactor = await TryGetLiveKWinTouchpadScrollFactorAsync(cancellationToken);
        if (liveScrollFactor is not null)
        {
            trackpadScrollSpeed = FactorToScrollSpeedIndex(liveScrollFactor.Value);
        }

        return new InputHardwareStatus(naturalScroll, tapToClick, pointerSpeed, trackpadScrollSpeed);
    }

    public async Task<bool> SetNaturalScrollAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var val = enabled ? "true" : "false";
        var mouseSaved = await WriteConfigAsync("Mouse", "NaturalScroll", val, cancellationToken);
        var touchpadSaved = await WriteConfigAsync("Touchpad", "NaturalScroll", val, cancellationToken);
        var touchpadApplied = await ApplyLiveKWinTouchpadPropertyAsync(
            "naturalScroll", "b", val, cancellationToken);
        if (touchpadApplied) return mouseSaved && touchpadSaved;

        await ReloadKWinAsync(cancellationToken);
        return false;
    }

    public async Task<bool> SetTapToClickAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var val = enabled ? "true" : "false";
        var saved = await WriteConfigAsync("Touchpad", "TapToClick", val, cancellationToken);
        var applied = await ApplyLiveKWinTouchpadPropertyAsync(
            "tapToClick", "b", val, cancellationToken);
        if (applied) return saved;

        await ReloadKWinAsync(cancellationToken);
        return false;
    }

    public async Task<bool> SetPointerSpeedAsync(string speed, CancellationToken cancellationToken = default)
    {
        var accel = speed switch
        {
            "Slow" => "-0.4",
            "Fast" => "0.4",
            _ => "0.0"
        };

        var mouseSaved = await WriteConfigAsync("Mouse", "PointerAcceleration", accel, cancellationToken);
        var touchpadSaved = await WriteConfigAsync("Touchpad", "PointerAcceleration", accel, cancellationToken);
        var touchpadApplied = await ApplyLiveKWinTouchpadPropertyAsync(
            "pointerAcceleration", "d", accel, cancellationToken);
        if (touchpadApplied) return mouseSaved && touchpadSaved;

        await ReloadKWinAsync(cancellationToken);
        return false;
    }

    public async Task<bool> SetTrackpadScrollSpeedAsync(int speedIndex, CancellationToken cancellationToken = default)
    {
        var factor = ScrollSpeedIndexToFactor(speedIndex);
        var factorStr = factor.ToString("0.0#", CultureInfo.InvariantCulture);

        var touchpadSaved = await WriteConfigAsync("Touchpad", "ScrollFactor", factorStr, cancellationToken);
        var applied = await ApplyLiveKWinTouchpadScrollFactorAsync(factor, cancellationToken);

        // Setting the KWin device property applies the value immediately and
        // makes KWin persist it in the device's real nested Libinput group.
        // Keep the generic Touchpad entry above as DaisyOS's offline fallback.
        if (applied)
        {
            return touchpadSaved;
        }

        await ReloadKWinAsync(cancellationToken);
        return false;
    }

    /// <summary>
    /// Applies DaisyOS's slowest scroll speed on first use. Existing user or
    /// per-device KWin configuration is never overwritten.
    /// </summary>
    public async Task<bool> EnsureDefaultTrackpadScrollSpeedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var lines = await File.ReadAllLinesAsync(_configFilePath, cancellationToken);
                if (lines.Any(line => line.Split('=', 2)[0].Trim().Equals("ScrollFactor", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Try to establish the default even if the existing file could not
            // be inspected; SetTrackpadScrollSpeedAsync reports persistence errors.
        }

        return await SetTrackpadScrollSpeedAsync(0, cancellationToken);
    }

    private async Task<double?> TryGetLiveKWinTouchpadScrollFactorAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var devPath in await GetKWinInputDevicePathsAsync(cancellationToken))
            {
                if (!await IsKWinTouchpadAsync(devPath, cancellationToken)) continue;

                var result = await _commandRunner.RunAsync(
                    "busctl", ["--user", "get-property", "org.kde.KWin", devPath,
                        "org.kde.KWin.InputDevice", "scrollFactor"],
                    TimeSpan.FromMilliseconds(500), cancellationToken);
                if (result.Succeeded &&
                    TryParseBusctlDouble(result.StandardOutput, out var factor))
                {
                    return factor;
                }
            }
        }
        catch
        {
            // File-backed value remains available when KWin cannot be queried.
        }

        return null;
    }

    private async Task<bool> ApplyLiveKWinTouchpadScrollFactorAsync(double factor, CancellationToken cancellationToken)
        => await ApplyLiveKWinTouchpadPropertyAsync(
            "scrollFactor", "d", factor.ToString("0.0#", CultureInfo.InvariantCulture), cancellationToken);

    private async Task<bool> ApplyLiveKWinTouchpadPropertyAsync(
        string property,
        string signature,
        string value,
        CancellationToken cancellationToken)
    {
        var applied = false;
        try
        {
            foreach (var devPath in await GetKWinInputDevicePathsAsync(cancellationToken))
            {
                if (!await IsKWinTouchpadAsync(devPath, cancellationToken)) continue;

                var result = await _commandRunner.RunAsync(
                    "busctl", ["--user", "set-property", "org.kde.KWin", devPath,
                        "org.kde.KWin.InputDevice", property, signature, value],
                    TimeSpan.FromMilliseconds(500), cancellationToken);
                applied |= result.Succeeded;
            }
        }
        catch
        {
            // The saved fallback is still available if live application fails.
        }

        return applied;
    }

    private async Task<IReadOnlyList<string>> GetKWinInputDevicePathsAsync(CancellationToken cancellationToken)
    {
        var treeResult = await _commandRunner.RunAsync(
            "busctl", ["--user", "tree", "org.kde.KWin"], CommandTimeout, cancellationToken);
        if (!treeResult.Succeeded || string.IsNullOrWhiteSpace(treeResult.StandardOutput))
            return [];

        return treeResult.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault())
            .Where(path => path?.Contains("/InputDevice/event", StringComparison.Ordinal) == true)
            .Select(path => path!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<bool> IsKWinTouchpadAsync(string devPath, CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            "busctl", ["--user", "get-property", "org.kde.KWin", devPath,
                "org.kde.KWin.InputDevice", "touchpad"],
            TimeSpan.FromMilliseconds(500), cancellationToken);
        return result.Succeeded &&
               result.StandardOutput.Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseBusctlDouble(string output, out double value)
    {
        value = default;
        var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 &&
               double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private async Task<bool> WriteConfigAsync(string group, string key, string value, CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            "kwriteconfig6",
            ["--file", _configFilePath, "--group", group, "--key", key, value, "--notify"],
            CommandTimeout,
            cancellationToken);

        if (!result.Succeeded)
        {
            // Direct file fallback
            try
            {
                var dir = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var lines = File.Exists(_configFilePath)
                    ? (await File.ReadAllLinesAsync(_configFilePath, cancellationToken)).ToList()
                    : new List<string>();

                var groupHeader = $"[{group}]";
                var groupIdx = lines.FindIndex(l => l.Trim().Equals(groupHeader, StringComparison.OrdinalIgnoreCase));

                if (groupIdx < 0)
                {
                    lines.Add(groupHeader);
                    lines.Add($"{key}={value}");
                }
                else
                {
                    var inserted = false;
                    for (int i = groupIdx + 1; i < lines.Count; i++)
                    {
                        var trimmed = lines[i].Trim();
                        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                        {
                            lines.Insert(i, $"{key}={value}");
                            inserted = true;
                            break;
                        }
                        if (trimmed.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = $"{key}={value}";
                            inserted = true;
                            break;
                        }
                    }
                    if (!inserted)
                    {
                        lines.Add($"{key}={value}");
                    }
                }

                await File.WriteAllLinesAsync(_configFilePath, lines, cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> ReloadKWinAsync(CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            "busctl",
            ["--user", "call", "org.kde.KWin", "/KWin", "org.kde.KWin", "reconfigure"],
            CommandTimeout,
            cancellationToken);
        return result.Succeeded;
    }
}
