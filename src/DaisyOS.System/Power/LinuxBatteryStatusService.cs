using DaisyOS.Core.Models;
using DaisyOS.Core.Services;

namespace DaisyOS.System.Power;

public sealed class LinuxBatteryStatusService : IBatteryStatusService
{
    private const string PowerSupplyPath = "/sys/class/power_supply";
    private const string ChassisTypePath = "/sys/class/dmi/id/chassis_type";

    // SMBIOS chassis types that describe stationary desktop systems. Some
    // desktop boards or UPS integrations expose a battery-like power supply;
    // that should not create a laptop-style status indicator in the shell.
    private static readonly HashSet<int> DesktopChassisTypes =
    [3, 4, 5, 6, 7, 15, 16, 17, 23, 35, 36];

    public async Task<BatteryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (await IsDesktopChassisAsync(cancellationToken))
        {
            return new BatteryStatus(false, null, "Unavailable", "Desktop systems do not have a built-in battery.");
        }

        if (!Directory.Exists(PowerSupplyPath))
        {
            return new BatteryStatus(false, null, "Unavailable", "No power supply information was found.");
        }

        foreach (var directory in Directory.EnumerateDirectories(PowerSupplyPath).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!await IsBatteryAsync(directory, cancellationToken))
            {
                continue;
            }

            var capacity = await ReadPercentAsync(Path.Combine(directory, "capacity"), cancellationToken);
            var state = await ReadTextAsync(Path.Combine(directory, "status"), cancellationToken) ?? "Unknown";
            var name = Path.GetFileName(directory);
            var detail = capacity is null
                ? $"{name}: {state}"
                : $"{name}: {capacity}% {state}";

            return new BatteryStatus(true, capacity, state, detail);
        }

        return new BatteryStatus(false, null, "Unavailable", "No battery power supply was found.");
    }

    private static async Task<bool> IsDesktopChassisAsync(CancellationToken cancellationToken)
    {
        var chassisType = await ReadTextAsync(ChassisTypePath, cancellationToken);
        return int.TryParse(chassisType, out var type) && DesktopChassisTypes.Contains(type);
    }

    private static async Task<bool> IsBatteryAsync(string directory, CancellationToken cancellationToken)
    {
        var type = await ReadTextAsync(Path.Combine(directory, "type"), cancellationToken);
        return string.Equals(type, "Battery", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int?> ReadPercentAsync(string path, CancellationToken cancellationToken)
    {
        var text = await ReadTextAsync(path, cancellationToken);
        if (!int.TryParse(text, out var percent))
        {
            return null;
        }

        return Math.Clamp(percent, 0, 100);
    }

    private static async Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
