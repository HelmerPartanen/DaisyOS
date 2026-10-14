namespace DaisyOS.Core.Models;

/// <summary>
/// Contains raw button/axis event information from physical controller hardware.
/// </summary>
public sealed record RawControllerInputEventArgs(
    byte EventType,
    byte Number,
    short Value,
    ushort EvdevCode = 0);
