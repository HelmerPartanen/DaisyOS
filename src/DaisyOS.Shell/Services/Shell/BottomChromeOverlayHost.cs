using Avalonia;
using Avalonia.Controls;

namespace DaisyOS.Shell.Services.Shell;

/// <summary>
/// Full-output overlay root with a shared viewport clipped inside the reserved dock edge.
/// </summary>
public sealed class BottomChromeOverlayHost : Grid
{
    public Grid Viewport { get; }

    public BottomChromeOverlayHost()
    {
        ClipToBounds = false;
        Viewport = new Grid
        {
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 0, ShellSurfacePlan.PrimaryPanelExclusiveZone)
        };
        Children.Add(Viewport);
    }
    public void SetDockEdge(Appearance.DockEdge edge, double extent = ShellSurfacePlan.PrimaryPanelExclusiveZone)
    {
        Viewport.Margin = DockGeometry.Inset(edge, extent);
        foreach (var child in Viewport.Children)
        {
            child.VerticalAlignment = edge == Appearance.DockEdge.Top ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Bottom;
            child.HorizontalAlignment = edge == Appearance.DockEdge.Left ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Right;
            child.Margin = edge switch
            {
                Appearance.DockEdge.Left => new Thickness(0, 6, 6, 6),
                Appearance.DockEdge.Right => new Thickness(6, 6, 0, 6),
                Appearance.DockEdge.Top => new Thickness(6, 0, 6, 6),
                _ => new Thickness(6, 6, 6, 0)
            };
        }
    }
}
