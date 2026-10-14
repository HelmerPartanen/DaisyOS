using DaisyOS.Core.Models;
using DaisyOS.Core.Services;

namespace DaisyOS.System.Power;

public sealed class LinuxBatteryStatusService : IBatteryStatusService
{
    private const string PowerSupplyPath = "/sys/class/power_supply";

    public async Task<BatteryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
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
