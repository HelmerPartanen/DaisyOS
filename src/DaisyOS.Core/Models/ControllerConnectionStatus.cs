namespace DaisyOS.Core.Models;

public enum ControllerType
{
    Generic,
    Xbox,
    PlayStation
}

/// <summary>Describes a game controller that is currently exposed to the Linux input stack.</summary>
public sealed record ControllerConnectionStatus(
    bool IsConnected,
    string? Name,
    string? DevicePath,
    string? JoystickPath = null)
{
    public static ControllerConnectionStatus Disconnected { get; } = new(false, null, null);

    public ControllerType Type => DetectType(Name);

    public static ControllerType DetectType(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return ControllerType.Generic;

        var lowerName = name.ToLowerInvariant();

        if (lowerName.Contains("sony") ||
            lowerName.Contains("dualsense") ||
            lowerName.Contains("dualshock") ||
            lowerName.Contains("playstation") ||
            lowerName.Contains("ps5") ||
            lowerName.Contains("ps4") ||
            lowerName.Contains("ps3") ||
            lowerName.Contains("054c"))
        {
            return ControllerType.PlayStation;
        }

        if (lowerName.Contains("xbox") ||
            lowerName.Contains("x-box") ||
            lowerName.Contains("xinput") ||
            lowerName.Contains("microsoft") ||
            lowerName.Contains("045e"))
        {
            return ControllerType.Xbox;
        }

        return ControllerType.Generic;
    }
}
