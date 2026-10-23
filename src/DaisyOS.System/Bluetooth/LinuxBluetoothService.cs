using System.Diagnostics;
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

        // "devices" is BlueZ's transient cache, while "devices Paired" is its
        // persistent bond list. Some BlueZ versions omit powered-off or long-idle
        // accessories from the former, which made previously paired devices vanish
        // from Settings. Always query the bond list, even while the radio is off, and
        // merge nearby advertisements into it while the radio is on.
        var pairedTask = _commandRunner.RunAsync(
            "bluetoothctl", ["--timeout", "2", "devices", "Paired"], TimeSpan.FromSeconds(3), cancellationToken);
        Task<CommandResult>? nearbyTask = isEnabled
            ? _commandRunner.RunAsync("bluetoothctl", ["--timeout", "2", "devices"], TimeSpan.FromSeconds(3), cancellationToken)
            : null;
        if (nearbyTask is not null) await Task.WhenAll(pairedTask, nearbyTask);
        var pairedResult = await pairedTask;
        var nearbyResult = nearbyTask is null ? null : await nearbyTask;
        var deviceQueryFailed = !pairedResult.Succeeded && (nearbyResult is null || !nearbyResult.Succeeded);

        var entries = new Dictionary<string, (string Name, bool ListedAsPaired)>(StringComparer.OrdinalIgnoreCase);
        AddDeviceEntries(pairedResult, listedAsPaired: true, entries);
        if (nearbyResult is not null) AddDeviceEntries(nearbyResult, listedAsPaired: false, entries);

        var deviceEntries = entries.Select(entry => (Address: entry.Key, entry.Value.Name, entry.Value.ListedAsPaired)).ToArray();
        var infoTasks = deviceEntries.Select(entry =>
            _commandRunner.RunAsync("bluetoothctl", ["--timeout", "2", "info", entry.Address], TimeSpan.FromSeconds(3), cancellationToken));
        var infoResults = await Task.WhenAll(infoTasks);

        for (var i = 0; i < deviceEntries.Length; i++)
        {
            var (address, name, listedAsPaired) = deviceEntries[i];
            var resultInfo = infoResults[i];
            var isConnected = false;
            var isPaired = listedAsPaired;
            var isNearby = false;
            var type = "Unknown";
            var displayName = name;

            if (resultInfo.Succeeded)
            {
                var info = resultInfo.StandardOutput;
                isConnected = info.Contains("Connected: yes", StringComparison.OrdinalIgnoreCase);
                isPaired |= info.Contains("Paired: yes", StringComparison.OrdinalIgnoreCase)
                    || info.Contains("Bonded: yes", StringComparison.OrdinalIgnoreCase);
                isNearby = info.Contains("RSSI:", StringComparison.Ordinal);
                type = ParseDeviceType(info);
                displayName = ParseInfoAlias(info) ?? ParseInfoName(info) ?? name;

                // BlueZ retains stale advertisements. Only bonds and devices with a
                // current RSSI belong in a user-facing picker.
                if (!isPaired && !isConnected && (!isNearby || IsAnonymousName(displayName))) continue;

                if (isPaired && info.Contains("Trusted: no", StringComparison.OrdinalIgnoreCase))
                    _ = _commandRunner.RunAsync("bluetoothctl", ["--timeout", "3", "trust", address], TimeSpan.FromSeconds(4), CancellationToken.None);
            }
            else if (!isPaired || IsAnonymousName(displayName))
            {
                continue;
            }

            devices.Add(new BluetoothDevice(address, displayName, isConnected, isPaired, type, isNearby));
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
            var detail = string.Concat(resultShow.StandardOutput, " ", resultShow.StandardError).Trim();
            return new BluetoothStatus(false, false, false, Array.Empty<BluetoothDevice>(),
                detail.Contains("No default controller", StringComparison.OrdinalIgnoreCase)
                    ? "Bluetooth isn’t available on this device."
                    : BuildFailureDetail(resultShow, "Bluetooth status"));
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
        var result = await _commandRunner.RunAsync(
            "bluetoothctl",
            ["power", enabled ? "on" : "off"],
            CommandTimeout,
            cancellationToken);

        // A soft rfkill is the common recoverable failure. Do not invoke privileged
        // service management from a toggle; the session service owns daemon startup.
        if (enabled && !result.Succeeded && string.Concat(result.StandardOutput, result.StandardError)
                .Contains("Blocked", StringComparison.OrdinalIgnoreCase))
        {
            await _commandRunner.RunAsync("rfkill", ["unblock", "bluetooth"], CommandTimeout, cancellationToken);
            result = await _commandRunner.RunAsync(
                "bluetoothctl", ["power", "on"], CommandTimeout, cancellationToken);
        }

        if (enabled && result.Succeeded)
        {
            _ = Task.Run(() => RestorePairedDevicesAsync(CancellationToken.None));
        }

        return result.Succeeded;
    }

    public async Task<bool> SetConnectedAsync(string address, bool connected, CancellationToken cancellationToken = default)
    {
        if (!DeviceAddressRegex().IsMatch(address)) return false;
        if (connected)
            await _commandRunner.RunAsync("bluetoothctl", ["--timeout", "3", "trust", address], TimeSpan.FromSeconds(4), cancellationToken);
        await _commandRunner.RunAsync(
            "bluetoothctl",
            ["--timeout", "15", connected ? "connect" : "disconnect", address],
            TimeSpan.FromSeconds(16),
            cancellationToken);
        var reachedRequestedState = await HasConnectionStateAsync(address, connected, cancellationToken);
        if (reachedRequestedState) RememberConnectionChoice(address, connected);
        return reachedRequestedState;
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

        // BlueZ commonly leaves the controller non-pairable outside a settings
        // session. Registering a NoInputNoOutput agent is also required for
        // JustWorks accessories such as mice, which otherwise fail authentication.
        var pairableResult = await _commandRunner.RunAsync(
            "bluetoothctl", ["--timeout", "3", "pairable", "on"], TimeSpan.FromSeconds(4), cancellationToken);
        if (!pairableResult.Succeeded) return false;

        await using var pairingAgent = _commandRunner is SafeCommandRunner
            ? await BluetoothPairingAgentSession.StartAsync(cancellationToken)
            : null;
        if (_commandRunner is SafeCommandRunner && pairingAgent is null)
        {
            await _commandRunner.RunAsync(
                "bluetoothctl", ["--timeout", "3", "pairable", "off"], TimeSpan.FromSeconds(4), CancellationToken.None);
            return false;
        }

        try
        {
            // The process that owns the agent must also issue Pair(). Starting a
            // second bluetoothctl instance makes that instance temporarily replace
            // the default agent; when it exits, delayed BLE confirmation and HID
            // service setup fail with "No agent available" even though BlueZ may
            // still report Connected=yes.
            var pairingSucceeded = pairingAgent is not null
                ? await pairingAgent.PairAsync(address, cancellationToken)
                : await PairWithOneShotAgentAsync(address, cancellationToken);

            if (!pairingSucceeded)
            {
                return false;
            }

            // Trust device so it reconnects automatically.
            var trustResult = await _commandRunner.RunAsync(
                "bluetoothctl",
                ["--timeout", "3", "trust", address],
                TimeSpan.FromSeconds(4),
                cancellationToken);

            if (!trustResult.Succeeded) return false;

            // HID devices are usually connected by the pair operation itself.
            // Avoid a redundant connect request, but issue one when BlueZ only
            // created the bond. In both cases verify the actual device state.
            if (!await HasConnectionStateAsync(address, connected: true, cancellationToken))
            {
                await _commandRunner.RunAsync(
                    "bluetoothctl",
                    ["--timeout", "15", "connect", address],
                    TimeSpan.FromSeconds(16),
                    cancellationToken);
            }

            var connected = await HasConnectionStateAsync(address, connected: true, cancellationToken);
            if (connected) RememberConnectionChoice(address, true);
            return connected;
        }
        finally
        {
            // Pairability is only needed while the user is pairing an accessory.
            await _commandRunner.RunAsync(
                "bluetoothctl", ["--timeout", "3", "pairable", "off"], TimeSpan.FromSeconds(4), CancellationToken.None);
        }
    }

    private async Task<bool> HasConnectionStateAsync(string address, bool connected, CancellationToken cancellationToken)
    {
        var info = await _commandRunner.RunAsync(
            "bluetoothctl", ["--timeout", "3", "info", address], TimeSpan.FromSeconds(4), cancellationToken);
        if (!info.Succeeded) return false;
        var isConnected = info.StandardOutput.Contains("Connected: yes", StringComparison.OrdinalIgnoreCase);
        return isConnected == connected;
    }

    private async Task<bool> PairWithOneShotAgentAsync(string address, CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            "bluetoothctl",
            ["--agent", "NoInputNoOutput", "--timeout", "20", "pair", address],
            TimeSpan.FromSeconds(21),
            cancellationToken);
        return result.Succeeded || string.Concat(result.StandardOutput, result.StandardError)
            .Contains("AlreadyExists", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Keeps a real BlueZ agent registered for the complete pairing handshake.
    /// A one-shot `bluetoothctl --agent ... pair` can unregister its agent while
    /// a BLE HID device is still confirming its passkey, producing BlueZ's
    /// "No agent available for request type 2" failure.
    /// </summary>
    private sealed class BluetoothPairingAgentSession : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly object _outputLock = new();
        private TaskCompletionSource<bool>? _pairingCompletion;

        private BluetoothPairingAgentSession(Process process) => _process = process;

        public static async Task<BluetoothPairingAgentSession?> StartAsync(CancellationToken cancellationToken)
        {
            var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var madeDefault = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var startInfo = new ProcessStartInfo
            {
                FileName = "bluetoothctl",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--agent");
            startInfo.ArgumentList.Add("NoInputNoOutput");
            startInfo.Environment["LC_ALL"] = "C.UTF-8";
            startInfo.Environment["LANGUAGE"] = "C";

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var session = new BluetoothPairingAgentSession(process);
            DataReceivedEventHandler receive = (_, args) =>
            {
                var line = args.Data;
                if (line?.Contains("Agent registered", StringComparison.OrdinalIgnoreCase) == true)
                    registered.TrySetResult(true);
                if (line?.Contains("Default agent request successful", StringComparison.OrdinalIgnoreCase) == true)
                    madeDefault.TrySetResult(true);
                if (line is null) return;

                lock (session._outputLock)
                {
                    var pairing = session._pairingCompletion;
                    if (pairing is null) return;
                    if (line.Contains("Pairing successful", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Paired: yes", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("AlreadyExists", StringComparison.OrdinalIgnoreCase))
                        pairing.TrySetResult(true);
                    else if (line.Contains("Failed to pair", StringComparison.OrdinalIgnoreCase)
                             || line.Contains("AuthenticationFailed", StringComparison.OrdinalIgnoreCase)
                             || line.Contains("AuthenticationCanceled", StringComparison.OrdinalIgnoreCase)
                             || line.Contains("AuthenticationRejected", StringComparison.OrdinalIgnoreCase))
                        pairing.TrySetResult(false);
                }
            };
            process.OutputDataReceived += receive;
            process.ErrorDataReceived += receive;
            process.Exited += (_, _) =>
            {
                registered.TrySetResult(false);
                madeDefault.TrySetResult(false);
            };

            try
            {
                if (!process.Start()) { process.Dispose(); return null; }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                // Keep stdin open for the lifetime of the pairing operation.
                // Pairing will be issued through this same process, so it retains
                // agent ownership until BlueZ finishes the complete handshake.
                if (!await registered.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken))
                {
                    await StopAsync(process);
                    return null;
                }
                await process.StandardInput.WriteLineAsync("default-agent".AsMemory(), cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
                if (!await madeDefault.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken))
                {
                    await StopAsync(process);
                    return null;
                }
                return session;
            }
            catch (OperationCanceledException)
            {
                await StopAsync(process);
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception or IOException or TimeoutException)
            {
                await StopAsync(process);
                return null;
            }
        }

        public async Task<bool> PairAsync(string address, CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> completion;
            lock (_outputLock)
            {
                if (_pairingCompletion is not null)
                    throw new InvalidOperationException("A Bluetooth pairing operation is already active.");
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pairingCompletion = completion;
            }

            try
            {
                await _process.StandardInput.WriteLineAsync($"pair {address}".AsMemory(), cancellationToken);
                await _process.StandardInput.FlushAsync(cancellationToken);
                return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            catch (TimeoutException)
            {
                return false;
            }
            finally
            {
                lock (_outputLock) _pairingCompletion = null;
            }
        }

        public async ValueTask DisposeAsync() => await StopAsync(_process);

        private static async Task StopAsync(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    await process.StandardInput.WriteLineAsync("agent off");
                    await process.StandardInput.WriteLineAsync("quit");
                    process.StandardInput.Close();
                    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await process.WaitForExitAsync(stop.Token);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or OperationCanceledException) { }
            finally
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception) { }
                process.Dispose();
            }
        }
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

    private static void AddDeviceEntries(
        CommandResult result,
        bool listedAsPaired,
        IDictionary<string, (string Name, bool ListedAsPaired)> entries)
    {
        if (!result.Succeeded) return;
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = DeviceLineRegex().Match(line);
            if (!match.Success) continue;
            var address = match.Groups[1].Value;
            var name = match.Groups[2].Value.Trim();
            if (IsAnonymousName(name)) continue;
            if (entries.TryGetValue(address, out var existing))
                entries[address] = (existing.Name, existing.ListedAsPaired || listedAsPaired);
            else
                entries[address] = (name, listedAsPaired);
        }
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
