using System;
using System.Threading;
using System.Threading.Tasks;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Display;

public sealed record NightLightConfiguration(string Active, string Mode, string NightTemperature);

public sealed class LinuxNightLightService
{
    private readonly ICommandRunner _commandRunner;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(2);

    public LinuxNightLightService(ICommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new SafeCommandRunner();
    }

    public async Task<NightLightConfiguration?> CaptureConfigurationAsync()
    {
        async Task<string?> Read(string key, string fallback)
        {
            var result = await _commandRunner.RunAsync("kreadconfig6", ["--file", "kwinrc", "--group", "NightColor", "--key", key, "--default", fallback], CommandTimeout);
            return result.Succeeded ? result.StandardOutput.Trim() : null;
        }
        var active = await Read("Active", "false");
        var mode = await Read("Mode", "0");
        var temperature = await Read("NightTemperature", "4500");
        return active is null || mode is null || temperature is null ? null : new(active, mode, temperature);
    }

    public async Task<bool> RestoreConfigurationAsync(NightLightConfiguration configuration)
    {
        foreach (var (key, value) in new[] { ("Mode", configuration.Mode), ("NightTemperature", configuration.NightTemperature), ("Active", configuration.Active) })
        {
            if (!(await _commandRunner.RunAsync("kwriteconfig6", ["--file", "kwinrc", "--group", "NightColor", "--key", key, value, "--notify"], CommandTimeout)).Succeeded) return false;
        }
        var reload = await _commandRunner.RunAsync("busctl", ["--user", "call", "org.kde.KWin", "/KWin", "org.kde.KWin", "reconfigure"], CommandTimeout);
        var stop = await _commandRunner.RunAsync("busctl", ["--user", "call", "org.kde.KWin", "/org/kde/KWin/NightLight", "org.kde.KWin.NightLight", "stopPreview"], CommandTimeout);
        return reload.Succeeded && stop.Succeeded;
    }

    public async Task<bool> SetNightLightAsync(bool enabled, uint temperature = 4500, CancellationToken cancellationToken = default)
    {
        temperature = Math.Clamp(temperature, 2500u, 6500u);

        if (enabled)
        {
            // KWin 6.7 Constant mode is zero. A manual toggle must persist,
            // unlike the compositor's 15-second color-temperature preview.
            var mode = await _commandRunner.RunAsync("kwriteconfig6", ["--file", "kwinrc", "--group", "NightColor", "--key", "Mode", "0"], CommandTimeout, cancellationToken);
            var warmth = await _commandRunner.RunAsync("kwriteconfig6", ["--file", "kwinrc", "--group", "NightColor", "--key", "NightTemperature", temperature.ToString()], CommandTimeout, cancellationToken);
            if (!mode.Succeeded || !warmth.Succeeded) return false;
        }
        var active = await _commandRunner.RunAsync("kwriteconfig6", ["--file", "kwinrc", "--group", "NightColor", "--key", "Active", enabled ? "true" : "false", "--notify"], CommandTimeout, cancellationToken);
        if (!active.Succeeded) return false;
        var reload = await _commandRunner.RunAsync("busctl", ["--user", "call", "org.kde.KWin", "/KWin", "org.kde.KWin", "reconfigure"], CommandTimeout, cancellationToken);
        var stop = await _commandRunner.RunAsync("busctl", ["--user", "call", "org.kde.KWin", "/org/kde/KWin/NightLight", "org.kde.KWin.NightLight", "stopPreview"], CommandTimeout, cancellationToken);
        if (!reload.Succeeded || !stop.Succeeded) return false;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var state = await _commandRunner.RunAsync("busctl", ["--user", "get-property", "org.kde.KWin", "/org/kde/KWin/NightLight", "org.kde.KWin.NightLight", "enabled"], CommandTimeout, cancellationToken);
            if (state.Succeeded && state.StandardOutput.Trim() == (enabled ? "b true" : "b false")) return true;
            if (attempt < 5) await Task.Delay(50, cancellationToken);
        }
        return false;
    }

    public async Task<bool> IsNightLightEnabledAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync("busctl", ["--user", "get-property", "org.kde.KWin", "/org/kde/KWin/NightLight", "org.kde.KWin.NightLight", "enabled"], CommandTimeout, cancellationToken);
        return result.Succeeded && result.StandardOutput.Trim() == "b true";
    }

    public async Task<bool> IsNightLightRunningAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync("busctl", ["--user", "get-property", "org.kde.KWin", "/org/kde/KWin/NightLight", "org.kde.KWin.NightLight", "running"], CommandTimeout, cancellationToken);
        return result.Succeeded && result.StandardOutput.Trim().EndsWith("true", StringComparison.OrdinalIgnoreCase);
    }
}
