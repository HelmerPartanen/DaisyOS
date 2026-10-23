using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace DaisyOS.Shell.Controls;

/// <summary>Draws the original Material Wi-Fi geometry with individually colored signal bands.</summary>
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

    private readonly ShapePath _outerBand;
    private readonly ShapePath _innerBand;
    private readonly ShapePath _dot;

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

        _outerBand = CreateBand("M715-759Q826-718 914-643q20 17 21 42t-17 43q-17 17-42 17.5T831-556q-72-59-161.5-91.5T480-680q-100 0-189.5 32.5T129-556q-20 16-45 15.5T42-558q-18-18-17-43t21-42q88-75 198.5-116T480-800q125 0 235.5 41Z");
        _innerBand = CreateBand("M622.5-536Q690-512 745-470q20 15 20.5 39.5T748-388q-17 17-42 17.5T661-384q-38-26-84-41t-97-15q-51 0-97 15t-84 41q-20 14-45 13t-42-18q-17-18-17-42.5t20-39.5q55-42 122.5-65.5T480-560q75 0 142.5 24Z");
        _dot = CreateBand("M409-149q-29-29-29-71t29-71q29-29 71-29t71 29q29 29 29 71t-29 71q-29 29-71 29t-71-29Z");

        var canvas = new Canvas { Width = 960, Height = 960 };
        canvas.Children.Add(_outerBand);
        canvas.Children.Add(_innerBand);
        canvas.Children.Add(_dot);
        Child = canvas;
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

    private static ShapePath CreateBand(string data) =>
        new()
        {
            Data = Geometry.Parse(data),
            RenderTransform = new TranslateTransform(0, 960),
        };

    private void UpdateBandBrushes()
    {
        var signal = SignalPercent;
        SetBandBrush(_dot, signal > 0);
        SetBandBrush(_innerBand, signal >= 34);
        SetBandBrush(_outerBand, signal >= 67);
    }

    private void SetBandBrush(Shape band, bool isActive)
    {
        var brush = isActive ? ActiveBrush : InactiveBrush;
        if (brush is null)
        {
            return;
        }

        band.Fill = brush;
        band.Opacity = isActive ? 1 : InactiveBandOpacity;
    }
}
