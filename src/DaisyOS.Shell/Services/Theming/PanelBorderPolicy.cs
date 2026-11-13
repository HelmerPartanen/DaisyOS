using Avalonia.Controls;
using Avalonia.Media;
namespace DaisyOS.Shell.Services.Theming;

/// <summary>Suppresses decorative frame edges while preserving layout and focus indicators.</summary>
internal static class PanelBorderPolicy
{
    internal static readonly string[] BrushKeys = ["ShellFrameOuterEdgeBrush", "ShellFrameInnerHighlightBrush", "ShellSurfaceInnerBorderBrush", "WindowFrameBorderBrush"];
    public static void Apply(IResourceDictionary resources, bool disabled)
    {
        foreach (var key in BrushKeys)
            if (disabled) resources[key] = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            else resources.Remove(key);
        foreach (var key in new[] { "ShellFrameOuterEdgeColor", "ShellFrameInnerHighlightColor" })
            if (disabled) resources[key] = Color.FromArgb(0, 0, 0, 0);
            else resources.Remove(key);
    }
}
