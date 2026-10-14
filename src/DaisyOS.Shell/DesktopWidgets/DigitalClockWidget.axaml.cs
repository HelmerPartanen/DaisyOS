using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace DaisyOS.Shell.DesktopWidgets;

public partial class DigitalClockWidget : UserControl
{
    public DigitalClockWidget()
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
/// Retro-modern 7-segment digital clock face arranged in a 2x2 stacked matrix
/// (hours on top, minutes on bottom) with rounded pill segments and faint ghosted
/// inactive segments.
/// </summary>
public sealed class DigitalClockFace : Control
{
    private readonly DispatcherTimer _timer;
    private int _hourTens;
    private int _hourUnits;
    private int _minuteTens;
    private int _minuteUnits;

    // Segment masks for digits 0-9 (bits 0..6 correspond to segments A..G)
    // 0: A(top), 1: B(top-right), 2: C(bottom-right), 3: D(bottom),
    // 4: E(bottom-left), 5: F(top-left), 6: G(middle)
    private static readonly byte[] DigitMasks =
    [
        0x3F, // 0: A, B, C, D, E, F
        0x06, // 1: B, C
        0x5B, // 2: A, B, D, E, G
        0x4F, // 3: A, B, C, D, G
        0x66, // 4: B, C, F, G
        0x6D, // 5: A, C, D, F, G
        0x7D, // 6: A, C, D, E, F, G
        0x07, // 7: A, B, C
        0x7F, // 8: A, B, C, D, E, F, G
        0x6F  // 9: A, B, C, D, F, G
    ];

    private static readonly SolidColorBrush LightActiveBrush = new(Color.FromRgb(15, 15, 15));
    private static readonly SolidColorBrush LightInactiveBrush = new(Color.FromArgb(28, 0, 0, 0));

    private static readonly SolidColorBrush DarkActiveBrush = new(Color.FromRgb(255, 255, 255));
    private static readonly SolidColorBrush DarkInactiveBrush = new(Color.FromArgb(32, 255, 255, 255));

    // Design-time canonical dimensions for tall, elegant, super-readable 7-segment digits
    private const double SegmentThickness = 12.0;
    private const double SegmentRadius = 6.0;
    private const double SegmentGap = 2.5;
    private const double HorizontalSegmentWidth = 30.0;
    private const double VerticalSegmentHeight = 33.0;
    private const double ColumnGap = 20.0;
    private const double RowGap = 12.0;

    // Single digit dimensions
    private const double DigitWidth = 2 * SegmentThickness + HorizontalSegmentWidth + 2 * SegmentGap; // 59.0
    private const double DigitHeight = 3 * SegmentThickness + 2 * VerticalSegmentHeight + 4 * SegmentGap; // 112.0

    // Total 2x2 cluster dimensions
    private const double TotalDesignWidth = 2 * DigitWidth + ColumnGap; // 138.0
    private const double TotalDesignHeight = 2 * DigitHeight + RowGap; // 236.0
    private const double WideDigitGap = 10.0;
    private const double WideGroupGap = 28.0;
    private const double WideDesignWidth = 4 * DigitWidth + 2 * WideDigitGap + WideGroupGap;

    public DigitalClockFace()
    {
        ClipToBounds = true;
        UpdateTime();

        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        _timer.Tick += (_, _) => UpdateTime();
        _timer.Start();

        ActualThemeVariantChanged += OnThemeChanged;
        DetachedFromVisualTree += OnDetached;
        AttachedToVisualTree += OnAttached;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UpdateTime();
        _timer.Start();

        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += OnThemeChanged;
            if (Application.Current is App app)
            {
                app.AppearanceChanged += OnThemeChanged;
            }
        }

        InvalidateVisual();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();

        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
            if (Application.Current is App app)
            {
                app.AppearanceChanged -= OnThemeChanged;
            }
        }
    }

    private bool IsLightMode()
    {
        var localVariant = ActualThemeVariant;
        if (localVariant == ThemeVariant.Light) return true;
        if (localVariant == ThemeVariant.Dark) return false;

        var appVariant = Application.Current?.ActualThemeVariant;
        if (appVariant == ThemeVariant.Light) return true;
        if (appVariant == ThemeVariant.Dark) return false;

        if (this.TryFindResource("ClockDialBrush", out var res) && res is ISolidColorBrush brush)
        {
            return (brush.Color.R * 0.299 + brush.Color.G * 0.587 + brush.Color.B * 0.114) > 128;
        }

        return false;
    }

    private void UpdateTime()
    {
        var now = DateTime.Now;
        int hours = now.Hour;
        int minutes = now.Minute;

        int h10 = hours / 10;
        int h1 = hours % 10;
        int m10 = minutes / 10;
        int m1 = minutes % 10;

        if (h10 != _hourTens || h1 != _hourUnits || m10 != _minuteTens || m1 != _minuteUnits)
        {
            _hourTens = h10;
            _hourUnits = h1;
            _minuteTens = m10;
            _minuteUnits = m1;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;

        if (width <= 0 || height <= 0)
            return;

        bool isLight = IsLightMode();
        var activeBrush = isLight ? LightActiveBrush : DarkActiveBrush;
        var inactiveBrush = isLight ? LightInactiveBrush : DarkInactiveBrush;

        // Fine-tuned padding so the digital clock fills the card height cleanly
        double padY = 10.0;
        double padX = 14.0;
        double availW = Math.Max(10.0, width - 2 * padX);
        double availH = Math.Max(10.0, height - 2 * padY);

        bool useWideLayout = width / height >= 1.85;
        double designWidth = useWideLayout ? WideDesignWidth : TotalDesignWidth;
        double designHeight = useWideLayout ? DigitHeight : TotalDesignHeight;
        double scale = Math.Min(availW / designWidth, availH / designHeight);

        double renderedW = designWidth * scale;
        double renderedH = designHeight * scale;

        double originX = (width - renderedW) * 0.5;
        double originY = (height - renderedH) * 0.5;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(originX, originY)))
        {
            if (useWideLayout)
            {
                double secondDigitX = DigitWidth + WideDigitGap;
                double minuteTensX = secondDigitX + DigitWidth + WideGroupGap;
                double minuteUnitsX = minuteTensX + DigitWidth + WideDigitGap;

                DrawDigit(context, _hourTens, 0, 0, activeBrush, inactiveBrush);
                DrawDigit(context, _hourUnits, secondDigitX, 0, activeBrush, inactiveBrush);
                DrawDigit(context, _minuteTens, minuteTensX, 0, activeBrush, inactiveBrush);
                DrawDigit(context, _minuteUnits, minuteUnitsX, 0, activeBrush, inactiveBrush);

                double colonX = secondDigitX + DigitWidth + WideGroupGap * 0.5;
                context.DrawEllipse(activeBrush, null, new Point(colonX, DigitHeight * 0.38), 4, 4);
                context.DrawEllipse(activeBrush, null, new Point(colonX, DigitHeight * 0.68), 4, 4);
            }
            else
            {
                DrawDigit(context, _hourTens, 0, 0, activeBrush, inactiveBrush);
                DrawDigit(context, _hourUnits, DigitWidth + ColumnGap, 0, activeBrush, inactiveBrush);
                DrawDigit(context, _minuteTens, 0, DigitHeight + RowGap, activeBrush, inactiveBrush);
                DrawDigit(context, _minuteUnits, DigitWidth + ColumnGap, DigitHeight + RowGap, activeBrush, inactiveBrush);
            }
        }
    }

    private static void DrawDigit(
        DrawingContext context,
        int digit,
        double offsetX,
        double offsetY,
        IBrush activeBrush,
        IBrush inactiveBrush)
    {
        byte mask = (digit >= 0 && digit <= 9) ? DigitMasks[digit] : (byte)0;

        IBrush GetBrush(int bit) => (mask & (1 << bit)) != 0 ? activeBrush : inactiveBrush;

        double midY = SegmentThickness + SegmentGap + VerticalSegmentHeight + SegmentGap;
        double botY = midY + SegmentThickness + SegmentGap + VerticalSegmentHeight + SegmentGap;
        double rightX = SegmentThickness + SegmentGap + HorizontalSegmentWidth + SegmentGap;
        double topVertY = SegmentThickness + SegmentGap;
        double botVertY = midY + SegmentThickness + SegmentGap;

        // Segment 0: Top Horizontal (A)
        context.DrawRectangle(
            GetBrush(0),
            null,
            new RoundedRect(
                new Rect(offsetX + SegmentThickness + SegmentGap, offsetY, HorizontalSegmentWidth, SegmentThickness),
                SegmentRadius,
                SegmentRadius));

        // Segment 1: Top-Right Vertical (B)
        context.DrawRectangle(
            GetBrush(1),
            null,
            new RoundedRect(
                new Rect(offsetX + rightX, offsetY + topVertY, SegmentThickness, VerticalSegmentHeight),
                SegmentRadius,
                SegmentRadius));

        // Segment 2: Bottom-Right Vertical (C)
        context.DrawRectangle(
            GetBrush(2),
            null,
            new RoundedRect(
                new Rect(offsetX + rightX, offsetY + botVertY, SegmentThickness, VerticalSegmentHeight),
                SegmentRadius,
                SegmentRadius));

        // Segment 3: Bottom Horizontal (D)
        context.DrawRectangle(
            GetBrush(3),
            null,
            new RoundedRect(
                new Rect(offsetX + SegmentThickness + SegmentGap, offsetY + botY, HorizontalSegmentWidth, SegmentThickness),
                SegmentRadius,
                SegmentRadius));

        // Segment 4: Bottom-Left Vertical (E)
        context.DrawRectangle(
            GetBrush(4),
            null,
            new RoundedRect(
                new Rect(offsetX, offsetY + botVertY, SegmentThickness, VerticalSegmentHeight),
                SegmentRadius,
                SegmentRadius));

        // Segment 5: Top-Left Vertical (F)
        context.DrawRectangle(
            GetBrush(5),
            null,
            new RoundedRect(
                new Rect(offsetX, offsetY + topVertY, SegmentThickness, VerticalSegmentHeight),
                SegmentRadius,
                SegmentRadius));

        // Segment 6: Middle Horizontal (G)
        context.DrawRectangle(
            GetBrush(6),
            null,
            new RoundedRect(
                new Rect(offsetX + SegmentThickness + SegmentGap, offsetY + midY, HorizontalSegmentWidth, SegmentThickness),
                SegmentRadius,
                SegmentRadius));
    }
}
