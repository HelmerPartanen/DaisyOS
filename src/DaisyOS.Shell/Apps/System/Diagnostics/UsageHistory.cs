using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

/// <summary>A bounded, two-minute history on a fixed 0–100% scale.</summary>
public sealed class UsageHistory : Control
{
    public static readonly StyledProperty<double[]?> SamplesProperty = AvaloniaProperty.Register<UsageHistory, double[]?>(nameof(Samples));
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<UsageHistory, IBrush?>(nameof(Stroke));
    public double[]? Samples { get => GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    static UsageHistory() => AffectsRender<UsageHistory>(SamplesProperty, StrokeProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var brush = Stroke ?? Brushes.DodgerBlue;
        var w = Bounds.Width; var h = Bounds.Height;
        using (context.PushOpacity(.12))
            for (var i = 0; i <= 4; i++) context.DrawLine(new Pen(brush, 1), new(0, i * h / 4), new(w, i * h / 4));
        if (Samples is not { Length: > 0 } samples) return;
        var points = samples.TakeLast(60).Select((v, i) => new Point(w - (samples.Length - 1 - i) * w / 59, h - Math.Clamp(v, 0, 100) * h / 100)).ToArray();
        if (points.Length == 1) { context.DrawEllipse(brush, null, points[0], 2, 2); return; }
        var area = new StreamGeometry();
        using (var g = area.Open()) { g.BeginFigure(new(points[0].X, h), true); foreach (var point in points) g.LineTo(point); g.LineTo(new(w, h)); g.EndFigure(true); }
        using (context.PushOpacity(.10)) context.DrawGeometry(brush, null, area);
        for (var i = 1; i < points.Length; i++) context.DrawLine(new Pen(brush, 2), points[i - 1], points[i]);
    }
}
