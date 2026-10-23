using System.Text.RegularExpressions;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Bluetooth;

public sealed partial class LinuxBluetoothService : IBluetoothService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private readonly ICommandRunner _commandRunner;
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, byte> ManualDisconnects = new(StringComparer.OrdinalIgnoreCase);

    public static bool CanAutoConnect(string address) => !ManualDisconnects.ContainsKey(address)
        && (DisconnectMarker(address) is not { } marker || !File.Exists(marker));

    private static string? DisconnectMarker(string address)
    {
        if (!DeviceAddressRegex().IsMatch(address)) return null;
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return string.IsNullOrEmpty(runtime) ? null : Path.Combine(runtime, "daisyos-bluetooth", address + ".disconnected");
    }

    private void RememberConnectionChoice(string address, bool connected)
    {
        if (connected) ManualDisconnects.TryRemove(address, out _);
        else ManualDisconnects[address] = 0;
        // The standalone Settings app and shell must share explicit disconnects.
        // Runtime storage expires with the login session; test runners stay in memory.
        if (_commandRunner is not SafeCommandRunner || DisconnectMarker(address) is not { } marker) return;
        try
        {
            if (connected) File.Delete(marker);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
                File.WriteAllText(marker, "manual disconnect");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

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

                // Collect the unique (address, name) pairs first. BlueZ caches every
                // advertisement it has seen; anonymous BLE beacons (including the
                // rotating random addresses AirPods advertise from) carry no name and
                // BlueZ falls back to the address ("52-48-…"). Those are never
                // user-facing accessories, so drop them before querying details.
                var deviceEntries = new List<(string Address, string Name)>();
                foreach (var line in lines)
                {
                    var match = DeviceLineRegex().Match(line);
                    if (!match.Success || !seenAddresses.Add(match.Groups[1].Value)) continue;
                    var entryName = match.Groups[2].Value.Trim();
                    if (IsAnonymousName(entryName)) continue;
                    deviceEntries.Add((match.Groups[1].Value, entryName));
                }

                // Fetch all device info concurrently instead of sequentially — avoids
                // N × (50–150 ms) stall when many devices are paired.
                var infoTasks = deviceEntries.Select(entry =>
                    _commandRunner.RunAsync("bluetoothctl", ["--timeout", "2", "info", entry.Address], TimeSpan.FromSeconds(3), cancellationToken));

                var infoResults = await Task.WhenAll(infoTasks);

                for (var i = 0; i < deviceEntries.Count; i++)
                {
                    var (address, name) = deviceEntries[i];
                    var resultInfo = infoResults[i];

                    var isConnected = false;
                    var isPaired = false;
                    var isNearby = false;
                    var type = "Unknown";
                    var displayName = name;

                    if (resultInfo.Succeeded)
                    {
                        var info = resultInfo.StandardOutput;
                        isConnected = info.Contains("Connected: yes", StringComparison.OrdinalIgnoreCase);
                        isPaired = info.Contains("Paired: yes", StringComparison.OrdinalIgnoreCase)
                            || info.Contains("Bonded: yes", StringComparison.OrdinalIgnoreCase);
                        isNearby = info.Contains("RSSI:", StringComparison.Ordinal);
                        type = ParseDeviceType(info);
                        displayName = ParseInfoAlias(info) ?? ParseInfoName(info) ?? name;

                        // Unpaired devices are only offered while they are actually in range
                        // and advertise a real name; stale cache entries are hidden.
                        if (!isPaired && !isConnected && (!isNearby || IsAnonymousName(displayName))) continue;

                        // Paired accessories must be trusted, otherwise BlueZ refuses the
                        // device-initiated reconnect (opening the AirPods case, powering on
                        // a keyboard) and the user has to reconnect manually every time.
                        if (isPaired && info.Contains("Trusted: no", StringComparison.OrdinalIgnoreCase))
                            _ = _commandRunner.RunAsync("bluetoothctl", ["--timeout", "3", "trust", address], TimeSpan.FromSeconds(4), CancellationToken.None);
                    }
                    else if (IsAnonymousName(displayName))
                    {
                        continue;
                    }

                    devices.Add(new BluetoothDevice(address, displayName, isConnected, isPaired, type, isNearby));
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
            TimeSpan.FromSeconds(3),
            cancellationToken);
        if (!resultShow.Succeeded)
        {
            var serviceCheck = await _commandRunner.RunAsync("systemctl", ["is-active", "--quiet", "bluetooth.service"], TimeSpan.FromSeconds(1), cancellationToken);
            if (!serviceCheck.Succeeded)
            {
                await _commandRunner.RunAsync("systemctl", ["enable", "--now", "bluetooth.service", "--no-ask-password"], TimeSpan.FromSeconds(2), cancellationToken);
                await _commandRunner.RunAsync("systemctl", ["start", "bluetooth.service", "--no-ask-password"], TimeSpan.FromSeconds(2), cancellationToken);

                var recheck = await _commandRunner.RunAsync("systemctl", ["is-active", "--quiet", "bluetooth.service"], TimeSpan.FromSeconds(1), cancellationToken);
                if (recheck.Succeeded)
                {
                    resultShow = await _commandRunner.RunAsync(
                        "bluetoothctl",
                        ["--timeout", "2", "show"],
                        TimeSpan.FromSeconds(3),
                        cancellationToken);
                }
            }
        }

        if (!resultShow.Succeeded)
        {
            return new BluetoothStatus(true, false, false, Array.Empty<BluetoothDevice>(),
                "Bluetooth is turned off.");
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
        if (!enabled)
        {
            await _commandRunner.RunAsync("rfkill", ["block", "bluetooth"], CommandTimeout, cancellationToken);
            await _commandRunner.RunAsync(
                "bluetoothctl",
                ["power", "off"],
                CommandTimeout,
                cancellationToken);
            return true;
        }

        // 1. Unblock rfkill first (doesn't require root permissions)
        await _commandRunner.RunAsync("rfkill", ["unblock", "bluetooth"], CommandTimeout, cancellationToken);

        // 2. Check if bluetooth.service is active; if not, try to start and enable it
        var serviceCheck = await _commandRunner.RunAsync("systemctl", ["is-active", "--quiet", "bluetooth.service"], TimeSpan.FromSeconds(1), cancellationToken);
        if (!serviceCheck.Succeeded)
        {
            await _commandRunner.RunAsync("systemctl", ["enable", "--now", "bluetooth.service", "--no-ask-password"], TimeSpan.FromSeconds(2), cancellationToken);
            var startRes = await _commandRunner.RunAsync("systemctl", ["start", "bluetooth.service", "--no-ask-password"], TimeSpan.FromSeconds(2), cancellationToken);
            if (!startRes.Succeeded)
            {
                await _commandRunner.RunAsync("pkexec", ["systemctl", "start", "bluetooth.service"], TimeSpan.FromSeconds(5), cancellationToken);
            }
            await Task.Delay(300, cancellationToken);
        }

        var result = await _commandRunner.RunAsync(
            "bluetoothctl",
            ["power", "on"],
            CommandTimeout,
            cancellationToken);

        if (!result.Succeeded)
        {
            await Task.Delay(500, cancellationToken);
            result = await _commandRunner.RunAsync(
                "bluetoothctl",
                ["power", "on"],
                CommandTimeout,
                cancellationToken);
        }

        if (result.Succeeded)
        {
            _ = Task.Run(() => RestorePairedDevicesAsync(CancellationToken.None));
        }

        return result.Succeeded;
    }

    public async Task<bool> SetConnectedAsync(string address, bool connected, CancellationToken cancellationToken = default)
    {
        if (!DeviceAddressRegex().IsMatch(address)) return false;
        RememberConnectionChoice(address, connected);
        if (connected)
            await _commandRunner.RunAsync("bluetoothctl", ["--timeout", "3", "trust", address], TimeSpan.FromSeconds(4), cancellationToken);
        var result = await _commandRunner.RunAsync(
            "bluetoothctl",
            ["--timeout", "15", connected ? "connect" : "disconnect", address],
            TimeSpan.FromSeconds(16),
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

        if (!pairResult.Succeeded && !string.Concat(pairResult.StandardOutput, pairResult.StandardError)
                .Contains("AlreadyExists", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Trust device so it reconnects automatically
        var trustResult = await _commandRunner.RunAsync(
            "bluetoothctl",
            ["--timeout", "3", "trust", address],
            TimeSpan.FromSeconds(4),
            cancellationToken);

        if (!trustResult.Succeeded) return false;

        // Connect immediately after pairing and report the actual outcome.
        var connectResult = await _commandRunner.RunAsync(
            "bluetoothctl",
            ["--timeout", "10", "connect", address],
            TimeSpan.FromSeconds(11),
            cancellationToken);

        if (connectResult.Succeeded) RememberConnectionChoice(address, true);
        return connectResult.Succeeded;
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

    /// <summary>True for BlueZ's address-derived placeholder names such as "52-48-0A-11-9C-2F".</summary>
    public static bool IsAnonymousName(string? name) =>
        string.IsNullOrWhiteSpace(name) || AnonymousNameRegex().IsMatch(name.Trim());

    private static string? ParseInfoName(string info) => ParseInfoField(info, "Name:");
    private static string? ParseInfoAlias(string info) => ParseInfoField(info, "Alias:");

    private static string? ParseInfoField(string info, string field)
    {
        var line = info.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.StartsWith(field, StringComparison.Ordinal));
        var value = line?[field.Length..].Trim();
        return IsAnonymousName(value) ? null : value;
    }

    /// <summary>
    /// Trusts every paired device and reconnects the ones the user did not
    /// explicitly disconnect. Called once when the session starts so headphones,
    /// keyboards and mice used before come back without visiting Settings.
    /// </summary>
    public async Task RestorePairedDevicesAsync(CancellationToken cancellationToken = default)
    {
        var paired = await _commandRunner.RunAsync("bluetoothctl", ["--timeout", "2", "devices", "Paired"], CommandTimeout, cancellationToken);
        if (!paired.Succeeded) return;
        foreach (var line in paired.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = DeviceLineRegex().Match(line);
            if (!match.Success) continue;
            var address = match.Groups[1].Value;
            var info = await _commandRunner.RunAsync("bluetoothctl", ["--timeout", "2", "info", address], TimeSpan.FromSeconds(3), cancellationToken);
            if (!info.Succeeded) continue;
            if (info.StandardOutput.Contains("Trusted: no", StringComparison.OrdinalIgnoreCase))
                await _commandRunner.RunAsync("bluetoothctl", ["--timeout", "3", "trust", address], TimeSpan.FromSeconds(4), cancellationToken);
            if (info.StandardOutput.Contains("Connected: yes", StringComparison.OrdinalIgnoreCase) || !CanAutoConnect(address)) continue;
            // Short timeout: an accessory that is switched off or out of range
            // simply isn't connected; BlueZ will accept its own reconnect later.
            await _commandRunner.RunAsync("bluetoothctl", ["--timeout", "6", "connect", address], TimeSpan.FromSeconds(7), cancellationToken);
        }
    }

    private static bool ParseShowOutput(string output)
    {
        return output.Contains("Powered: yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string ParseDeviceType(string output)
    {
        if (output.Contains("74ec2172-0bad-4d01-8f77-997b2be0722a", StringComparison.OrdinalIgnoreCase)) return "AirPods";
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

    [GeneratedRegex(@"^([0-9A-Fa-f]{2}([-:_][0-9A-Fa-f]{2}){5}|[0-9A-Fa-f]{12})$")]
    private static partial Regex AnonymousNameRegex();
}
