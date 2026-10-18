using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace DaisyOS.Shell.DesktopWidgets;

public partial class ClockWidget : UserControl
{
    public ClockWidget()
    {
        InitializeComponent();
    }

    public void SetGridSize(int columnSpan, int rowSpan)
    {
        var container = this.FindControl<DaisyOS.Shell.Controls.WidgetContainer>("Container");
        if (container != null)
        {
            container.ColumnSpan = columnSpan;
            container.RowSpan = rowSpan;
        }
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}

/// <summary>
/// Cupertino-inspired animated clock face with 60 equidistant second tick marks
/// distributed along an inset rounded rectangular track that matches the widget's corner curvature,
/// rendered either with a second-sweep opacity gradient or SVG analog hands.
/// </summary>
public sealed partial class ClockFace : Control
{
    public static readonly StyledProperty<bool> IsAnalogProperty =
        AvaloniaProperty.Register<ClockFace, bool>(nameof(IsAnalog));

    static ClockFace() => AffectsRender<ClockFace>(IsAnalogProperty, IsGlowProperty);

    public bool IsAnalog
    {
        get => GetValue(IsAnalogProperty);
        set => SetValue(IsAnalogProperty, value);
    }

    private App? _app;
    private bool _attached;
    private DateTime _now;
    private readonly DispatcherTimer _timer;
    private string _time = "";
    private double _currentSecond = 0;

    // Distance from the container edge to the center line of the tick track.
    private const double TickInset = 7.0;

    // The content surface has a 30px inner corner. Insetting the tick centerline
    // by 7px yields a 23px track radius, keeping the marks concentric with the
    // shared 32px widget shell instead of pinching them into small corners.
    private const double TickCornerRadius = 23.0;

    // Length of each tick mark.
    private const double TickLength = 6.0;

    // Thickness of each tick mark stroke.
    private const double TickThickness = 2.0;

    private static readonly Typeface TimeTypeface =
        new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    // Preallocated pen lookups for 0..255 alpha values to avoid 3,600 allocations per second
    private static readonly Pen[] LightPens = CreatePenLut(Color.FromRgb(0, 0, 0));
    private static readonly Pen[] DarkPens = CreatePenLut(Color.FromRgb(255, 255, 255));

    private static Pen[] CreatePenLut(Color baseColor)
    {
        var lut = new Pen[256];
        for (int a = 0; a < 256; a++)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)a, baseColor.R, baseColor.G, baseColor.B));
            lut[a] = new Pen(brush, TickThickness) { LineCap = PenLineCap.Round };
        }
        return lut;
    }

    // Cached geometry for the 60 ticks (recalculated only when Bounds change)
    private readonly (Point Start, Point End)[] _cachedTicks = new (Point Start, Point End)[60];
    private double _cachedWidth = -1;
    private double _cachedHeight = -1;

    // Cached typography
    private FormattedText? _cachedFormattedText;
    private string? _cachedFormattedTime;
    private double _cachedFormattedScale = -1;
    private Color _cachedFormattedColor = Colors.Transparent;

    public ClockFace()
    {
        ClipToBounds = true;

        UpdateTime();

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            // The upcoming tick brightens throughout the second. A one-second
            // timer sampled only the endpoints and made that sweep look binary.
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _timer.Tick += (_, _) => UpdateTime();
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            _timer.Stop();
            if (_app is not null)
            {
                _app.DesktopOcclusionChanged -= OnDesktopActivityChanged;
                _app.WindowTracker.OcclusionStateChanged -= OnDesktopActivityChanged;
                _app.SessionState.Changed -= OnDesktopActivityChanged;
                _app = null;
            }
        };
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            _app = Application.Current as App;
            if (_app is not null)
            {
                _app.DesktopOcclusionChanged += OnDesktopActivityChanged;
                _app.WindowTracker.OcclusionStateChanged += OnDesktopActivityChanged;
                _app.SessionState.Changed += OnDesktopActivityChanged;
            }
            OnDesktopActivityChanged(null, EventArgs.Empty);
        };
    }

    private void OnDesktopActivityChanged(object? sender, EventArgs e) =>
        SetDesktopActivitySuspended(_app is not null &&
            (_app.SessionState.GamingMode || _app.IsDesktopOccludedByExternalApp ||
             _app.WindowTracker.IsAnyWindowMaximizedOrFullScreen || _app.IsLockScreenActive));

    internal bool IsAnimationRunning => _timer.IsEnabled;

    internal void SetDesktopActivitySuspended(bool suspended)
    {
        if (!_attached || suspended) _timer.Stop();
        else if (!_timer.IsEnabled)
        {
            UpdateTime();
            _timer.Start();
        }
    }

    private void UpdateTime()
    {
        var now = DateTime.Now;
        _now = now;
        _time = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        _currentSecond = now.Second + now.Millisecond / 1000.0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;

        if (width <= 0 || height <= 0)
            return;

        bool isLight = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;
        var foreColor = isLight ? Color.FromRgb(0, 0, 0) : Color.FromRgb(255, 255, 255);
        var pens = isLight ? LightPens : DarkPens;

        var center = new Point(width * 0.5, height * 0.5);

        EnsureTickGeometry(width, height, center);
        if (IsGlow)
        {
            DrawGlow(context, center, width, height, _now);
            return;
        }
        if (IsAnalog)
        {
            DrawAnalog(context, center, width, height, _now);
            return;
        }
        DrawSecondMarks(context, _currentSecond, pens);
        DrawText(context, center, height, foreColor);
    }

    private void EnsureTickGeometry(double width, double height, Point center)
    {
        if (Math.Abs(_cachedWidth - width) < 0.1 && Math.Abs(_cachedHeight - height) < 0.1)
        {
            return;
        }

        _cachedWidth = width;
        _cachedHeight = height;

        var inset = TickInset;
        var radius = TickCornerRadius;

        var left = inset;
        var top = inset;
        var right = width - inset;
        var bottom = height - inset;

        radius = Math.Min(radius, Math.Min(right - left, bottom - top) * 0.5);

        double w = right - left;
        double h = bottom - top;
        double arcLength = Math.PI * 0.5 * radius;
        double totalPerimeter = 2.0 * (w - 2.0 * radius) + 2.0 * (h - 2.0 * radius) + 4.0 * arcLength;

        for (var second = 0; second < 60; second++)
        {
            double s = second * (totalPerimeter / 60.0);
            var (hit, _) = GetPointAndNormalOnRoundedRect(s, left, top, right, bottom, radius, center.X);

            double radialX = hit.X - center.X;
            double radialY = hit.Y - center.Y;
            double radialLength = Math.Sqrt(radialX * radialX + radialY * radialY);
            var direction = radialLength > 1e-6
                ? new Vector(radialX / radialLength, radialY / radialLength)
                : new Vector(0, -1);

            var halfLength = TickLength * 0.5;
            _cachedTicks[second] = (hit - direction * halfLength, hit + direction * halfLength);
        }
    }

    private void DrawSecondMarks(DrawingContext context, double currentSecond, Pen[] pens)
    {
        for (var second = 0; second < 60; second++)
        {
            double d = (currentSecond - second) % 60.0;
            if (d < 0) d += 60.0;

            double opacity;
            if (d < 59.0)
            {
                // Trailing comet tail: fades from full brightness down to the
                // floor over the 59 seconds since this tick was last "current".
                opacity = 0.20 + 0.90 * (1.0 - (d / 59.0));
            }
            else
            {
                // Anticipation ramp: during the final second before this tick
                // becomes "current" again, smoothly brighten it back up to full
                // opacity instead of sitting at the floor and popping instantly.
                double t = d - 59.0;
                t = t * t * (3.0 - 2.0 * t);
                opacity = 0.20 + 0.90 * t;
            }

            int alpha = Math.Clamp((int)Math.Round(opacity * 255.0), 25, 255);
            var (start, end) = _cachedTicks[second];
            context.DrawLine(pens[alpha], start, end);
        }
    }

    private static (Point Point, Vector Normal) GetPointAndNormalOnRoundedRect(
        double s,
        double left,
        double top,
        double right,
        double bottom,
        double radius,
        double centerX)
    {
        double w = right - left;
        double h = bottom - top;
        radius = Math.Max(2.0, Math.Min(radius, Math.Min(w, h) * 0.5));

        double topStraightHalf = (w - 2.0 * radius) * 0.5;
        double arcLength = Math.PI * 0.5 * radius;
        double verticalStraight = h - 2.0 * radius;
        double horizontalStraight = w - 2.0 * radius;

        double P = 2.0 * (w - 2.0 * radius) + 2.0 * (h - 2.0 * radius) + 4.0 * arcLength;
        s = ((s % P) + P) % P;

        // 1. Right half of top straight edge: [centerX, right - radius]
        if (s < topStraightHalf)
        {
            return (new Point(centerX + s, top), new Vector(0, -1));
        }
        s -= topStraightHalf;

        // 2. Top-right corner arc (sweeping from -90 deg to 0 deg)
        if (s < arcLength)
        {
            double angle = -Math.PI * 0.5 + (s / radius);
            var circleCenter = new Point(right - radius, top + radius);
            var pt = new Point(circleCenter.X + radius * Math.Cos(angle), circleCenter.Y + radius * Math.Sin(angle));
            return (pt, new Vector(Math.Cos(angle), Math.Sin(angle)));
        }
        s -= arcLength;

        // 3. Right vertical straight edge: [top + radius, bottom - radius]
        if (s < verticalStraight)
        {
            return (new Point(right, top + radius + s), new Vector(1, 0));
        }
        s -= verticalStraight;

        // 4. Bottom-right corner arc (sweeping from 0 deg to 90 deg)
        if (s < arcLength)
        {
            double angle = 0.0 + (s / radius);
            var circleCenter = new Point(right - radius, bottom - radius);
            var pt = new Point(circleCenter.X + radius * Math.Cos(angle), circleCenter.Y + radius * Math.Sin(angle));
            return (pt, new Vector(Math.Cos(angle), Math.Sin(angle)));
        }
        s -= arcLength;

        // 5. Bottom horizontal straight edge: [right - radius, left + radius]
        if (s < horizontalStraight)
        {
            return (new Point(right - radius - s, bottom), new Vector(0, 1));
        }
        s -= horizontalStraight;

        // 6. Bottom-left corner arc (sweeping from 90 deg to 180 deg)
        if (s < arcLength)
        {
            double angle = Math.PI * 0.5 + (s / radius);
            var circleCenter = new Point(left + radius, bottom - radius);
            var pt = new Point(circleCenter.X + radius * Math.Cos(angle), circleCenter.Y + radius * Math.Sin(angle));
            return (pt, new Vector(Math.Cos(angle), Math.Sin(angle)));
        }
        s -= arcLength;

        // 7. Left vertical straight edge: [bottom - radius, top + radius]
        if (s < verticalStraight)
        {
            return (new Point(left, bottom - radius - s), new Vector(-1, 0));
        }
        s -= verticalStraight;

        // 8. Top-left corner arc (sweeping from 180 deg to 270 deg)
        if (s < arcLength)
        {
            double angle = Math.PI + (s / radius);
            var circleCenter = new Point(left + radius, top + radius);
            var pt = new Point(circleCenter.X + radius * Math.Cos(angle), circleCenter.Y + radius * Math.Sin(angle));
            return (pt, new Vector(Math.Cos(angle), Math.Sin(angle)));
        }
        s -= arcLength;

        // 9. Left half of top straight edge: [left + radius, centerX]
        return (new Point(left + radius + s, top), new Vector(0, -1));
    }

    // ============================================================
    // TYPOGRAPHY (VERTICALLY CENTERED WITH EQUAL PADDING)
    // ============================================================

    private void DrawText(
        DrawingContext context,
        Point center,
        double height,
        Color foreColor)
    {
        double scale = Math.Max(0.2, height / 180.0);

        if (_cachedFormattedText is null
            || _cachedFormattedTime != _time
            || Math.Abs(_cachedFormattedScale - scale) > 0.01
            || _cachedFormattedColor != foreColor)
        {
            _cachedFormattedTime = _time;
            _cachedFormattedScale = scale;
            _cachedFormattedColor = foreColor;
            _cachedFormattedText = new FormattedText(
                _time,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                TimeTypeface,
                68.0 * scale,
                new SolidColorBrush(foreColor));
        }

        var timeX = center.X - _cachedFormattedText.Width * 0.5;
        var timeY = center.Y - _cachedFormattedText.Height * 0.5;

        context.DrawText(_cachedFormattedText, new Point(timeX, timeY));
    }
}
