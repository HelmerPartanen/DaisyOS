using Avalonia;
using Avalonia.Controls;

namespace DaisyOS.Shell.Controls;

/// <summary>Optional fixed search header in the shared dropdown menu.</summary>
public sealed class DropdownSearch : AvaloniaObject
{
    public static readonly AttachedProperty<Control?> HeaderProperty =
        AvaloniaProperty.RegisterAttached<DropdownSearch, ComboBox, Control?>("Header");
    public static Control? GetHeader(ComboBox control) => control.GetValue(HeaderProperty);
    public static void SetHeader(ComboBox control, Control? value) => control.SetValue(HeaderProperty, value);
}
