using System;
using System.Linq;
using Avalonia.Input;
using Avalonia.Input.Raw;

namespace Avalonia.Wayland;

internal static class WaylandKeyboardInput
{
    // Initial presses and repeat ticks must produce the same text events.
    // Handled shortcuts and control sequences (Escape, Backspace, etc.) are
    // key events only; sending their symbols as text inserts unwanted glyphs.
    internal static void DispatchText(RawKeyEventArgs args, Action<RawInputEventArgs>? input)
    {
        if (args.Device is IKeyboardDevice keyboard && !args.Handled && args.Type == RawKeyEventType.KeyDown &&
            args.KeySymbol is { Length: > 0 } text && text.All(character => !char.IsControl(character)))
            input?.Invoke(new RawTextInputEventArgs(keyboard, args.Timestamp, args.Root, text));
    }
}
