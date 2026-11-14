using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace DaisyOS.Shell.Controls;

public enum CartoucheTailEdge { Bottom, Top, Left, Right }

/// <summary>
/// A seamless popover container featuring a smooth, rounded cartouche on any edge
/// that flows continuously with the 3-tier DaisyOS shell glass border and elevation shadow.
/// </summary>
public class CartoucheBorder : Decorator
{
    private static readonly IBrush ShadowFarBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x18, 0, 0, 0));
    private static readonly IBrush ShadowMiddleBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x1C, 0, 0, 0));
    private static readonly IBrush ShadowNearBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x14, 0, 0, 0));
    private static readonly IBrush FallbackSurfaceBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0x24, 0x24, 0x26));
    private static readonly IBrush FallbackHighlightBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush FallbackEdgeBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0, 0, 0));
    public static readonly StyledProperty<double> TailXProperty =
        AvaloniaProperty.Register<CartoucheBorder, double>(nameof(TailX), double.NaN);

    public double TailX
    {
        get => GetValue(TailXProperty);
        set => SetValue(TailXProperty, value);
    }

    public static readonly StyledProperty<double> TailWidthProperty =
        AvaloniaProperty.Register<CartoucheBorder, double>(nameof(TailWidth), 18.0);

    public double TailWidth
    {
        get => GetValue(TailWidthProperty);
        set => SetValue(TailWidthProperty, value);
    }

    public static readonly StyledProperty<double> TailHeightProperty =
        AvaloniaProperty.Register<CartoucheBorder, double>(nameof(TailHeight), 8.0);

    public double TailHeight
    {
        get => GetValue(TailHeightProperty);
        set => SetValue(TailHeightProperty, value);
    }

    public static readonly StyledProperty<bool> TailOnTopProperty =
        AvaloniaProperty.Register<CartoucheBorder, bool>(nameof(TailOnTop));

    public bool TailOnTop
    {
        get => GetValue(TailOnTopProperty);
        set => SetValue(TailOnTopProperty, value);
    }

    public static readonly StyledProperty<CartoucheTailEdge> TailEdgeProperty =
        AvaloniaProperty.Register<CartoucheBorder, CartoucheTailEdge>(nameof(TailEdge));
    public CartoucheTailEdge TailEdge { get => GetValue(TailEdgeProperty); set => SetValue(TailEdgeProperty, value); }

    public static readonly StyledProperty<double> TailYProperty =
        AvaloniaProperty.Register<CartoucheBorder, double>(nameof(TailY), double.NaN);
    public double TailY { get => GetValue(TailYProperty); set => SetValue(TailYProperty, value); }

    private CartoucheTailEdge EffectiveTailEdge => TailOnTop ? CartoucheTailEdge.Top : TailEdge;

    private Thickness ContentInsets()
    {
        var padding = Padding;
        var th = Math.Max(0, TailHeight);
        var edge = EffectiveTailEdge;
        return new Thickness(padding.Left + 2 + (edge == CartoucheTailEdge.Left ? th : 0),
            padding.Top + 2 + (edge == CartoucheTailEdge.Top ? th : 0),
            padding.Right + 2 + (edge == CartoucheTailEdge.Right ? th : 0),
            padding.Bottom + 2 + (edge == CartoucheTailEdge.Bottom ? th : 0));
    }

    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<CartoucheBorder, double>(nameof(CornerRadius), 14.0);

    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<CartoucheBorder, IBrush?>(nameof(Background));

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<CartoucheBorder, IBrush?>(nameof(BorderBrush));

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public static readonly StyledProperty<IBrush?> InnerHighlightBrushProperty =
        AvaloniaProperty.Register<CartoucheBorder, IBrush?>(nameof(InnerHighlightBrush));

    public IBrush? InnerHighlightBrush
    {
        get => GetValue(InnerHighlightBrushProperty);
        set => SetValue(InnerHighlightBrushProperty, value);
    }


    public CartoucheBorder()
    {
        // Resource changes must invalidate custom-drawn chrome as well as XAML borders.
        this.Bind(BorderBrushProperty, this.GetResourceObservable("ShellFrameOuterEdgeBrush"));
        this.Bind(InnerHighlightBrushProperty, this.GetResourceObservable("ShellFrameInnerHighlightBrush"));
    }

    static CartoucheBorder()
    {
        AffectsRender<CartoucheBorder>(
            TailXProperty, TailYProperty, TailEdgeProperty, TailWidthProperty, TailHeightProperty, TailOnTopProperty,
            CornerRadiusProperty, BackgroundProperty, BorderBrushProperty,
            InnerHighlightBrushProperty);
        AffectsMeasure<CartoucheBorder>(TailHeightProperty, TailEdgeProperty, TailOnTopProperty, PaddingProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var innerPadding = ContentInsets();

        if (Child != null)
        {
            Child.Measure(availableSize.Deflate(innerPadding));
            return Child.DesiredSize.Inflate(innerPadding);
        }

        return new Size(innerPadding.Left + innerPadding.Right, innerPadding.Top + innerPadding.Bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var innerPadding = ContentInsets();

        if (Child != null)
        {
            var targetRect = new Rect(
                innerPadding.Left,
                innerPadding.Top,
                Math.Max(0, finalSize.Width - innerPadding.Left - innerPadding.Right),
                Math.Max(0, finalSize.Height - innerPadding.Top - innerPadding.Bottom));
            Child.Arrange(targetRect);
        }

        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        double th = TailHeight;
        double tw = TailWidth;
        double tx = EffectiveTailEdge is CartoucheTailEdge.Left or CartoucheTailEdge.Right ? TailY : TailX;
        double cr = CornerRadius;
        var tailEdge = EffectiveTailEdge;

        var borderless = this.TryFindResource("ShellFrameBorderThickness", out var thickness)
            && thickness is Thickness { Left: 0, Top: 0, Right: 0, Bottom: 0 };
        var geometry = GetGeometry(w, h, tx, tw, th, cr, tailEdge, borderless);

        // 1. Multi-layered soft drop shadow
        using (context.PushTransform(Matrix.CreateTranslation(0, 4)))
        {
            context.DrawGeometry(ShadowFarBrush, null, geometry.Shadow);
        }
        using (context.PushTransform(Matrix.CreateTranslation(0, 2)))
        {
            context.DrawGeometry(ShadowMiddleBrush, null, geometry.Shadow);
        }
        using (context.PushTransform(Matrix.CreateTranslation(0, 1)))
        {
            context.DrawGeometry(ShadowNearBrush, null, geometry.Shadow);
        }

        // 2. Tier 3: Surface fill
        var fillBrush = Background
                        ?? (this.TryFindResource("SurfaceOverlayBrush", out var resFill) ? resFill as IBrush : null)
                        ?? (Application.Current != null && Application.Current.TryFindResource("SurfaceOverlayBrush", out var appFill) ? appFill as IBrush : null)
                        ?? FallbackSurfaceBrush;
        context.DrawGeometry(fillBrush, null, geometry.Fill);

        // 3. Tier 2: Inner highlight (1px inset frame)
        var innerHighlightBrush = (this.TryFindResource("ShellFrameInnerHighlightBrush", out var resInner) ? resInner as IBrush : null)
                                  ?? InnerHighlightBrush
                                  ?? FallbackHighlightBrush;
        context.DrawGeometry(null, RefPen(ref _innerPen, ref _innerPenBrush, innerHighlightBrush, 1.0), geometry.Inner);

        // 4. Tier 1: Outer edge perimeter (1px outer frame)
        var outerEdgeBrush = (this.TryFindResource("ShellFrameOuterEdgeBrush", out var resOuter) ? resOuter as IBrush : null)
                             ?? BorderBrush
                             ?? FallbackEdgeBrush;
        context.DrawGeometry(null, RefPen(ref _outerPen, ref _outerPenBrush, outerEdgeBrush, 1.0), geometry.Outer);
    }

    private sealed class GeometryBundle(
        StreamGeometry shadow,
        StreamGeometry fill,
        StreamGeometry inner,
        StreamGeometry outer)
    {
        public StreamGeometry Shadow { get; } = shadow;
        public StreamGeometry Fill { get; } = fill;
        public StreamGeometry Inner { get; } = inner;
        public StreamGeometry Outer { get; } = outer;
    }

    private GeometryBundle? _geometry;
    private double _geometryWidth, _geometryHeight, _geometryTailX, _geometryTailWidth;
    private double _geometryTailHeight, _geometryCornerRadius;
    private CartoucheTailEdge _geometryTailEdge;
    private bool _geometryBorderless;
    private Pen? _innerPen, _outerPen;
    private IBrush? _innerPenBrush, _outerPenBrush;

    private GeometryBundle GetGeometry(
        double width,
        double height,
        double tailX,
        double tailWidth,
        double tailHeight,
        double cornerRadius,
        CartoucheTailEdge tailEdge, bool borderless)
    {
        if (_geometry is { } cached &&
            _geometryWidth.Equals(width) && _geometryHeight.Equals(height) &&
            _geometryTailX.Equals(tailX) && _geometryTailWidth.Equals(tailWidth) &&
            _geometryTailHeight.Equals(tailHeight) && _geometryCornerRadius.Equals(cornerRadius) &&
            _geometryTailEdge == tailEdge && _geometryBorderless == borderless)
        {
            return cached;
        }

        _geometryWidth = width;
        _geometryHeight = height;
        _geometryTailX = tailX;
        _geometryTailWidth = tailWidth;
        _geometryTailHeight = tailHeight;
        _geometryCornerRadius = cornerRadius;
        _geometryTailEdge = tailEdge;
        _geometryBorderless = borderless;
        _geometry = new GeometryBundle(
            BuildGeometry(width, height, tailX, tailWidth, tailHeight, cornerRadius, 0, tailEdge),
            BuildGeometry(width, height, tailX, tailWidth, tailHeight, cornerRadius, borderless ? 0 : 2.0, tailEdge),
            BuildGeometry(width, height, tailX, tailWidth, tailHeight, cornerRadius, 1.5, tailEdge),
            BuildGeometry(width, height, tailX, tailWidth, tailHeight, cornerRadius, 0.5, tailEdge));
        return _geometry;
    }

    private static Pen RefPen(ref Pen? pen, ref IBrush? cachedBrush, IBrush brush, double thickness)
    {
        if (pen is null || !ReferenceEquals(cachedBrush, brush))
        {
            cachedBrush = brush;
            pen = new Pen(brush, thickness);
        }
        return pen;
    }

    internal static StreamGeometry BuildGeometry(double width, double height, double tailX, double tailWidth, double tailHeight, double cornerRadius, double inset, CartoucheTailEdge tailEdge)
    {
        // Build the familiar bottom cartouche, then orient the complete path.
        // Content stays upright; only the frame geometry changes direction.
        var actualWidth = width;
        var actualHeight = height;
        if (tailEdge is CartoucheTailEdge.Left or CartoucheTailEdge.Right)
            (width, height) = (height, width);
        var geom = new StreamGeometry();
        using var ctx = geom.Open();

        double l = inset;
        double t = inset;
        double r = Math.Max(l + 1, width - inset);
        double b = Math.Max(t + 1, height - tailHeight - inset);
        double cr = Math.Clamp(cornerRadius - inset, 0, Math.Min(r - l, b - t) / 2);

        double tw = Math.Max(4, tailWidth - inset * 0.5);
        double th = tailHeight <= 0 ? 0 : Math.Max(0, tailHeight - inset * 0.5);

        double minTx = l + cr + tw / 2.0;
        double maxTx = r - cr - tw / 2.0;
        double tx = (double.IsNaN(tailX) || minTx > maxTx)
            ? (l + r) / 2.0
            : Math.Clamp(tailX, minTx, maxTx);

        double tailLeft = tx - tw / 2.0;
        double tailRight = tx + tw / 2.0;
        double tipY = b + th;

        // Top-left start
        ctx.BeginFigure(new Point(l + cr, t), isFilled: true);

        // Top edge
        ctx.LineTo(new Point(r - cr, t));
        // Top-right corner
        ctx.ArcTo(new Point(r, t + cr), new Size(cr, cr), 0, false, SweepDirection.Clockwise);

        // Right edge
        ctx.LineTo(new Point(r, b - cr));
        // Bottom-right corner
        ctx.ArcTo(new Point(r - cr, b), new Size(cr, cr), 0, false, SweepDirection.Clockwise);

        if (th > 0)
        {
            // Bottom edge to right base of tail
            ctx.LineTo(new Point(tailRight, b));

            // Right slope of tail into rounded tip
            Point p1 = new Point(tx + tw * 0.30, b + th * 0.15);
            Point p2 = new Point(tx + tw * 0.14, tipY - th * 0.25);
            Point pTipR = new Point(tx + tw * 0.08, tipY - th * 0.08);
            ctx.CubicBezierTo(p1, p2, pTipR);

            // Rounded tip
            Point pTipC = new Point(tx, tipY);
            Point pTipL = new Point(tx - tw * 0.08, tipY - th * 0.08);
            ctx.CubicBezierTo(new Point(tx + tw * 0.03, tipY), pTipC, pTipC);
            ctx.CubicBezierTo(new Point(tx - tw * 0.03, tipY), pTipL, pTipL);

            // Left slope of tail back to bottom edge
            Point p3 = new Point(tx - tw * 0.14, tipY - th * 0.25);
            Point p4 = new Point(tx - tw * 0.30, b + th * 0.15);
            ctx.CubicBezierTo(p3, p4, new Point(tailLeft, b));
        }

        // Bottom edge to bottom-left corner
        ctx.LineTo(new Point(l + cr, b));
        // Bottom-left corner
        ctx.ArcTo(new Point(l, b - cr), new Size(cr, cr), 0, false, SweepDirection.Clockwise);

        // Left edge
        ctx.LineTo(new Point(l, t + cr));
        // Top-left corner
        ctx.ArcTo(new Point(l + cr, t), new Size(cr, cr), 0, false, SweepDirection.Clockwise);

        ctx.EndFigure(isClosed: true);
        geom.Transform = tailEdge switch
        {
            CartoucheTailEdge.Top => new MatrixTransform(new Matrix(1, 0, 0, -1, 0, actualHeight)),
            CartoucheTailEdge.Left => new MatrixTransform(new Matrix(0, 1, -1, 0, actualWidth, 0)),
            CartoucheTailEdge.Right => new MatrixTransform(new Matrix(0, 1, 1, 0, 0, 0)),
            _ => null
        };
        return geom;
    }
}
