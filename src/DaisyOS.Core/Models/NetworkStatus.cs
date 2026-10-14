namespace DaisyOS.Core.Models;

public enum NetworkConnectionKind
{
    Unknown,
    WiFi,
    Ethernet
}

/// <summary>NetworkManager's view of whether the active connection can reach the internet.</summary>
public enum NetworkConnectivity
{
    Unknown,
    Full,
    Limited,
    Portal,
    None
}

public sealed record NetworkStatus(
    bool IsConnected,
    string DisplayName,
    string Detail,
    NetworkConnectionKind ConnectionKind = NetworkConnectionKind.Unknown,
    NetworkConnectivity Connectivity = NetworkConnectivity.Unknown,
    int? SignalPercent = null,
    bool IsAvailable = true);
