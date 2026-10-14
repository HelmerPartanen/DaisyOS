using System.Text.Json.Serialization;

namespace DaisyOS.Core.Models;

/// <summary>
/// Configuration for default and user-customized controller keybindings in DaisyOS.
/// Loaded from ~/.config/daisyos/controller_keybindings.json.
/// </summary>
public sealed class ControllerKeybindingsConfig
{
    public Dictionary<int, string> XboxButtons { get; set; } = new()
    {
        [0] = "Confirm",         // A (Bottom)
        [1] = "Back",            // B (Right)
        [2] = "QuickSettings",   // X (Left)
        [3] = "Details",         // Y (Top)
        [4] = "PreviousSection", // LB
        [5] = "NextSection",     // RB
        [10] = "OpenConsole"     // Guide Button ONLY
    };

    public Dictionary<int, string> PlayStationButtons { get; set; } = new()
    {
        [0] = "Confirm",         // Cross ✕ (Bottom)
        [1] = "Back",            // Circle ○ (Right)
        [3] = "QuickSettings",   // Square ▫ (Left)
        [2] = "Details",         // Triangle △ (Top)
        [4] = "PreviousSection", // L1
        [5] = "NextSection",     // R1
        [10] = "OpenConsole"     // PS Button ONLY
    };

    public Dictionary<ushort, string> EvdevKeys { get; set; } = new()
    {
        [0x130] = "Confirm",         // BTN_SOUTH / A / Cross
        [0x131] = "Back",            // BTN_EAST / B / Circle
        [0x133] = "QuickSettings",   // BTN_WEST / X / Square
        [0x134] = "Details",         // BTN_NORTH / Y / Triangle
        [0x136] = "PreviousSection", // BTN_TL / LB / L1
        [0x137] = "NextSection",     // BTN_TR / RB / R1
        [0x13c] = "OpenConsole",     // BTN_MODE / Guide / PS
        [0x13d] = "OpenConsole"
    };
}
