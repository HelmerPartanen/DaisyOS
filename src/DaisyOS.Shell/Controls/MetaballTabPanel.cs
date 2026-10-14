using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DaisyOS.Shell.Controls;

public class MetaballTabPanel : Panel
{
    public static readonly StyledProperty<double> FlareRadiusProperty =
        AvaloniaProperty.Register<MetaballTabPanel, double>(nameof(FlareRadius), 8.0);

    public double FlareRadius
    {
        get => GetValue(FlareRadiusProperty);
        set => SetValue(FlareRadiusProperty, value);
    }

    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<MetaballTabPanel, double>(nameof(CornerRadius), 8.0);

    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DirectProperty<MetaballTabPanel, Geometry?> MetaballGeometryProperty =
        AvaloniaProperty.RegisterDirect<MetaballTabPanel, Geometry?>(nameof(MetaballGeometry), o => o.MetaballGeometry);

    private Geometry? _metaballGeometry;
    public Geometry? MetaballGeometry
    {
        get => _metaballGeometry;
        private set => SetAndRaise(MetaballGeometryProperty, ref _metaballGeometry, value);
    }

    static MetaballTabPanel()
    {
        AffectsArrange<MetaballTabPanel>(FlareRadiusProperty, CornerRadiusProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double fr = FlareRadius;
        
        Size childAvailable = new Size(
            global::System.Math.Max(0, availableSize.Width),
            global::System.Math.Max(0, availableSize.Height - 12)
        );

        foreach (var child in Children)
        {
            if (child is Avalonia.Controls.Shapes.Path)
            {
                child.Measure(availableSize);
            }
            else
            {
                child.Measure(childAvailable);
            }
        }

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double fr = FlareRadius;
        
        double childWidth = global::System.Math.Max(0, finalSize.Width);
        double childHeight = global::System.Math.Max(0, finalSize.Height - 12);
        
        foreach (var child in Children)
        {
            if (child is Avalonia.Controls.Shapes.Path)
            {
                child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            }
            else
            {
                // Align the child over the full width, preserving 6px margins top and bottom.
                child.Arrange(new Rect(0, 6, childWidth, childHeight));
            }
        }

        UpdateGeometry(finalSize);
        return finalSize;
    }

    private void UpdateGeometry(Size finalSize)
    {
        double w = finalSize.Width;
        double h = finalSize.Height;
        if (w <= 0 || h <= 0) return;

        double fr = FlareRadius;
        double cr = CornerRadius;
        double top = 6.0;

        var pathGeometry = new StreamGeometry();
        using (var context2D = pathGeometry.Open())
        {
            context2D.BeginFigure(new Point(0, h), true);

            // Left flare
            context2D.CubicBezierTo(
                new Point(fr * 0.5, h),
                new Point(fr, h - fr * 0.6),
                new Point(fr, h - fr));

            // Left straight edge
            context2D.LineTo(new Point(fr, top + cr));

            // Top left corner
            context2D.CubicBezierTo(
                new Point(fr, top + cr * 0.45),
                new Point(fr + cr * 0.45, top),
                new Point(fr + cr, top));

            // Top straight edge
            context2D.LineTo(new Point(w - fr - cr, top));

            // Top right corner
            context2D.CubicBezierTo(
                new Point(w - fr - cr * 0.45, top),
                new Point(w - fr, top + cr * 0.45),
                new Point(w - fr, top + cr));

            // Right straight edge
            context2D.LineTo(new Point(w - fr, h - fr));

            // Right flare
            context2D.CubicBezierTo(
                new Point(w - fr, h - fr * 0.6),
                new Point(w - fr * 0.5, h),
                new Point(w, h));

            context2D.EndFigure(true);
        }

        MetaballGeometry = pathGeometry;
    }
}
