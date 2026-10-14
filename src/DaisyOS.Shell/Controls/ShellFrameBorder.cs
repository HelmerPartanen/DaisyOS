using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DaisyOS.Shell.Controls;

/// <summary>Supersamples decorative chrome without caching or resampling its content.</summary>
public sealed class ShellFrameBorder : Border
{
    public ShellFrameBorder()
    {
        RenderOptions.SetEdgeMode(this, EdgeMode.Antialias);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }
    private RenderTargetBitmap? _frame;
    private FrameKey? _key;
    private readonly record struct FrameKey(Size Size, double Scale, IBrush? Background,
        IBrush? Stroke, Thickness Thickness, CornerRadius Radius, BackgroundSizing Sizing, BoxShadows Shadow);
    protected override Type StyleKeyOverride => typeof(Border);

    /// <summary>Prepares static chrome before a reveal starts its animation clock.</summary>
    internal void PrepareFrame()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var scale = (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) * 2;
        var key = new FrameKey(Bounds.Size, scale, Background?.ToImmutable(), BorderBrush?.ToImmutable(),
            BorderThickness, CornerRadius, BackgroundSizing, BoxShadow);
        if (_frame is null || _key != key)
        {
            _frame?.Dispose();
            var border = new Border
            {
                Background = key.Background, BorderBrush = key.Stroke, BorderThickness = key.Thickness,
                CornerRadius = key.Radius, BackgroundSizing = key.Sizing
            };
            border.Measure(key.Size);
            border.Arrange(new Rect(key.Size));
            RenderOptions.SetEdgeMode(border, EdgeMode.Antialias);
            _frame = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(key.Size.Width * scale),
                (int)Math.Ceiling(key.Size.Height * scale)), new Vector(96 * scale, 96 * scale));
            _frame.Render(border);
            _key = key;
        }
    }

    public override void Render(DrawingContext context)
    {
        PrepareFrame();
        if (_frame is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        // Keep shadows vector-rendered so they extend beyond the cached frame bounds.
        if (BoxShadow.Count > 0)
            context.DrawRectangle(null, null, new RoundedRect(new Rect(Bounds.Size), CornerRadius), BoxShadow);
        // This backend's bitmap drawing source rectangle is in physical pixels.
        // Using the DPI-normalized image Size would crop the supersampled frame.
        context.DrawImage(_frame, new Rect(0, 0, _frame.PixelSize.Width, _frame.PixelSize.Height), new Rect(Bounds.Size));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _frame?.Dispose();
        _frame = null;
        _key = null;
        base.OnDetachedFromVisualTree(e);
    }
}
