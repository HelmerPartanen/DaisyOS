namespace DaisyOS.Core.Models;

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    string IconGlyph,
    bool IsDefault,
    bool IsBluetooth = false)
{
    public string? Status { get; init; }
}
