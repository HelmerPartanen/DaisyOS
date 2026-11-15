using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

/// <summary>A consistently styled resource summary with a bounded usage history.</summary>
public partial class MonitorMetricCard : UserControl
{
    public MonitorMetricCard() => InitializeComponent();
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<MonitorMetricCard, string>(nameof(Title));
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public static readonly StyledProperty<string> IconProperty = AvaloniaProperty.Register<MonitorMetricCard, string>(nameof(Icon));
    public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public static readonly StyledProperty<string> ValueProperty = AvaloniaProperty.Register<MonitorMetricCard, string>(nameof(Value));
    public string Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public static readonly StyledProperty<string> DetailProperty = AvaloniaProperty.Register<MonitorMetricCard, string>(nameof(Detail));
    public string Detail { get => GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public static readonly StyledProperty<string> AssessmentProperty = AvaloniaProperty.Register<MonitorMetricCard, string>(nameof(Assessment));
    public string Assessment { get => GetValue(AssessmentProperty); set => SetValue(AssessmentProperty, value); }
    public static readonly StyledProperty<HistoryPoint[]?> SamplesProperty = AvaloniaProperty.Register<MonitorMetricCard, HistoryPoint[]?>(nameof(Samples));
    public HistoryPoint[]? Samples { get => GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<MonitorMetricCard, IBrush?>(nameof(Accent));
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public static readonly StyledProperty<HistoryPoint[]?> SecondarySamplesProperty = AvaloniaProperty.Register<MonitorMetricCard, HistoryPoint[]?>(nameof(SecondarySamples));
    public HistoryPoint[]? SecondarySamples { get => GetValue(SecondarySamplesProperty); set => SetValue(SecondarySamplesProperty, value); }
    public static readonly StyledProperty<DateTimeOffset> EndTimeProperty = AvaloniaProperty.Register<MonitorMetricCard, DateTimeOffset>(nameof(EndTime));
    public DateTimeOffset EndTime { get => GetValue(EndTimeProperty); set => SetValue(EndTimeProperty, value); }
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<MonitorMetricCard, double>(nameof(Maximum), 100);
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
}
