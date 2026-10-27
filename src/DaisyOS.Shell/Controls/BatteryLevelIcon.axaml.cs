using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DaisyOS.Shell.Controls;

internal enum BatteryLevelSeverity
{
    Normal,
    Low,
    Critical
}

public partial class BatteryLevelIcon : UserControl
{
    // Optically inset from the antialiased inside edge of the 1.5px outline.
    private const double InteriorWidth = 18.5;

    public static readonly StyledProperty<int> PercentageProperty =
        AvaloniaProperty.Register<BatteryLevelIcon, int>(nameof(Percentage));

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<BatteryLevelIcon, IBrush?>(nameof(FillBrush));

    public static readonly StyledProperty<IBrush?> OutlineBrushProperty =
        AvaloniaProperty.Register<BatteryLevelIcon, IBrush?>(nameof(OutlineBrush));

    static BatteryLevelIcon()
    {
        PercentageProperty.Changed.AddClassHandler<BatteryLevelIcon>((icon, _) => icon.UpdateFill());
    }

    public BatteryLevelIcon()
    {
        InitializeComponent();
        UpdateFill();
    }

    public int Percentage
    {
        get => GetValue(PercentageProperty);
        set => SetValue(PercentageProperty, Math.Clamp(value, 0, 100));
    }

    public IBrush? FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public IBrush? OutlineBrush
    {
        get => GetValue(OutlineBrushProperty);
        set => SetValue(OutlineBrushProperty, value);
    }

    internal static BatteryLevelSeverity GetSeverity(int percentage) => Math.Clamp(percentage, 0, 100) switch
    {
        < 10 => BatteryLevelSeverity.Critical,
        < 20 => BatteryLevelSeverity.Low,
        _ => BatteryLevelSeverity.Normal
    };

    internal static double GetFillWidth(int percentage) =>
        InteriorWidth * Math.Clamp(percentage, 0, 100) / 100d;

    private void UpdateFill()
    {
        if (this.FindControl<Border>("FillBar") is { } fill)
            fill.Width = GetFillWidth(Percentage);
    }
}
