using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace DaisyOS.Shell.Controls;

/// <summary>Allows the desktop's inline editor to wrap outside its fixed icon cell.</summary>
public sealed class DesktopRenameHost : Panel
{
    // The normal desktop label receives the full 74 px cell width. The editor
    // adds four pixels of padding and a one-pixel border on each side, so its
    // outer width needs ten extra pixels to preserve that exact text width.
    private const double EditorChromeWidth = 10d;
    private double _editorWidth = 84d;

    public DesktopRenameHost() => ClipToBounds = false;

    protected override Size MeasureOverride(Size availableSize)
    {
        var labelWidth = this.FindAncestorOfType<DesktopItemView>()?.FindControl<DesktopItemLabel>("ItemLabel")?.TextLayout.WidthIncludingTrailingWhitespace;
        var contentWidth = labelWidth is > 0
            ? Math.Ceiling(labelWidth.Value) + 1
            : double.IsFinite(availableSize.Width) ? availableSize.Width : 74d;
        var width = Math.Min(84d, contentWidth + EditorChromeWidth);
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
        foreach (var child in Children)
        {
            var size = new Size(_editorWidth, child.DesiredSize.Height);
            var x = (finalSize.Width - size.Width) / 2;
            var y = 54d;
            if (origin is { } point && window is not null)
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
