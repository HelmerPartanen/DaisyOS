using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DaisyOS.Shell.Services.Theming;

/// <summary>Inherited rendering options for opaque and translucent application surfaces.</summary>
public sealed class TextRenderingPolicy : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextRenderingPolicy, Control, bool>("Enabled");

    static TextRenderingPolicy()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            if (change.NewValue is true)
                TextOptions.SetTextOptions(control, new TextOptions
                {
                    TextRenderingMode = TextRenderingMode.Antialias,
                    TextHintingMode = TextHintingMode.Strong,
                    BaselinePixelAlignment = BaselinePixelAlignment.Aligned
                });
        });
    }

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);
}
