using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace DaisyOS.Shell.Services.Theming;

/// <summary>Suppresses decorative frame strokes and insets while preserving focus indicators.</summary>
internal static class PanelBorderPolicy
{
    internal static readonly string[] BrushKeys = ["ShellFrameOuterEdgeBrush", "ShellFrameInnerHighlightBrush", "ShellSurfaceInnerBorderBrush", "WindowFrameBorderBrush"];
    public static void Apply(IResourceDictionary resources, bool disabled)
    {
        foreach (var key in new[] { "ShellFrameBorderThickness", "ShellFrameSurfaceInset" })
            if (disabled) resources[key] = new Thickness(0);
            else resources.Remove(key);
        foreach (var (key, radius) in new (string, double)[]
        {
            ("RoundedXXLInset", 32), ("RoundedXXLDoubleInset", 32),
            ("RoundedXLInset", 18), ("RoundedXLDoubleInset", 18),
            ("RoundedLGInset", 14), ("AppWindowCornerRadiusInset", 16)
        })
            if (disabled) resources[key] = new CornerRadius(radius);
            else resources.Remove(key);
        foreach (var key in BrushKeys)
            if (disabled) resources[key] = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            else resources.Remove(key);
        foreach (var key in new[] { "ShellFrameOuterEdgeColor", "ShellFrameInnerHighlightColor" })
            if (disabled) resources[key] = Color.FromArgb(0, 0, 0, 0);
            else resources.Remove(key);
    }
}
