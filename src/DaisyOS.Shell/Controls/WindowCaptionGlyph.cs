using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace DaisyOS.Shell.Controls;

public enum WindowCaptionKind { Minimize, Maximize, Restore, Close }

// Keep these paths in sync with drawGlyph in native/daisy_window_decoration.
public sealed class WindowCaptionGlyph : Control
{
    public static readonly StyledProperty<WindowCaptionKind> KindProperty =
        AvaloniaProperty.Register<WindowCaptionGlyph, WindowCaptionKind>(nameof(Kind));
    public static readonly StyledProperty<double> GlyphSizeProperty =
        AvaloniaProperty.Register<WindowCaptionGlyph, double>(nameof(GlyphSize), 10);
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<WindowCaptionGlyph>();

    static WindowCaptionGlyph() => AffectsRender<WindowCaptionGlyph>(KindProperty, GlyphSizeProperty, ForegroundProperty);

    public WindowCaptionGlyph() { Width = 16; Height = 16; IsHitTestVisible = false; }
    public WindowCaptionKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double GlyphSize { get => GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var pen = new Pen(Foreground, 1.45, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var size = global::System.Math.Min(global::System.Math.Min(Bounds.Width, Bounds.Height), GlyphSize) / 2;
        var left = center.X - size;
        var right = center.X + size;
        var top = center.Y - size;
        var bottom = center.Y + size;
        if (Kind == WindowCaptionKind.Minimize)
            context.DrawLine(pen, new Point(left, center.Y), new Point(right, center.Y));
        else if (Kind == WindowCaptionKind.Close)
        {
            context.DrawLine(pen, new Point(left, top), new Point(right, bottom));
            context.DrawLine(pen, new Point(left, bottom), new Point(right, top));
        }
        else
        {
            context.DrawRectangle(null, pen, new Rect(left, top, size * 2, size * 2));
            if (Kind == WindowCaptionKind.Restore)
            {
                context.DrawLine(pen, new Point(left + 2, top - 2), new Point(right + 2, top - 2));
                context.DrawLine(pen, new Point(right + 2, top - 2), new Point(right + 2, bottom - 2));
            }
        }
    }
}
