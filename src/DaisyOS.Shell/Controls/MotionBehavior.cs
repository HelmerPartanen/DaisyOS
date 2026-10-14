using Avalonia;
using Avalonia.Controls;
using DaisyOS.Shell.Services.Motion;

namespace DaisyOS.Shell.Controls;

/// <summary>Reveals popup contents after attachment, without moving the native popup frame.</summary>
public sealed class MotionBehavior : AvaloniaObject
{
    public static readonly AttachedProperty<bool> RevealOnAttachProperty =
        AvaloniaProperty.RegisterAttached<MotionBehavior, Control, bool>("RevealOnAttach");

    static MotionBehavior()
    {
        RevealOnAttachProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            if (change.NewValue is not true) return;
            control.AttachedToVisualTree += (_, _) => { if (GetRevealOnAttach(control)) _ = GuiMotion.RevealAsync(control, 4); };
            control.DetachedFromVisualTree += (_, _) => GuiMotion.Cancel(control);
        });
    }
    public static bool GetRevealOnAttach(AvaloniaObject target) => target.GetValue(RevealOnAttachProperty);
    public static void SetRevealOnAttach(AvaloniaObject target, bool value) => target.SetValue(RevealOnAttachProperty, value);
}
