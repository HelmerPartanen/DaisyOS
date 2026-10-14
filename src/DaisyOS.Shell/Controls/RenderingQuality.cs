using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DaisyOS.Shell.Controls;

/// <summary>Shared rendering defaults for shell controls and their templates.</summary>
public sealed class RenderingQuality : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<RenderingQuality, Control, bool>("Enabled");

    static RenderingQuality()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            if (change.NewValue is true)
            {
                // Avalonia 12 exposes these options through methods rather
                // than styled properties, so XAML Setters cannot set them.
                RenderOptions.SetEdgeMode(control, EdgeMode.Antialias);
                RenderOptions.SetBitmapInterpolationMode(control, BitmapInterpolationMode.HighQuality);
                TextOptions.SetTextRenderingMode(control, TextRenderingMode.Antialias);
                TextOptions.SetTextHintingMode(control, TextHintingMode.Light);
            }
        });
    }

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);
}
