namespace DaisyOS.Core.Models;

public sealed record WirelessNetworkStatus(
    bool IsAvailable,
    IReadOnlyList<WirelessNetworkInfo> Networks,
    string Detail);

public sealed record WirelessNetworkInfo(
    string Ssid,
    int SignalPercent,
    string Security,
    bool IsActive);

/// <summary>Fast adapter/radio state used by the shell before an on-demand network scan.</summary>
public sealed record WirelessRadioStatus(bool IsAvailable, bool IsEnabled, string Detail);
