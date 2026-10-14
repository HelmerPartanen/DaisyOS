using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Gaming;

public sealed class LinuxGamingService : IGamingService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);
    private readonly ICommandRunner _runner;

    public LinuxGamingService(ICommandRunner runner)
    {
        _runner = runner;
    }

    public async Task<GamingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var gpuTask = GetGpuInfoAsync(cancellationToken);
        var toolsTask = CheckToolsAsync(cancellationToken);
        var vulkanTask = CheckVulkanAsync(cancellationToken);
        var vulkan32Task = CheckAnyFileAsync(
            ["/usr/lib32/libvulkan.so.1", "/usr/lib/i386-linux-gnu/libvulkan.so.1"],
            cancellationToken);
        var controllerRulesTask = CheckAnyFileAsync(
            [
                "/usr/lib/udev/rules.d/60-game-devices-udev.rules",
                "/usr/lib/udev/rules.d/60-steam-input.rules",
                "/etc/udev/rules.d/60-game-devices-udev.rules",
                "/etc/udev/rules.d/60-steam-input.rules"
            ],
            cancellationToken);
        var bluetoothTask = CheckBluetoothServiceAsync(cancellationToken);

        await Task.WhenAll(gpuTask, toolsTask, vulkanTask, vulkan32Task, controllerRulesTask, bluetoothTask);

        var (gpuVendor, gpuModel) = await gpuTask;
        var (steam, gamemode, mangohud, gamescope, wine) = await toolsTask;
        var (vulkanAvailable, vulkanDriver) = await vulkanTask;
        var (bluetoothAvailable, bluetoothRunning) = await bluetoothTask;
        var vulkan32 = await vulkan32Task;
        var controllerRules = await controllerRulesTask;

        var details = BuildDetail(
            gpuVendor,
            gpuModel,
            steam,
            gamemode,
            mangohud,
            gamescope,
            wine,
            vulkanAvailable,
            vulkan32,
            vulkanDriver,
            controllerRules,
            bluetoothAvailable,
            bluetoothRunning);

        return new GamingStatus(
            steam,
            gamemode,
            mangohud,
            gamescope,
            wine,
            vulkanAvailable,
            vulkan32,
            vulkanDriver,
            gpuVendor,
            gpuModel,
            controllerRules,
            bluetoothAvailable,
            bluetoothRunning,
            details);
    }

    private async Task<(string Vendor, string Model)> GetGpuInfoAsync(CancellationToken ct)
    {
        var result = await _runner.RunAsync("lspci", ["-k"], DefaultTimeout, ct);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return ("Unknown", "Unknown");
        }

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains("VGA compatible", StringComparison.OrdinalIgnoreCase)
                || line.Contains("3D controller", StringComparison.OrdinalIgnoreCase))
            {
                var model = line;
                var vendor = DetectVendor(line);
                return (vendor, model.Trim());
            }
        }

        return ("Unknown", "Unknown");
    }

    private static string DetectVendor(string line)
    {
        if (line.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            return "AMD";
        }

        if (line.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return "Intel";
        }

        if (line.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Nvidia", StringComparison.OrdinalIgnoreCase))
        {
            return "NVIDIA";
        }

        return "Unknown";
    }

    private async Task<(bool Steam, bool GameMode, bool MangoHud, bool Gamescope, bool Wine)> CheckToolsAsync(CancellationToken ct)
    {
        var steamTask = ToolInstalledAsync("steam", ct);
        var gamemodeTask = ToolInstalledAsync("gamemoderun", ct);
        var mangohudTask = ToolInstalledAsync("mangohud", ct);
        var gamescopeTask = ToolInstalledAsync("gamescope", ct);
        var wineTask = ToolInstalledAsync("wine", ct);

        await Task.WhenAll(steamTask, gamemodeTask, mangohudTask, gamescopeTask, wineTask);

        return (await steamTask, await gamemodeTask, await mangohudTask, await gamescopeTask, await wineTask);
    }

    private async Task<bool> ToolInstalledAsync(string tool, CancellationToken ct)
    {
        var result = await _runner.RunAsync("which", [tool], DefaultTimeout, ct);
        return result.Succeeded && !string.IsNullOrWhiteSpace(result.StandardOutput);
    }

    private async Task<(bool Available, string? Driver)> CheckVulkanAsync(CancellationToken ct)
    {
        var result = await _runner.RunAsync("vulkaninfo", ["--summary"], DefaultTimeout, ct);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return (false, null);
        }

        var driver = ParseVulkanDriver(result.StandardOutput);
        return (true, driver);
    }

    private static Task<bool> CheckAnyFileAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        // File.Exists is semantically equivalent to `test -e` for regular files and is
        // orders of magnitude faster than spawning a child process per path.
        _ = ct; // cancellation not needed for synchronous filesystem check
        return Task.FromResult(paths.Any(File.Exists));
    }

    private async Task<(bool Available, bool Running)> CheckBluetoothServiceAsync(CancellationToken ct)
    {
        var loadState = await _runner.RunAsync(
            "systemctl",
            ["show", "bluetooth.service", "--property=LoadState", "--value"],
            DefaultTimeout,
            ct);
        var available = loadState.Succeeded
            && loadState.StandardOutput.Trim().Equals("loaded", StringComparison.OrdinalIgnoreCase);
        if (!available)
        {
            return (false, false);
        }

        var activeState = await _runner.RunAsync(
            "systemctl",
            ["is-active", "bluetooth.service"],
            DefaultTimeout,
            ct);
        return (true, activeState.Succeeded
            && activeState.StandardOutput.Trim().Equals("active", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ParseVulkanDriver(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Devices:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("GPU", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (trimmed.Contains("driver", StringComparison.OrdinalIgnoreCase)
                && trimmed.Contains(":", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split(':', 2);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    return parts[1].Trim();
                }
            }
        }

        return null;
    }

    private static string BuildDetail(
        string gpuVendor,
        string gpuModel,
        bool steam,
        bool gamemode,
        bool mangohud,
        bool gamescope,
        bool wine,
        bool vulkan,
        bool vulkan32,
        string? vulkanDriver,
        bool controllerRules,
        bool bluetoothAvailable,
        bool bluetoothRunning)
    {
        var parts = new List<string>
        {
            $"Graphics: {gpuVendor} — {gpuModel}"
        };

        if (vulkan)
        {
            parts.Add($"Vulkan: Ready{(vulkanDriver is not null ? $" — {vulkanDriver}" : "")}");
        }
        else
        {
            parts.Add("Vulkan: Not detected");
        }

        parts.Add($"32-bit Vulkan: {(vulkan32 ? "Ready" : "Not detected")}");
        parts.Add(ToolDetail("Steam", steam));
        parts.Add(ToolDetail("GameMode", gamemode));
        parts.Add(ToolDetail("MangoHud", mangohud));
        parts.Add(ToolDetail("Gamescope", gamescope));
        parts.Add(ToolDetail("Wine", wine));
        parts.Add($"Controller support: {(controllerRules ? "Ready" : "Rules not detected")}");
        parts.Add($"Bluetooth: {(bluetoothRunning ? "Running" : bluetoothAvailable ? "Available but stopped" : "Not detected")}");

        return string.Join(Environment.NewLine, parts);
    }

    private static string ToolDetail(string name, bool installed)
    {
        return installed ? $"{name}: Installed" : $"{name}: Not detected";
    }
}
