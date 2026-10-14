using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IWirelessNetworkService
{
    Task<WirelessNetworkStatus> GetNetworksAsync(CancellationToken cancellationToken = default);

    Task<WirelessRadioStatus> GetRadioStatusAsync(CancellationToken cancellationToken = default);

    Task<bool> ConnectToNetworkAsync(string ssid, string? password = null, CancellationToken cancellationToken = default);

    Task<bool> DisconnectFromNetworkAsync(CancellationToken cancellationToken = default);

    Task<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
