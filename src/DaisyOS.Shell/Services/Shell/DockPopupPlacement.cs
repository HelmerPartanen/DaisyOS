using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using DaisyOS.Shell.Services.Appearance;
using System.Runtime.CompilerServices;

namespace DaisyOS.Shell.Services.Shell;

/// <summary>Positions dock popovers in their parent's coordinates, including detached native surfaces.</summary>
public static class DockPopupPlacement
{
    private sealed class Placement
    {
        public required Control Anchor;
        public DockEdge Edge;
        public bool NativePanel;
        public Point? AnchorPoint;
        public bool Updating;
        public EventHandler? Reflow;
        public double MaxWidth;
        public double MaxHeight;
    }
    private static readonly ConditionalWeakTable<Control, Placement> Placements = new();

    public static void Position(Control popup, Control anchor, DockEdge edge, bool nativePanel = true, Point? anchorPoint = null)
    {
        if (!Placements.TryGetValue(popup, out var placement))
        {
            placement = new Placement { Anchor = anchor, MaxWidth = popup.MaxWidth, MaxHeight = popup.MaxHeight };
            Placements.Add(popup, placement);
            // Reflow after content, output size, scale or anchor layout changes.
            placement.Reflow = (_, _) =>
            {
                if (popup.IsVisible) Apply(popup, placement, measure: false);
            };
            popup.LayoutUpdated += placement.Reflow;
            anchor.LayoutUpdated += placement.Reflow;
        }
        if (!ReferenceEquals(placement.Anchor, anchor))
        {
            placement.Anchor.LayoutUpdated -= placement.Reflow;
            anchor.LayoutUpdated += placement.Reflow;
        }
        placement.Anchor = anchor;
        placement.Edge = edge;
        placement.NativePanel = nativePanel;
        placement.AnchorPoint = anchorPoint;
        popup.IsVisible = true;
        // Commit both roots before starting motion. DesiredSize alone can differ
        // from the final arranged size after wrapping and fractional-scale rounding.
        TopLevel.GetTopLevel(anchor)?.UpdateLayout();
        Apply(popup, placement, measure: true);
        TopLevel.GetTopLevel(popup)?.UpdateLayout();
    }

    private static void Apply(Control popup, Placement placement, bool measure)
    {
        if (placement.Updating || popup.Parent is not Control parent) return;
        var owner = TopLevel.GetTopLevel(placement.Anchor);
        var overlay = TopLevel.GetTopLevel(popup);
        if (owner is null || overlay is null || parent.Bounds.Width <= 0 || parent.Bounds.Height <= 0) return;
        placement.Updating = true;
        try
        {
            var local = placement.AnchorPoint ?? default;
            var point = LayoutOrigin(placement.Anchor, owner, local);
            var end = LayoutOrigin(placement.Anchor, owner, placement.AnchorPoint ?? new Point(placement.Anchor.Bounds.Width, placement.Anchor.Bounds.Height));
            var anchorSize = new Size(Math.Abs(end.X - point.X), Math.Abs(end.Y - point.Y));
            if (placement.NativePanel && !ReferenceEquals(owner, overlay))
            {
                if (placement.Edge == DockEdge.Bottom) point = new Point(point.X, point.Y + overlay.Bounds.Height - owner.Bounds.Height);
                if (placement.Edge == DockEdge.Right) point = new Point(point.X + overlay.Bounds.Width - owner.Bounds.Width, point.Y);
            }
            var origin = LayoutOrigin(parent, overlay);
            var anchor = new Rect(point - (Vector)origin, anchorSize);
            if (popup is Controls.CartoucheBorder cartouche)
            {
                cartouche.TailHeight = 8;
                cartouche.TailEdge = placement.Edge switch
                {
                    DockEdge.Left => Controls.CartoucheTailEdge.Left,
                    DockEdge.Right => Controls.CartoucheTailEdge.Right,
                    DockEdge.Top => Controls.CartoucheTailEdge.Top,
                    _ => Controls.CartoucheTailEdge.Bottom
                };
                cartouche.TailOnTop = placement.Edge == DockEdge.Top;
            }
            var available = new Size(Math.Max(1, parent.Bounds.Width - 12), Math.Max(1, parent.Bounds.Height - 12));
            popup.MaxWidth = Math.Min(placement.MaxWidth, available.Width);
            popup.MaxHeight = Math.Min(placement.MaxHeight, available.Height);
            popup.HorizontalAlignment = HorizontalAlignment.Left;
            popup.VerticalAlignment = VerticalAlignment.Top;
            Size size;
            if (measure)
            {
                // Measure once when opening. Layout owns all later measurement;
                // invoking Measure from LayoutUpdated creates a native render loop.
                popup.Margin = default;
                popup.Measure(available);
                size = popup.DesiredSize;
            }
            else
            {
                size = popup.Bounds.Size;
                if (size.Width <= 0 || size.Height <= 0) return;
            }
            var rect = Fit(anchor, size, parent.Bounds.Size, placement.Edge);
            popup.HorizontalAlignment = HorizontalAlignment.Left;
            popup.VerticalAlignment = VerticalAlignment.Top;
            popup.Margin = new Thickness(rect.X, rect.Y, 0, 0);
            if (popup is Controls.CartoucheBorder pointer)
            {
                pointer.TailX = anchor.Center.X - rect.X;
                pointer.TailY = anchor.Center.Y - rect.Y;
            }
        }
        finally { placement.Updating = false; }
    }

    internal static Point LayoutOrigin(Visual control, Visual root, Point localPoint = default)
    {
        // A revealing dock is translated temporarily. Anchor to its resting
        // layout box so the popover does not chase that transform and snap back.
        var origin = localPoint;
        for (Visual? current = control; current is not null && current != root; current = current.GetVisualParent())
        {
            // LayoutTransformControl implements taskbar sizing through a render
            // matrix. Preserve scale/rotation, while ignoring reveal translations.
            if (current.RenderTransform?.Value is { } matrix &&
                (matrix.M11 != 1 || matrix.M12 != 0 || matrix.M21 != 0 || matrix.M22 != 1))
            {
                var pivot = current.RenderTransformOrigin.ToPixels(current.Bounds.Size);
                origin = (origin - (Vector)pivot).Transform(matrix) + (Vector)pivot;
            }
            origin += (Vector)current.Bounds.Position;
        }
        return origin;
    }

    internal static Rect Fit(Rect anchor, Size size, Size viewport, DockEdge edge)
    {
        double x = anchor.Center.X - size.Width / 2;
        double y = anchor.Top - size.Height - 6;
        if (edge == DockEdge.Top) y = anchor.Bottom + 6;
        if (DockGeometry.IsVertical(edge))
        {
            x = edge == DockEdge.Left ? anchor.Right + 6 : anchor.Left - size.Width - 6;
            y = anchor.Center.Y - size.Height / 2;
        }
        return new Rect(new Point(Math.Clamp(x, 6, Math.Max(6, viewport.Width - size.Width - 6)),
            Math.Clamp(y, 6, Math.Max(6, viewport.Height - size.Height - 6))), size);
    }
}
