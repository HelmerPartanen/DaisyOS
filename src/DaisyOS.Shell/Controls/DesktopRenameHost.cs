using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace DaisyOS.Shell.Controls;

/// <summary>Allows the desktop's inline editor to wrap outside its fixed icon cell.</summary>
public sealed class DesktopRenameHost : Panel
{
    private double _editorWidth = 74d;

    private (Point? Origin, Size Window)? _placement;

    public DesktopRenameHost()
    {
        ClipToBounds = false;
        LayoutUpdated += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is { } window &&
                _placement != (this.TranslatePoint(default, window), window.Bounds.Size))
                InvalidateArrange();
        };
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Keep editing inside the desktop item's label column. A wide overlay
        // obscures neighboring items and no longer matches the committed label.
        var width = double.IsFinite(availableSize.Width) && availableSize.Width > 0
            ? availableSize.Width
            : 74d;
        if (TopLevel.GetTopLevel(this) is { } window && window.Bounds.Width > 8)
            width = Math.Min(width, window.Bounds.Width - 8);
        _editorWidth = width;
        foreach (var child in Children) child.Measure(new Size(width, double.PositiveInfinity));
        // Editing must not resize the grid or move neighboring icons.
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var window = TopLevel.GetTopLevel(this);
        var origin = window is not null ? this.TranslatePoint(default, window) : null;
        _placement = (origin, window?.Bounds.Size ?? default);
        foreach (var child in Children)
        {
            var size = new Size(_editorWidth, child.DesiredSize.Height);
            var x = (finalSize.Width - size.Width) / 2;
            var y = 56d;
            if (origin is { } point && window is { Bounds.Width: > 8, Bounds.Height: > 8 })
            {
                x = Math.Clamp(x, 4 - point.X, Math.Max(4 - point.X, window.Bounds.Width - point.X - size.Width - 4));
                if (point.Y + y + size.Height > window.Bounds.Height - 4)
                    y = Math.Max(4 - point.Y, -size.Height - 4);
            }
            child.Arrange(new Rect(new Point(x, y), size));
        }
        return finalSize;
    }
}
