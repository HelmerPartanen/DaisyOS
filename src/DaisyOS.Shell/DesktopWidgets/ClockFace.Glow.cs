using System;
using Avalonia;
using Avalonia.Media;

namespace DaisyOS.Shell.DesktopWidgets;

/// <summary>
/// "Glow" analog face: an angular (conic) light trail follows the second hand around the dial,
/// the shared 60 minute ticks are lit by that same trail, and the quarter marks (12, 3, 6, 9)
/// are drawn long. Uses the *_Hand_Glow.svg hands with a soft halo.
/// </summary>
public sealed partial class ClockFace
{
    public static readonly StyledProperty<bool> IsGlowProperty =
        AvaloniaProperty.Register<ClockFace, bool>(nameof(IsGlow));

    public bool IsGlow
    {
        get => GetValue(IsGlowProperty);
        set => SetValue(IsGlowProperty, value);
    }

    // Glow hands. Hour/Minute pivot on their stroked ring path center (32, ~32);
    // the second hand pivots on its <circle cx="22" cy="76">.
    private static readonly Lazy<Hand> GlowHourHand = new(() => LoadHand("Hour_Hand_Glow.svg"));
    private static readonly Lazy<Hand> GlowMinuteHand = new(() => LoadHand("Minute_Hand_Glow.svg"));
    private static readonly Lazy<Hand> GlowSecondHand = new(() => LoadHand("Second_Hand_Glow.svg"));

    // Steepness of the trail's exponential falloff behind the second hand.
    private const double GlowFalloff = 3.6;

    // How far the quarter marks extend inward past a regular minute tick.
    private const double QuarterTickExtension = 10.0;

    // Clearance between the longest hand's tip and the widget edge.
    private const double GlowHandClearance = 24.0;

    // Halo passes around the hands (screen pixels, alpha). Wide+faint to narrow+brighter.
    private static readonly (double Width, double Alpha)[] HaloPasses = { (9.0, 0.035), (5.0, 0.06), (2.5, 0.10) };

    // Opacity mask that keeps the trail strongest around the pivot and softer toward the frame.
    private static readonly IBrush GlowVignetteMask = new RadialGradientBrush
    {
        GradientStops =
        {
            new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.0),
            new GradientStop(Color.FromArgb(200, 0, 0, 0), 0.55),
            new GradientStop(Color.FromArgb(120, 0, 0, 0), 1.0),
        }
    };

    private readonly double[] _cachedTickAngles = new double[60];
    private double _cachedTickAngleWidth = -1;
    private double _cachedTickAngleHeight = -1;

    private ConicGradientBrush? _glowSweepBrush;
    private RadialGradientBrush? _glowBloomBrush;
    private Color _glowBrushColor;
    private double _glowBrushPeak = -1;

    /// <summary>Trail brightness (0..1) for a point <paramref name="behind"/> (0..1 of a turn) behind the hand.</summary>
    private static double GlowTrail(double behind)
    {
        double floor = Math.Exp(-GlowFalloff);
        return Math.Clamp((Math.Exp(-GlowFalloff * behind) - floor) / (1.0 - floor), 0.0, 1.0);
    }

    private void EnsureGlowBrushes(Color fore, double peak)
    {
        if (_glowSweepBrush is not null && _glowBrushColor == fore && Math.Abs(_glowBrushPeak - peak) < 1e-6)
            return;

        _glowBrushColor = fore;
        _glowBrushPeak = peak;

        // Conic gradients run clockwise from Angle (0 = 12 o'clock). Offset 0 sits just
        // ahead of the hand (dark), offset 1 just behind it (brightest) - a sharp leading edge
        // with a long exponential tail, like a phosphor trace.
        var sweep = new ConicGradientBrush { Center = RelativePoint.Center };
        double[] offsets = { 0.0, 0.25, 0.45, 0.6, 0.72, 0.82, 0.89, 0.94, 0.975, 1.0 };
        foreach (double offset in offsets)
        {
            byte alpha = (byte)Math.Round(255.0 * peak * GlowTrail(1.0 - offset));
            sweep.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, fore.R, fore.G, fore.B), offset));
        }
        _glowSweepBrush = sweep;

        // A faint bloom around the pivot gives the dial some depth.
        _glowBloomBrush = new RadialGradientBrush
        {
            RadiusX = new RelativeScalar(0.35, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.45, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)Math.Round(255 * peak * 0.35), fore.R, fore.G, fore.B), 0.0),
                new GradientStop(Color.FromArgb(0, fore.R, fore.G, fore.B), 1.0),
            }
        };
    }

    private void EnsureTickAngles(double width, double height, Point center)
    {
        if (Math.Abs(_cachedTickAngleWidth - width) < 0.1 && Math.Abs(_cachedTickAngleHeight - height) < 0.1)
            return;

        _cachedTickAngleWidth = width;
        _cachedTickAngleHeight = height;
        for (int i = 0; i < 60; i++)
        {
            var (start, end) = _cachedTicks[i];
            var mid = (start + end) * 0.5 - center;
            // Ticks are spaced evenly along the rounded-rect perimeter, so their true angle
            // differs from i * 6 deg. Lighting by true angle keeps them in sync with the trail.
            double degrees = Math.Atan2(mid.X, -mid.Y) * 180.0 / Math.PI;
            _cachedTickAngles[i] = (degrees + 360.0) % 360.0;
        }
    }

    private void DrawGlow(DrawingContext context, Point center, double width, double height, DateTime now)
    {
        bool isLight = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;
        var fore = isLight ? Color.FromRgb(0, 0, 0) : Color.FromRgb(255, 255, 255);
        var pens = isLight ? LightPens : DarkPens;
        var primary = ThemeBrush("ContentPrimaryBrush", isLight ? Brushes.Black : Brushes.White);
        var accent = ThemeBrush("ActionPrimaryBrush", primary);

        double seconds = now.Second + now.Millisecond / 1000d;
        double minutes = now.Minute + seconds / 60;
        double hours = now.Hour % 12 + minutes / 60;
        double secondDegrees = seconds * 6.0;

        var bounds = new Rect(0, 0, width, height);
        EnsureGlowBrushes(fore, isLight ? 0.10 : 0.20);
        EnsureTickAngles(width, height, center);

        // 1. Angular trail + center bloom.
        _glowSweepBrush!.Angle = secondDegrees;
        using (context.PushOpacityMask(GlowVignetteMask, bounds))
        {
            context.DrawRectangle(_glowSweepBrush, null, bounds);
        }
        context.DrawRectangle(_glowBloomBrush, null, bounds);

        // 2. A faint light edge along the leading side of the trail softens the hard
        //    gradient seam where the sweep wraps from brightest to darkest.
        double radians = secondDegrees * Math.PI / 180.0;
        var direction = new Vector(Math.Sin(radians), -Math.Cos(radians));
        var far = center + direction * Math.Sqrt(width * width + height * height) * 0.5;
        var edgeBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(center, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(far, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(isLight ? 40 : 70), fore.R, fore.G, fore.B), 0.0),
                new GradientStop(Color.FromArgb(0, fore.R, fore.G, fore.B), 1.0),
            }
        };
        context.DrawLine(new Pen(edgeBrush, 1.0) { LineCap = PenLineCap.Round }, center, far);

        // 3. Minute ticks shared with the other faces, lit as the trail passes over them.
        //    12/3/6/9 extend inward and stay bright.
        for (int i = 0; i < 60; i++)
        {
            var (start, end) = _cachedTicks[i];
            double behind = ((secondDegrees - _cachedTickAngles[i]) % 360.0 + 360.0) % 360.0 / 360.0;
            double trail = GlowTrail(behind);
            double opacity;
            if (i % 15 == 0)
            {
                start -= (end - start) / TickLength * QuarterTickExtension;
                opacity = 0.85 + 0.15 * trail;
            }
            else
            {
                opacity = (i % 5 == 0 ? 0.42 : 0.26) + 0.70 * trail;
            }

            int alpha = Math.Clamp((int)Math.Round(opacity * 255.0), 25, 255);
            context.DrawLine(pens[alpha], start, end);
        }

        // 4. Hands, sharing one scale so the supplied proportions are preserved.
        double longestHand = Math.Max(GlowHourHand.Value.ForwardLength,
            Math.Max(GlowMinuteHand.Value.ForwardLength, GlowSecondHand.Value.ForwardLength));
        double scale = Math.Max(0, (Math.Min(width, height) * 0.5 - GlowHandClearance) / longestHand);
        if (scale <= 0)
            return;

        var halo = isLight ? Color.FromRgb(0, 0, 0) : Color.FromRgb(255, 255, 255);
        var secondHalo = accent is ISolidColorBrush solidAccent ? solidAccent.Color : halo;
        DrawGlowHand(context, GlowHourHand.Value, primary, halo, center, scale, hours * Math.PI / 6);
        DrawGlowHand(context, GlowMinuteHand.Value, primary, halo, center, scale, minutes * Math.PI / 30);
        DrawGlowHand(context, GlowSecondHand.Value, accent, secondHalo, center, scale, seconds * Math.PI / 30);
    }

    private static void DrawGlowHand(DrawingContext context, Hand hand, IBrush brush, Color haloColor, Point center, double scale, double angle)
    {
        using (context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Antialias }))
        using (context.PushTransform(HandTransform(hand.Pivot, center, scale, angle)))
        {
            // Soft halo: concentric outlines of decreasing width, converted from screen
            // pixels into the hand's SVG units so the glow is resolution independent.
            foreach (var (haloWidth, haloAlpha) in HaloPasses)
            {
                var haloBrush = new SolidColorBrush(haloColor, haloAlpha);
                double extra = haloWidth * 2.0 / scale;
                foreach (var (shape, stroke, thickness) in hand.Parts)
                {
                    var pen = new Pen(haloBrush, stroke ? thickness + extra : extra) { LineJoin = PenLineJoin.Round };
                    context.DrawGeometry(null, pen, shape);
                }
            }

            foreach (var (shape, stroke, thickness) in hand.Parts)
                context.DrawGeometry(stroke ? null : brush, stroke ? new Pen(brush, thickness) : null, shape);
        }
    }
}

