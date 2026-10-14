using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IBluetoothService
{
    Task<BluetoothStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<BluetoothStatus> GetAdapterStatusAsync(CancellationToken cancellationToken = default);

    Task<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    Task<bool> SetConnectedAsync(string address, bool connected, CancellationToken cancellationToken = default);

    Task<bool> StartDiscoveryAsync(CancellationToken cancellationToken = default);

    Task<bool> StopDiscoveryAsync(CancellationToken cancellationToken = default);

    Task<bool> PairDeviceAsync(string address, CancellationToken cancellationToken = default);

    Task<bool> ForgetDeviceAsync(string address, CancellationToken cancellationToken = default);
}
