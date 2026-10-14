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
    string PointerSpeed);

public sealed class LinuxInputHardwareService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(3);
    private readonly ICommandRunner _commandRunner;
    private readonly string _configFilePath;

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
                }
            }
        }
        catch
        {
            // Use defaults on read failure
        }

        return new InputHardwareStatus(naturalScroll, tapToClick, pointerSpeed);
    }

    public async Task<bool> SetNaturalScrollAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var val = enabled ? "true" : "false";
        var mouseSaved = await WriteConfigAsync("Mouse", "NaturalScroll", val, cancellationToken);
        var touchpadSaved = await WriteConfigAsync("Touchpad", "NaturalScroll", val, cancellationToken);
        return mouseSaved && touchpadSaved && await ReloadKWinAsync(cancellationToken);
    }

    public async Task<bool> SetTapToClickAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var val = enabled ? "true" : "false";
        return await WriteConfigAsync("Touchpad", "TapToClick", val, cancellationToken)
            && await ReloadKWinAsync(cancellationToken);
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
        return mouseSaved && touchpadSaved && await ReloadKWinAsync(cancellationToken);
    }

    private async Task<bool> WriteConfigAsync(string group, string key, string value, CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            "kwriteconfig6",
            ["--file", "kcminputrc", "--group", group, "--key", key, value],
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
