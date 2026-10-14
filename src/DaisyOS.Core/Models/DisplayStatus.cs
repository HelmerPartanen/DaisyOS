namespace DaisyOS.Core.Models;

public sealed record DisplayStatus(
    bool IsAvailable,
    string PrimaryDisplay,
    string Resolution,
    int ConnectedDisplayCount,
    string Detail);
