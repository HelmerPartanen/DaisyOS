using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DaisyOS.Shell.Controls;

/// <summary>Renders and independently colors the three bands from wifi-02.svg.</summary>
public sealed class WifiSignalIcon : Viewbox
{
    public static readonly StyledProperty<int> SignalPercentProperty =
        AvaloniaProperty.Register<WifiSignalIcon, int>(nameof(SignalPercent));
    public static readonly StyledProperty<IBrush?> ActiveBrushProperty =
        AvaloniaProperty.Register<WifiSignalIcon, IBrush?>(nameof(ActiveBrush));
    public static readonly StyledProperty<IBrush?> InactiveBrushProperty =
        AvaloniaProperty.Register<WifiSignalIcon, IBrush?>(nameof(InactiveBrush));
    public static readonly StyledProperty<double> InactiveBandOpacityProperty =
        AvaloniaProperty.Register<WifiSignalIcon, double>(nameof(InactiveBandOpacity), 1d);

    private readonly DaisyIcon _outerBand;
    private readonly DaisyIcon _innerBand;
    private readonly DaisyIcon _dot;

    static WifiSignalIcon()
    {
        SignalPercentProperty.Changed.AddClassHandler<WifiSignalIcon>((icon, _) => icon.UpdateBandBrushes());
        ActiveBrushProperty.Changed.AddClassHandler<WifiSignalIcon>((icon, _) => icon.UpdateBandBrushes());
        InactiveBrushProperty.Changed.AddClassHandler<WifiSignalIcon>((icon, _) => icon.UpdateBandBrushes());
        InactiveBandOpacityProperty.Changed.AddClassHandler<WifiSignalIcon>((icon, _) => icon.UpdateBandBrushes());
    }

    public WifiSignalIcon()
    {
        Width = 18;
        Height = 18;
        Stretch = Stretch.Uniform;

        _outerBand = CreateBand(new Rect(0, 0, 22, 6.5));
        _innerBand = CreateBand(new Rect(0, 6.5, 22, 5));
        _dot = CreateBand(new Rect(0, 11.5, 22, 5.5));
        Child = new Canvas { Width = 22, Height = 17, Children = { _outerBand, _innerBand, _dot } };
    }

    public int SignalPercent
    {
        get => GetValue(SignalPercentProperty);
        set => SetValue(SignalPercentProperty, Math.Clamp(value, 0, 100));
    }

    public IBrush? ActiveBrush
    {
        get => GetValue(ActiveBrushProperty);
        set => SetValue(ActiveBrushProperty, value);
    }

    public IBrush? InactiveBrush
    {
        get => GetValue(InactiveBrushProperty);
        set => SetValue(InactiveBrushProperty, value);
    }

    public double InactiveBandOpacity
    {
        get => GetValue(InactiveBandOpacityProperty);
        set => SetValue(InactiveBandOpacityProperty, Math.Clamp(value, 0, 1));
    }

    private static DaisyIcon CreateBand(Rect clip) => new()
    {
        Icon = "wifi-02",
        Width = 22,
        Height = 17,
        Clip = new RectangleGeometry(clip)
    };

    private void UpdateBandBrushes()
    {
        var signal = SignalPercent;
        SetBandBrush(_dot, signal > 0);
        SetBandBrush(_innerBand, signal >= 34);
        SetBandBrush(_outerBand, signal >= 67);
    }

    private void SetBandBrush(DaisyIcon band, bool isActive)
    {
        var brush = isActive ? ActiveBrush : InactiveBrush;
        if (brush is null) return;
        band.Foreground = brush;
        band.Opacity = isActive ? 1 : InactiveBandOpacity;
    }
}
