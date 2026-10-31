using Avalonia;
using Avalonia.Controls;

namespace DaisyOS.Shell.Controls;

public partial class BatteryStatusView : UserControl
{
    public static readonly StyledProperty<int> ChargePercentProperty =
        AvaloniaProperty.Register<BatteryStatusView, int>(nameof(ChargePercent));
    public static readonly StyledProperty<string> StateTextProperty =
        AvaloniaProperty.Register<BatteryStatusView, string>(nameof(StateText), string.Empty);
    public static readonly StyledProperty<bool> ShowStateProperty =
        AvaloniaProperty.Register<BatteryStatusView, bool>(nameof(ShowState));
    public static readonly StyledProperty<bool> IsChargingProperty =
        AvaloniaProperty.Register<BatteryStatusView, bool>(nameof(IsCharging));
    public static readonly StyledProperty<double> IconWidthProperty =
        AvaloniaProperty.Register<BatteryStatusView, double>(nameof(IconWidth), 24);
    public static readonly StyledProperty<double> IconHeightProperty =
        AvaloniaProperty.Register<BatteryStatusView, double>(nameof(IconHeight), 18);
    public static readonly StyledProperty<double> LabelFontSizeProperty =
        AvaloniaProperty.Register<BatteryStatusView, double>(nameof(LabelFontSize), 13);

    static BatteryStatusView()
    {
        ChargePercentProperty.Changed.AddClassHandler<BatteryStatusView>((view, _) => view.UpdatePresentation());
    }

    public BatteryStatusView()
    {
        InitializeComponent();
        UpdatePresentation();
    }

    public int ChargePercent
    {
        get => GetValue(ChargePercentProperty);
        set => SetValue(ChargePercentProperty, Math.Clamp(value, 0, 100));
    }
    public string StateText { get => GetValue(StateTextProperty); set => SetValue(StateTextProperty, value); }
    public bool ShowState { get => GetValue(ShowStateProperty); set => SetValue(ShowStateProperty, value); }
    public bool IsCharging { get => GetValue(IsChargingProperty); set => SetValue(IsChargingProperty, value); }
    public double IconWidth { get => GetValue(IconWidthProperty); set => SetValue(IconWidthProperty, value); }
    public double IconHeight { get => GetValue(IconHeightProperty); set => SetValue(IconHeightProperty, value); }
    public double LabelFontSize { get => GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }

    private void UpdatePresentation()
    {
        if (this.FindControl<TextBlock>("PercentText") is { } percent)
            percent.Text = $"{Math.Clamp(ChargePercent, 0, 100)}%";
    }

    internal static bool IsChargingState(string? state) =>
        string.Equals(state?.Trim(), "Charging", StringComparison.OrdinalIgnoreCase);
}
