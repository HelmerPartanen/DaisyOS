using Avalonia;
using Avalonia.Controls;
using DaisyOS.Shell.Services.Motion;

namespace DaisyOS.Shell.Controls;

/// <summary>Reveals popup contents after attachment, without moving the native popup frame.</summary>
public sealed class MotionBehavior : AvaloniaObject
{
    public static readonly AttachedProperty<bool> RevealOnAttachProperty =
        AvaloniaProperty.RegisterAttached<MotionBehavior, Control, bool>("RevealOnAttach");
    public static readonly AttachedProperty<bool> RevealOnVisibleProperty =
        AvaloniaProperty.RegisterAttached<MotionBehavior, Control, bool>("RevealOnVisible");

    static MotionBehavior()
    {
        RevealOnAttachProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            control.AttachedToVisualTree -= OnAttached;
            if (change.NewValue is true) control.AttachedToVisualTree += OnAttached;
            UpdateDetachHandler(control);
        });
        RevealOnVisibleProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            control.PropertyChanged -= OnVisibilityChanged;
            if (change.NewValue is true) control.PropertyChanged += OnVisibilityChanged;
            UpdateDetachHandler(control);
        });
    }
    private static void UpdateDetachHandler(Control control)
    {
        control.DetachedFromVisualTree -= OnDetached;
        if (GetRevealOnAttach(control) || GetRevealOnVisible(control)) control.DetachedFromVisualTree += OnDetached;
    }
    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    { if (sender is Control control && control.IsVisible) _ = GuiMotion.RevealAsync(control, 4); }
    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    { if (sender is Control control) GuiMotion.Cancel(control); }
    private static void OnVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || sender is not Control control) return;
        if (control.IsVisible) _ = GuiMotion.RevealAsync(control, 4);
        else GuiMotion.Cancel(control);
    }
    public static bool GetRevealOnAttach(AvaloniaObject target) => target.GetValue(RevealOnAttachProperty);
    public static void SetRevealOnAttach(AvaloniaObject target, bool value) => target.SetValue(RevealOnAttachProperty, value);
    public static bool GetRevealOnVisible(AvaloniaObject target) => target.GetValue(RevealOnVisibleProperty);
    public static void SetRevealOnVisible(AvaloniaObject target, bool value) => target.SetValue(RevealOnVisibleProperty, value);
}
