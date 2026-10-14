using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace DaisyOS.Shell.Controls;

/// <summary>Keeps the shared rail active for the thumb's entire pointer capture.</summary>
public sealed class ScrollBarInteraction : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollBarInteraction, ScrollBar, bool>("Enabled");

    static ScrollBarInteraction()
    {
        Thumb.DragStartedEvent.AddClassHandler<ScrollBar>((bar, _) =>
        {
            if (GetEnabled(bar)) bar.Classes.Set("thumb-dragging", true);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        // Thumb also completes its drag when pointer capture is lost.
        Thumb.DragCompletedEvent.AddClassHandler<ScrollBar>((bar, _) =>
            bar.Classes.Set("thumb-dragging", false), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public static bool GetEnabled(ScrollBar bar) => bar.GetValue(EnabledProperty);
    public static void SetEnabled(ScrollBar bar, bool value) => bar.SetValue(EnabledProperty, value);
}
