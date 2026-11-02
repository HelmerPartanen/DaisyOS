using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;

namespace DaisyOS.Shell.Controls;

/// <summary>Renders the three independently colored bands of the system Wi-Fi glyph.</summary>
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

    private readonly Path _outerBand;
    private readonly Path _innerBand;
    private readonly Path _dot;

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

        // These are the individual paths from the bundled wifi-02 glyph. Drawing
        // each path once avoids overlapping copies of the complete SVG, whose
        // per-layer clipping and opacity can be lost by the SVG renderer.
        _outerBand = CreateBand("M1 5.5C1 5.5 4 1 11 1C18 1 21 5.5 21 5.5", true);
        _innerBand = CreateBand("M5 9.5C5 9.5 7 7 11 7C15 7 17 9.5 17 9.5", true);
        _dot = CreateBand("M8.57969 14.0797L10.2929 15.7929C10.6834 16.1834 11.3166 16.1834 11.7071 15.7929L13.4203 14.0797C13.7574 13.7426 13.8042 13.2125 13.4415 12.9032C12.9508 12.4848 12.137 12 11 12C9.86297 12 9.04915 12.4848 8.55854 12.9032C8.19584 13.2125 8.24262 13.7426 8.57969 14.0797Z", false);
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

    private static Path CreateBand(string data, bool stroked) => new()
    {
        Data = Geometry.Parse(data),
        Width = 22,
        Height = 17,
        Stretch = Stretch.None,
        StrokeThickness = stroked ? 2 : 0,
        StrokeLineCap = PenLineCap.Round,
        IsHitTestVisible = false
    };

    private void UpdateBandBrushes()
    {
        var signal = SignalPercent;
        SetBandBrush(_dot, signal > 0, stroked: false);
        SetBandBrush(_innerBand, signal >= 34, stroked: true);
        SetBandBrush(_outerBand, signal >= 67, stroked: true);
    }

    private void SetBandBrush(Path band, bool isActive, bool stroked)
    {
        var brush = isActive ? ActiveBrush : InactiveBrush;
        if (stroked) band.Stroke = brush;
        else band.Fill = brush;
        band.Opacity = isActive ? 1 : InactiveBandOpacity;
    }
}
