using Avalonia;
using DaisyOS.Shell.Services.Appearance;

namespace DaisyOS.Shell.Services.Shell;

/// <summary>Shared logical geometry for panels, work areas, reveal sensors and overlays.</summary>
public static class DockGeometry
{
    public static bool IsVertical(DockEdge edge) => edge is DockEdge.Left or DockEdge.Right;
    public static LayerShellAnchor Anchors(DockEdge edge) => edge switch
    {
        DockEdge.Top => LayerShellAnchor.Top | LayerShellAnchor.Left | LayerShellAnchor.Right,
        DockEdge.Left => LayerShellAnchor.Left | LayerShellAnchor.Top | LayerShellAnchor.Bottom,
        DockEdge.Right => LayerShellAnchor.Right | LayerShellAnchor.Top | LayerShellAnchor.Bottom,
        _ => LayerShellAnchor.Bottom | LayerShellAnchor.Left | LayerShellAnchor.Right
    };
    public static Thickness Inset(DockEdge edge, double extent) => edge switch
    {
        DockEdge.Left => new(extent, 0, 0, 0), DockEdge.Top => new(0, extent, 0, 0),
        DockEdge.Right => new(0, 0, extent, 0), _ => new(0, 0, 0, extent)
    };
    public static Point Slide(DockEdge edge, double distance) => edge switch
    {
        DockEdge.Left => new(-distance, 0), DockEdge.Right => new(distance, 0),
        DockEdge.Top => new(0, -distance), _ => new(0, distance)
    };
    public static bool IsAtEdge(Point point, Size size, DockEdge edge, double depth = 2) => edge switch
    {
        DockEdge.Top => point.Y >= 0 && point.Y <= depth,
        DockEdge.Left => point.X >= 0 && point.X <= depth,
        DockEdge.Right => point.X <= size.Width && point.X >= size.Width - depth,
        _ => point.Y <= size.Height && point.Y >= size.Height - depth
    };
    public static LayerShellSurfaceOptions AtEdge(LayerShellSurfaceOptions options, DockEdge edge, uint thickness) =>
        options with { Anchor = Anchors(edge), Width = IsVertical(edge) ? thickness : 0, Height = IsVertical(edge) ? 0 : thickness };
}
