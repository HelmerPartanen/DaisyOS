using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Networking;

public sealed class LinuxNetworkStatusService : INetworkStatusService
{
    private readonly ICommandRunner _commandRunner;

    public LinuxNetworkStatusService(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<NetworkStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            "nmcli",
            ["-t", "-f", "DEVICE,TYPE,STATE,CONNECTION", "device", "status"],
            TimeSpan.FromSeconds(2),
            cancellationToken);

        if (!result.Succeeded)
        {
            return new NetworkStatus(
                false,
                "Network unavailable",
                "Couldn't read the network status. Try again in a moment.",
                IsAvailable: false);
        }

        var connectivity = await GetConnectivityAsync(cancellationToken);

        var output = result.StandardOutput.Trim();
        var activeDevice = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseDeviceStatusLine)
            .Where(device => device.IsConnected)
            .OrderBy(device => device.Kind == NetworkConnectionKind.Ethernet ? 0 : 1)
            .FirstOrDefault();

        if (activeDevice is null)
        {
            return new NetworkStatus(
                false,
                "Offline",
                "No active network connection was found.",
                Connectivity: connectivity);
        }

        var signalPercent = activeDevice.Kind == NetworkConnectionKind.WiFi
            ? await GetActiveWifiSignalAsync(cancellationToken)
            : null;

        var detail = activeDevice.Kind switch
        {
            NetworkConnectionKind.WiFi => "Wi-Fi is connected.",
            NetworkConnectionKind.Ethernet => "Ethernet is connected.",
            _ => "A network connection is active."
        };
        return new NetworkStatus(
            true,
            activeDevice.DisplayName,
            detail,
            activeDevice.Kind,
            connectivity,
            signalPercent);
    }

    private async Task<NetworkConnectivity> GetConnectivityAsync(CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            "nmcli",
            ["-t", "-f", "CONNECTIVITY", "general"],
            TimeSpan.FromSeconds(3),
            cancellationToken);

        if (!result.Succeeded)
        {
            return NetworkConnectivity.Unknown;
        }

        return result.StandardOutput.Trim().ToLowerInvariant() switch
        {
            "full" => NetworkConnectivity.Full,
            "limited" => NetworkConnectivity.Limited,
            "portal" => NetworkConnectivity.Portal,
            "none" => NetworkConnectivity.None,
            _ => NetworkConnectivity.Unknown
        };
    }

    private async Task<int?> GetActiveWifiSignalAsync(CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            "nmcli",
            ["-t", "-f", "IN-USE,SIGNAL", "device", "wifi", "list", "--rescan", "no"],
            TimeSpan.FromSeconds(3),
            cancellationToken);
        if (!result.Succeeded)
        {
            return null;
        }

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.LastIndexOf(':');
            if (separator < 0 || line[..separator] != "*")
            {
                continue;
            }

            if (int.TryParse(line[(separator + 1)..], out var signal))
            {
                return Math.Clamp(signal, 0, 100);
            }
        }

        return null;
    }

    private static NetworkDeviceStatus ParseDeviceStatusLine(string line)
    {
        var parts = line.Split(':');
        var type = parts.Length > 1 ? parts[1] : string.Empty;
        var state = parts.Length > 2 ? parts[2] : string.Empty;
        var connection = parts.Length > 3 ? parts[3] : string.Empty;
        var kind = type.Equals("wifi", StringComparison.OrdinalIgnoreCase)
            ? NetworkConnectionKind.WiFi
            : type.Equals("ethernet", StringComparison.OrdinalIgnoreCase)
                ? NetworkConnectionKind.Ethernet
                : NetworkConnectionKind.Unknown;

        var isConnected = state.Contains("connected", StringComparison.OrdinalIgnoreCase)
            && !state.Contains("disconnected", StringComparison.OrdinalIgnoreCase);
        var displayName = !string.IsNullOrWhiteSpace(connection) && connection != "--"
            ? connection
            : kind switch
            {
                NetworkConnectionKind.WiFi => "Wi-Fi",
                NetworkConnectionKind.Ethernet => "Ethernet",
                _ => "Connected"
            };

        return new NetworkDeviceStatus(isConnected, kind, displayName);
    }

    private sealed record NetworkDeviceStatus(bool IsConnected, NetworkConnectionKind Kind, string DisplayName);
}
