using Avalonia;
using Avalonia.Controls;

namespace DaisyOS.Shell.Controls;

/// <summary>One row of equal tabs, capped at their normal width and fitted to the available space.</summary>
public sealed class ShrinkingTabPanel : Panel
{
    private const double NormalWidth = 166;
    private const double NormalSpacing = 6;

    private (double Width, double Gap) Slots(double available)
    {
        var count = Children.Count;
        if (count == 0) return (0, 0);
        var gap = double.IsFinite(available) ? Math.Min(NormalSpacing, available / (2 * count)) : NormalSpacing;
        return (double.IsFinite(available) ? Math.Clamp((available - (count - 1) * gap) / count, 0, NormalWidth) : NormalWidth, gap);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (width, gap) = Slots(availableSize.Width);
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(Children.Count == 0 ? 0 : Children.Count * width + (Children.Count - 1) * gap, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (width, gap) = Slots(finalSize.Width);
        var x = 0.0;
        foreach (var child in Children)
        {
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + gap;
        }
        return finalSize;
    }
}
