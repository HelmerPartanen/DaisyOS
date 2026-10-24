using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Processes;
using System.Globalization;

namespace DaisyOS.System.Networking;

public sealed class LinuxWirelessNetworkService : IWirelessNetworkService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(8);
    private readonly ICommandRunner _commandRunner;

    public LinuxWirelessNetworkService(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<WirelessNetworkStatus> GetNetworksAsync(CancellationToken cancellationToken = default)
    {
        if (_commandRunner is SafeCommandRunner && !HasWirelessHardware())
        {
            return new WirelessNetworkStatus(
                false,
                Array.Empty<WirelessNetworkInfo>(),
                "Wi-Fi isn’t available on this device.");
        }

        var deviceResult = await _commandRunner.RunAsync(
            "nmcli",
            ["-t", "-f", "DEVICE,TYPE", "device", "status"],
            CommandTimeout,
            cancellationToken);

        if (!deviceResult.Succeeded)
        {
            return new WirelessNetworkStatus(
                false,
                Array.Empty<WirelessNetworkInfo>(),
                BuildFailureDetail(deviceResult, "Wi-Fi status"));
        }

        var hasWirelessAdapter = deviceResult.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line =>
            {
                var fields = SplitEscapedFields(line);
                return fields.Count > 1 && fields[1].Equals("wifi", StringComparison.OrdinalIgnoreCase);
            });
        if (!hasWirelessAdapter)
        {
            return new WirelessNetworkStatus(
                false,
                Array.Empty<WirelessNetworkInfo>(),
                "Wi-Fi isn’t available on this device.");
        }

        var knownTask = _commandRunner.RunAsync(
            "nmcli",
            ["-t", "-f", "NAME,TYPE", "connection", "show"],
            CommandTimeout,
            cancellationToken);
        var networksTask = _commandRunner.RunAsync(
            "nmcli",
            ["-t", "-f", "IN-USE,SSID,SECURITY,SIGNAL", "device", "wifi", "list", "--rescan", "no"],
            CommandTimeout,
            cancellationToken);
        await Task.WhenAll(knownTask, networksTask);
        var knownNetworks = ParseKnownNetworks(await knownTask);
        var result = await networksTask;

        if (!result.Succeeded)
        {
            return new WirelessNetworkStatus(
                false,
                Array.Empty<WirelessNetworkInfo>(),
                BuildFailureDetail(result, "Wi-Fi network scan"));
        }

        var visibleNetworks = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseNetworkLine)
            .Where(network => network is not null)
            .Cast<WirelessNetworkInfo>()
            .GroupBy(network => string.IsNullOrWhiteSpace(network.Ssid) ? "<hidden>" : network.Ssid, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(network => network.IsActive).ThenByDescending(network => network.SignalPercent).First())
            .Select(network => network with { IsKnown = knownNetworks.Contains(network.Ssid) })
            .ToArray();
        var visibleNames = visibleNetworks.Select(network => network.Ssid).ToHashSet(StringComparer.Ordinal);
        var networks = visibleNetworks
            .Concat(knownNetworks.Where(name => !visibleNames.Contains(name))
                .Select(name => new WirelessNetworkInfo(name, 0, string.Empty, false, IsKnown: true, IsAvailable: false)))
            .OrderByDescending(network => network.IsActive)
            .ThenByDescending(network => network.IsKnown)
            .ThenByDescending(network => network.IsAvailable)
            .ThenByDescending(network => network.SignalPercent)
            .ThenBy(network => network.Ssid, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var detailText = networks.Length == 0
            ? "No Wi-Fi networks found."
            : $"{networks.Length} Wi-Fi {(networks.Length == 1 ? "network" : "networks")} found.";
        return new WirelessNetworkStatus(true, networks, detailText);
    }

    public async Task RequestScanAsync(CancellationToken cancellationToken = default)
    {
        // Keep discovery separate from reading NetworkManager's cached access
        // points. Callers can render immediately, then request fresh results
        // without blocking or blanking their current list.
        await _commandRunner.RunAsync(
            "nmcli",
            ["device", "wifi", "rescan"],
            CommandTimeout,
            cancellationToken);
    }

    private static HashSet<string> ParseKnownNetworks(CommandResult result)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (!result.Succeeded) return known;
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = SplitEscapedFields(line);
            if (fields.Count < 2 || fields[0].Length == 0) continue;
            if (fields[1].Equals("802-11-wireless", StringComparison.OrdinalIgnoreCase) ||
                fields[1].Equals("wifi", StringComparison.OrdinalIgnoreCase) ||
                fields[1].Equals("wireless", StringComparison.OrdinalIgnoreCase))
            {
                known.Add(fields[0]);
            }
        }
        return known;
    }

    public async Task<WirelessRadioStatus> GetRadioStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_commandRunner is SafeCommandRunner && !HasWirelessHardware())
        {
            return new WirelessRadioStatus(false, false, "Wi-Fi isn’t available on this device.");
        }

        var deviceResult = await _commandRunner.RunAsync(
            "nmcli",
            ["-t", "-f", "DEVICE,TYPE", "device", "status"],
            TimeSpan.FromSeconds(2),
            cancellationToken);
        if (!deviceResult.Succeeded)
        {
            return new WirelessRadioStatus(false, false, BuildFailureDetail(deviceResult, "Wi-Fi status"));
        }

        var available = deviceResult.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SplitEscapedFields)
            .Any(fields => fields.Count > 1 && fields[1].Equals("wifi", StringComparison.OrdinalIgnoreCase));
        if (!available) return new WirelessRadioStatus(false, false, "Wi-Fi isn’t available on this device.");

        var radioResult = await _commandRunner.RunAsync(
            "nmcli",
            ["-t", "-f", "WIFI", "general"],
            TimeSpan.FromSeconds(2),
            cancellationToken);
        if (!radioResult.Succeeded)
        {
            return new WirelessRadioStatus(true, false, BuildFailureDetail(radioResult, "Wi-Fi radio status"));
        }

        var isEnabled = radioResult.StandardOutput.Trim().Equals("enabled", StringComparison.OrdinalIgnoreCase);
        return new WirelessRadioStatus(true, isEnabled, isEnabled ? "Toggle Wi-Fi" : "Wi-Fi is turned off.");
    }

    private static bool HasWirelessHardware()
    {
        try
        {
            if (Directory.Exists("/sys/class/net"))
            {
                foreach (var dir in Directory.EnumerateDirectories("/sys/class/net"))
                {
                    if (Directory.Exists(Path.Combine(dir, "wireless")) ||
                        Directory.Exists(Path.Combine(dir, "phy80211")))
                    {
                        return true;
                    }
                }
            }

            if (Directory.Exists("/sys/class/rfkill"))
            {
                foreach (var dir in Directory.EnumerateDirectories("/sys/class/rfkill"))
                {
                    var typeFile = Path.Combine(dir, "type");
                    if (File.Exists(typeFile) && File.ReadAllText(typeFile).Trim().Equals("wlan", StringComparison.OrdinalIgnoreCase))
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

    private static WirelessNetworkInfo? ParseNetworkLine(string line)
    {
        var fields = SplitEscapedFields(line);
        if (fields.Count < 4)
        {
            return null;
        }

        var isActive = fields[0] == "*";
        var ssid = string.IsNullOrWhiteSpace(fields[1]) ? "Hidden network" : fields[1];
        var security = string.IsNullOrWhiteSpace(fields[2]) ? "Open" : fields[2];
        var signal = int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSignal)
            ? Math.Clamp(parsedSignal, 0, 100)
            : 0;

        return new WirelessNetworkInfo(ssid, signal, security, isActive);
    }

    private static IReadOnlyList<string> SplitEscapedFields(string line)
    {
        var fields = new List<string>();
        var current = new List<char>();
        var isEscaped = false;

        foreach (var character in line)
        {
            if (isEscaped)
            {
                current.Add(character);
                isEscaped = false;
                continue;
            }

            if (character == '\\')
            {
                isEscaped = true;
                continue;
            }

            if (character == ':')
            {
                fields.Add(new string(current.ToArray()));
                current.Clear();
                continue;
            }

            current.Add(character);
        }

        if (isEscaped)
        {
            current.Add('\\');
        }

        fields.Add(new string(current.ToArray()));
        return fields;
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

    public async Task<bool> ConnectToNetworkAsync(string ssid, string? password = null, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "device", "wifi", "connect", ssid };
        if (!string.IsNullOrEmpty(password))
        {
            arguments.Add("password");
            arguments.Add(password);
        }

        var result = await _commandRunner.RunAsync(
            "nmcli",
            arguments,
            CommandTimeout,
            cancellationToken);

        return result.Succeeded;
    }

    public async Task<bool> DisconnectFromNetworkAsync(CancellationToken cancellationToken = default)
    {
        var resultDevice = await _commandRunner.RunAsync(
            "nmcli",
            ["-t", "-f", "DEVICE,TYPE,STATE", "device"],
            CommandTimeout,
            cancellationToken);

        if (!resultDevice.Succeeded)
        {
            return false;
        }

        var wifiDevices = resultDevice.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SplitEscapedFields)
            .Where(parts => parts.Count >= 3
                && parts[1].Equals("wifi", StringComparison.OrdinalIgnoreCase)
                && parts[2].Equals("connected", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[0])
            .ToArray();

        if (wifiDevices.Length == 0)
        {
            return true;
        }

        var success = true;
        foreach (var device in wifiDevices)
        {
            var disconnectResult = await _commandRunner.RunAsync(
                "nmcli",
                ["device", "disconnect", device],
                CommandTimeout,
                cancellationToken);
            if (!disconnectResult.Succeeded)
            {
                success = false;
            }
        }

        return success;
    }

    public async Task<bool> ForgetNetworkAsync(string ssid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ssid)) return false;
        var result = await _commandRunner.RunAsync(
            "nmcli",
            ["connection", "delete", "id", ssid],
            CommandTimeout,
            cancellationToken);
        return result.Succeeded;
    }

    public async Task<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            "nmcli",
            ["radio", "wifi", enabled ? "on" : "off"],
            CommandTimeout,
            cancellationToken);
        return result.Succeeded;
    }
}
