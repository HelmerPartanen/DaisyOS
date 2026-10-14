using System.Text.RegularExpressions;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Bluetooth;

public sealed partial class LinuxBluetoothService : IBluetoothService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private readonly ICommandRunner _commandRunner;

    public LinuxBluetoothService(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<BluetoothStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_commandRunner is SafeCommandRunner && !HasBluetoothHardware())
        {
            return new BluetoothStatus(false, false, false, Array.Empty<BluetoothDevice>(),
                "Bluetooth isn’t available on this device.");
        }

        var adapter = await GetAdapterStatusAsync(cancellationToken);
        if (!adapter.IsAvailable) return adapter;

        var isEnabled = adapter.IsEnabled;
        var isDiscovering = adapter.IsDiscovering;

        var devices = new List<BluetoothDevice>();
        var deviceQueryFailed = false;
        if (isEnabled)
        {
            var resultDevices = await _commandRunner.RunAsync(
                "bluetoothctl",
                ["--timeout", "2", "devices"],
                CommandTimeout,
                cancellationToken);

            if (resultDevices.Succeeded)
            {
                var lines = resultDevices.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var seenAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Collect the unique (address, name) pairs first.
                var deviceEntries = new List<(string Address, string Name)>();
                foreach (var line in lines)
                {
                    var match = DeviceLineRegex().Match(line);
                    if (match.Success && seenAddresses.Add(match.Groups[1].Value))
                    {
                        deviceEntries.Add((match.Groups[1].Value, match.Groups[2].Value));
                    }
                }

                // Fetch all device info concurrently instead of sequentially — avoids
                // N × (50–150 ms) stall when many devices are paired.
                var infoTasks = deviceEntries.Select(entry =>
                    _commandRunner.RunAsync("bluetoothctl", ["--timeout", "2", "info", entry.Address], TimeSpan.FromSeconds(2), cancellationToken));

                var infoResults = await Task.WhenAll(infoTasks);

                for (var i = 0; i < deviceEntries.Count; i++)
                {
                    var (address, name) = deviceEntries[i];
                    var resultInfo = infoResults[i];

                    var isConnected = false;
                    var isPaired = false;
                    var type = "Unknown";

                    if (resultInfo.Succeeded)
                    {
                        isConnected = resultInfo.StandardOutput.Contains("Connected: yes", StringComparison.OrdinalIgnoreCase);
                        isPaired = resultInfo.StandardOutput.Contains("Paired: yes", StringComparison.OrdinalIgnoreCase);
                        type = ParseDeviceType(resultInfo.StandardOutput);
                    }

                    devices.Add(new BluetoothDevice(address, name, isConnected, isPaired, type));
                }
            }
            else
            {
                deviceQueryFailed = true;
            }
        }

        var detailText = !isEnabled
            ? "Bluetooth is disabled."
            : deviceQueryFailed
                ? "Bluetooth is enabled, but devices could not be queried."
                : devices.Count == 0
                    ? "No Bluetooth devices found."
                    : $"{devices.Count} Bluetooth {(devices.Count == 1 ? "device" : "devices")} found.";

        return new BluetoothStatus(true, isEnabled, isDiscovering, devices, detailText);
    }

    public async Task<BluetoothStatus> GetAdapterStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_commandRunner is SafeCommandRunner && !HasBluetoothHardware())
        {
            return new BluetoothStatus(false, false, false, Array.Empty<BluetoothDevice>(),
                "Bluetooth isn’t available on this device.");
        }

        var resultShow = await _commandRunner.RunAsync(
            "bluetoothctl",
            ["--timeout", "2", "show"],
            TimeSpan.FromSeconds(2),
            cancellationToken);
        if (!resultShow.Succeeded)
        {
            var diagnostic = string.Concat(resultShow.StandardOutput, "\n", resultShow.StandardError);
            if (diagnostic.Contains("no default controller", StringComparison.OrdinalIgnoreCase) ||
                diagnostic.Contains("no controller available", StringComparison.OrdinalIgnoreCase))
            {
                return new BluetoothStatus(false, false, false, Array.Empty<BluetoothDevice>(),
                    "Bluetooth isn’t available on this device.");
            }

            return new BluetoothStatus(false, false, false, Array.Empty<BluetoothDevice>(),
                BuildFailureDetail(resultShow, "Bluetooth status query"));
        }

        var isEnabled = ParseShowOutput(resultShow.StandardOutput);
        var isDiscovering = resultShow.StandardOutput.Contains("Discovering: yes", StringComparison.OrdinalIgnoreCase);
        return new BluetoothStatus(true, isEnabled, isDiscovering, Array.Empty<BluetoothDevice>(),
            isEnabled ? "Toggle Bluetooth" : "Bluetooth is turned off.");
    }

    private static bool HasBluetoothHardware()
    {
        try
        {
            if (Directory.Exists("/sys/class/bluetooth") && Directory.EnumerateFileSystemEntries("/sys/class/bluetooth").Any())
            {
                return true;
            }

            if (Directory.Exists("/sys/class/rfkill"))
            {
                foreach (var dir in Directory.EnumerateDirectories("/sys/class/rfkill"))
                {
                    var typeFile = Path.Combine(dir, "type");
                    if (File.Exists(typeFile) && File.ReadAllText(typeFile).Trim().Equals("bluetooth", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    public async Task<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var action = enabled ? "on" : "off";
        var result = await _commandRunner.RunAsync(
            "bluetoothctl",
            ["power", action],
            CommandTimeout,
            cancellationToken);

        if (!result.Succeeded)
        {
            return false;
        }

        return true;
    }

    public async Task<bool> SetConnectedAsync(string address, bool connected, CancellationToken cancellationToken = default)
    {
        if (!DeviceAddressRegex().IsMatch(address)) return false;
        var result = await _commandRunner.RunAsync(
            "bluetoothctl",
            ["--timeout", "5", connected ? "connect" : "disconnect", address],
            CommandTimeout,
            cancellationToken);
        return result.Succeeded;
    }

    public async Task<bool> StartDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            "bluetoothctl",
            ["--timeout", "5", "scan", "on"],
            TimeSpan.FromSeconds(6),
            cancellationToken);
        return result.Succeeded;
    }

    public async Task<bool> StopDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            "bluetoothctl",
            ["--timeout", "2", "scan", "off"],
            TimeSpan.FromSeconds(3),
            cancellationToken);
        return result.Succeeded;
    }

    public async Task<bool> PairDeviceAsync(string address, CancellationToken cancellationToken = default)
    {
        if (!DeviceAddressRegex().IsMatch(address)) return false;

        var pairResult = await _commandRunner.RunAsync(
            "bluetoothctl",
            ["--timeout", "15", "pair", address],
            TimeSpan.FromSeconds(16),
            cancellationToken);

        if (!pairResult.Succeeded)
        {
            return false;
        }

        // Trust device so it reconnects automatically
        await _commandRunner.RunAsync(
            "bluetoothctl",
            ["--timeout", "3", "trust", address],
            TimeSpan.FromSeconds(4),
            cancellationToken);

        // Connect immediately after pairing
        await _commandRunner.RunAsync(
            "bluetoothctl",
            ["--timeout", "10", "connect", address],
            TimeSpan.FromSeconds(11),
            cancellationToken);

        return true;
    }

    public async Task<bool> ForgetDeviceAsync(string address, CancellationToken cancellationToken = default)
    {
        if (!DeviceAddressRegex().IsMatch(address)) return false;

        var result = await _commandRunner.RunAsync(
            "bluetoothctl",
            ["--timeout", "5", "remove", address],
            TimeSpan.FromSeconds(6),
            cancellationToken);
        return result.Succeeded;
    }

    private static bool ParseShowOutput(string output)
    {
        return output.Contains("Powered: yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string ParseDeviceType(string output)
    {
        var icon = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith("Icon:", StringComparison.OrdinalIgnoreCase));

        if (icon is not null)
        {
            if (icon.Contains("audio", StringComparison.OrdinalIgnoreCase)) return "Audio";
            if (icon.Contains("keyboard", StringComparison.OrdinalIgnoreCase)) return "Keyboard";
            if (icon.Contains("mouse", StringComparison.OrdinalIgnoreCase)) return "Mouse";
            if (icon.Contains("phone", StringComparison.OrdinalIgnoreCase)) return "Phone";
            if (icon.Contains("gamepad", StringComparison.OrdinalIgnoreCase) || icon.Contains("joystick", StringComparison.OrdinalIgnoreCase)) return "Controller";
            if (icon.Contains("input", StringComparison.OrdinalIgnoreCase)) return "Input";
        }

        return output.Contains("UUID: Audio", StringComparison.OrdinalIgnoreCase) ? "Audio" : "Unknown";
    }

    private static string BuildFailureDetail(CommandResult result, string operation)
    {
        if (result.TimedOut)
        {
            return $"{operation} timed out.";
        }

        var error = result.StandardError.Trim();
        return string.IsNullOrWhiteSpace(error)
            ? $"{operation} failed."
            : $"{operation} failed: {error}";
    }

    [GeneratedRegex(@"^Device\s+([0-9A-Fa-f:]{17})\s+(.+)$")]
    private static partial Regex DeviceLineRegex();

    [GeneratedRegex(@"^[0-9A-Fa-f]{2}(:[0-9A-Fa-f]{2}){5}$")]
    private static partial Regex DeviceAddressRegex();
}
