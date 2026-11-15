using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

/// <summary>A timestamped two-minute chart; missing samples and suspension gaps remain blank.</summary>
public sealed class UsageHistory : Control
{
    public static readonly StyledProperty<HistoryPoint[]?> SamplesProperty = AvaloniaProperty.Register<UsageHistory, HistoryPoint[]?>(nameof(Samples));
    public static readonly StyledProperty<HistoryPoint[]?> SecondarySamplesProperty = AvaloniaProperty.Register<UsageHistory, HistoryPoint[]?>(nameof(SecondarySamples));
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<UsageHistory, IBrush?>(nameof(Stroke));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<UsageHistory, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<DateTimeOffset> EndTimeProperty = AvaloniaProperty.Register<UsageHistory, DateTimeOffset>(nameof(EndTime));
    public HistoryPoint[]? Samples { get => GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public HistoryPoint[]? SecondarySamples { get => GetValue(SecondarySamplesProperty); set => SetValue(SecondarySamplesProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public DateTimeOffset EndTime { get => GetValue(EndTimeProperty); set => SetValue(EndTimeProperty, value); }
    static UsageHistory() => AffectsRender<UsageHistory>(SamplesProperty, SecondarySamplesProperty, StrokeProperty, MaximumProperty, EndTimeProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var brush = Stroke ?? Brushes.Gray;
        var all = (Samples ?? []).Concat(SecondarySamples ?? []).ToArray();
        var end = EndTime == default ? all.LastOrDefault()?.Time ?? DateTimeOffset.UtcNow : EndTime;
        var start = end.AddMinutes(-2);
        var maximum = Maximum > 0 ? Maximum : Math.Max(1, all.Where(p => p.Time >= start && p.Time <= end).Select(p => p.Value ?? 0).DefaultIfEmpty().Max());
        var pen = new Pen(brush, 1);
        using (context.PushClip(new Rect(Bounds.Size)))
        {
            using (context.PushOpacity(.12))
                for (var i = 0; i <= 4; i++) context.DrawLine(pen, new(0, i * Bounds.Height / 4), new(Bounds.Width, i * Bounds.Height / 4));
            Draw(Samples, false); Draw(SecondarySamples, true);
        }
        void Draw(HistoryPoint[]? samples, bool secondary)
        {
            if (samples is null) return;
            var segment = new List<Point>(); DateTimeOffset? previous = null;
            foreach (var sample in samples.Where(p => p.Time >= start && p.Time <= end))
            {
                if (sample.Value is not { } value || !double.IsFinite(value)) { Flush(); previous = null; continue; }
                if (previous is { } time && (sample.Time - time).TotalSeconds > 5) Flush();
                segment.Add(new((sample.Time - start).TotalSeconds / 120 * Bounds.Width, Bounds.Height * (1 - Math.Clamp(value / maximum, 0, 1))));
                previous = sample.Time;
            }
            Flush();
            void Flush()
            {
                if (segment.Count == 0) return;
                if (segment.Count == 1) context.DrawEllipse(brush, null, segment[0], 2, 2);
                else
                {
                    var line = new StreamGeometry();
                    using (var g = line.Open()) { g.BeginFigure(segment[0], false); foreach (var point in segment.Skip(1)) g.LineTo(point); g.EndFigure(false); }
                    if (!secondary)
                    {
                        var area = new StreamGeometry();
                        using (var g = area.Open()) { g.BeginFigure(new(segment[0].X, Bounds.Height), true); foreach (var point in segment) g.LineTo(point); g.LineTo(new(segment[^1].X, Bounds.Height)); g.EndFigure(true); }
                        using (context.PushOpacity(.10)) context.DrawGeometry(brush, null, area);
                    }
                    using (context.PushOpacity(secondary ? .65 : 1))
                        context.DrawGeometry(null, new Pen(brush, 2, secondary ? DashStyle.Dash : null), line);
                }
                segment.Clear();
            }
        }
    }
}
