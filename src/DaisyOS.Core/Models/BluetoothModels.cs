namespace DaisyOS.Core.Models;

public sealed record BluetoothDevice(
    string Address,
    string Name,
    bool IsConnected,
    bool IsPaired,
    string Type = "Unknown");

public sealed record BluetoothStatus(
    bool IsAvailable,
    bool IsEnabled,
    bool IsDiscovering,
    IReadOnlyList<BluetoothDevice> Devices,
    string Detail);
