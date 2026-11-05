using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Wallpaper;

namespace DaisyOS.Shell.Controls;

/// <summary>Noninteractive foreground above the existing accessible clock. Shares the wallpaper's image resource.</summary>
public sealed class DeptEffectPresenter : Control
{
    private Image? _wallpaper;
    private TextBlock? _clock;
    private Control? _area;
    private Func<Rect[]>? _glyphs;
    private Func<bool>? _allowed;
    private CancellationTokenSource? _request;
    private byte[][]? _masks;
    private Bitmap? _maskBitmap;
    private byte[]? _selectedMask;
    private readonly DeptEffectService _service = new();
    private readonly List<Bitmap> _retiredMasks = [];
    private string? _layoutKey;
    private double _placedTop = double.NaN;
    private double _baseMargin;
    private bool _enabled = true, _automatic = true;
    private int _strength = 15;
    private IImage? _source;
    private IBrush? _foregroundScrim;
    private Size _analyzedSize;

    public DeptEffectPresenter()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        LayoutUpdated += (_, _) => RefreshLayout();
        AttachedToVisualTree += (_, _) => ScheduleAnalysis();
        DetachedFromVisualTree += (_, _) => Stop();
    }

    public void Configure(Image wallpaper, TextBlock clock, Control area, Func<Rect[]> glyphs, Func<bool>? allowed = null, IBrush? foregroundScrim = null)
    {
        _wallpaper = wallpaper;
        _clock = clock;
        _area = area;
        _glyphs = glyphs;
        _allowed = allowed;
        _foregroundScrim = foregroundScrim;
        wallpaper.PropertyChanged += (_, e) =>
        {
            if (e.Property == Image.SourceProperty) ScheduleAnalysis();
            else if (e.Property == OpacityProperty) RefreshLayout();
        };
        SizeChanged += (_, _) => ScheduleAnalysis();
        ScheduleAnalysis();
    }

    public void SetPreferences(bool enabled, bool automatic, int strength)
    {
        strength = Math.Clamp(strength, 0, 30);
        if (_enabled == enabled && _automatic == automatic && _strength == strength) return;
        var needsAnalysis = _enabled != enabled || _automatic != automatic;
        _enabled = enabled;
        _automatic = automatic;
        _strength = Math.Clamp(strength, 0, 30);
        _layoutKey = null;
        if (needsAnalysis) ScheduleAnalysis();
        RefreshLayout();
    }

    private void Stop()
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = null;
        ClearMask();
        foreach (var retired in _retiredMasks) retired.Dispose();
        _retiredMasks.Clear();
        _masks = null;
        _source = null;
    }

    private void ScheduleAnalysis()
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = new CancellationTokenSource();
        var token = _request.Token;
        _masks = null;
        _source = null;
        _layoutKey = null;
        ClearMask();
        if (_area is not null && _baseMargin > 0) _area.Margin = new Thickness(0, _baseMargin, 0, 0);
        if (!_enabled || _wallpaper?.Source is not Bitmap || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        // Defer until layout and any image swap have settled. Capture only a small analysis image on the UI thread.
        Dispatcher.UIThread.Post(async () =>
        {
            if (token.IsCancellationRequested || _wallpaper.Source is not Bitmap source) return;
            try
            {
                _source = source;
                _analyzedSize = Bounds.Size;
                using var thumbnail = new RenderTargetBitmap(new PixelSize(DeptEffectService.OverlaySize, DeptEffectService.OverlaySize));
                using (var drawing = thumbnail.CreateDrawingContext())
                    drawing.DrawImage(source, Crop(source.Size, Bounds.Size), new Rect(0, 0, DeptEffectService.OverlaySize, DeptEffectService.OverlaySize));
                using var stream = new MemoryStream();
                thumbnail.Save(stream, PngBitmapEncoderOptions.Default);
                var masks = await _service.AnalyzeAsync(stream.ToArray(), _automatic, token);
                if (token.IsCancellationRequested) return;
                ApplyPreparedMasks(masks);
            }
            catch (Exception ex) { Console.Error.WriteLine($"[DeptEffect] Preview unavailable: {ex.Message}"); }
        }, DispatcherPriority.Background);
    }

    internal bool HasForeground => _maskBitmap is not null;

    internal void ApplyPreparedMasks(byte[][]? masks)
    {
        _request?.Cancel();
        _source = _wallpaper?.Source;
        _analyzedSize = Bounds.Size;
        _masks = masks;
        _layoutKey = null;
        RefreshLayout();
    }

    internal static Rect Crop(Size source, Size target)
    {
        var scale = Math.Max(target.Width / source.Width, target.Height / source.Height);
        var width = target.Width / scale;
        var height = target.Height / scale;
        return new Rect((source.Width - width) / 2, (source.Height - height) / 2, width, height);
    }

    private void ClearMask()
    {
        var retired = _maskBitmap;
        _maskBitmap = null;
        if (retired is not null) _ = RetireMaskAsync(retired);
        _selectedMask = null;
        InvalidateVisual();
    }

    private async Task RetireMaskAsync(Bitmap bitmap)
    {
        try
        {
            if (Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(this) is { } visual)
                await visual.Compositor.RequestCompositionBatchCommitAsync().Rendered.WaitAsync(TimeSpan.FromSeconds(5));
            bitmap.Dispose();
        }
        catch (Exception ex) when (ex is TimeoutException or ObjectDisposedException)
        {
            if (VisualRoot is null) bitmap.Dispose();
            else _retiredMasks.Add(bitmap);
        }
    }

    public void RefreshLayout()
    {
        if (_clock is null || _area is null) return;
        // Presence and media animations affect rendering, but never trigger inference or placement searches.
        InvalidateVisual();
        var origin = _clock.TranslatePoint(default, this);
        if (origin is null) return;
        var allowed = _allowed?.Invoke() ?? true;
        var key = $"{_clock.Text}|{_clock.FontSize}|{_clock.FontFamily}|{Bounds.Size}|{_strength}|{_enabled}|{allowed}|{origin.Value.X}|{_area.Bounds.Size}";
        if (key == _layoutKey && _area.Margin.Top.Equals(_placedTop)) return;
        _layoutKey = key;
        _baseMargin = Math.Clamp(Bounds.Height * 0.1, 24, 96);
        var previousTop = _area.Margin.Top;
        _placedTop = _baseMargin;
        _area.Margin = new Thickness(0, _baseMargin, 0, 0);
        ClearMask();
        if (!_enabled || !allowed || _strength == 0 || _masks is null || _glyphs is null || Bounds.Size != _analyzedSize) return;
        var bands = _glyphs();
        if (bands.Length == 0) return;
        var best = double.PositiveInfinity;
        var bestOffset = 0d;
        byte[]? selected = null;
        // Derive a stable resting origin even during the existing presence translation animation.
        var localOrigin = _clock.TranslatePoint(default, _area) ?? default;
        var xOrigin = origin.Value.X;
        var parentOrigin = (_area.Parent as Visual)?.TranslatePoint(_area.Bounds.Position, this);
        var yOrigin = (parentOrigin?.Y ?? previousTop) - previousTop + _baseMargin + localOrigin.Y;
        var glyphTop = bands.Min(b => b.Top);
        var glyphBottom = bands.Max(b => b.Bottom);
        var glyphHeight = glyphBottom - glyphTop;
        foreach (var mask in _masks)
            for (var offset = 0d; offset <= Math.Min(Bounds.Height * 0.22, 220); offset += 2)
            {
                if (yOrigin + offset + glyphBottom > Bounds.Height * 0.48) continue;
                var overlap = MeasureOverlap(mask, bands, xOrigin, yOrigin + offset, Bounds.Size, glyphTop + glyphHeight * 0.55);
                if (overlap.Total < 0.02 || overlap.Total > Math.Min(0.30, _strength / 100d + 0.04) || overlap.Upper > 0.015 || overlap.WorstDigit > 0.40) continue;
                var score = Math.Abs(overlap.Total - _strength / 100d) + offset * 0.00001;
                if (score >= best) continue;
                best = score;
                selected = mask;
                bestOffset = offset;
            }
        if (selected is null || best > 0.05) return;
        _placedTop = _baseMargin + bestOffset;
        _area.Margin = new Thickness(0, _placedTop, 0, 0);
        _selectedMask = selected;
        _maskBitmap = CreateMaskBitmap(selected);
    }

    public static (double Total, double Upper, double WorstDigit) MeasureOverlap(byte[] mask, Rect[] bands,
        double left, double top, Size viewport, double upperBoundary)
    {
        var maskSize = (int)Math.Sqrt(mask.Length);
        if (viewport.Width <= 0 || viewport.Height <= 0 || maskSize == 0 || maskSize * maskSize != mask.Length) return (0, 0, 0);
        double total = 0, covered = 0, upper = 0, upperCovered = 0;
        var minX = bands.Length == 0 ? 0 : bands.Min(b => b.Left);
        var width = bands.Length == 0 ? 1 : bands.Max(b => b.Right) - minX;
        double[] digitTotal = new double[5], digitCovered = new double[5];
        foreach (var band in bands)
            for (var x = band.Left; x < band.Right; x++)
            {
                var px = left + x;
                var py = top + band.Top;
                var alpha = px < 0 || py < 0 || px >= viewport.Width || py >= viewport.Height ? 0 :
                    mask[Math.Clamp((int)(py / viewport.Height * maskSize), 0, maskSize - 1) * maskSize +
                         Math.Clamp((int)(px / viewport.Width * maskSize), 0, maskSize - 1)] / 255d;
                total++;
                covered += alpha;
                var digit = Math.Clamp((int)((x - minX) / Math.Max(1, width) * 5), 0, 4);
                digitTotal[digit]++;
                digitCovered[digit] += alpha;
                if (band.Top < upperBoundary) { upper++; upperCovered += alpha; }
            }
        return (covered / Math.Max(1, total), upperCovered / Math.Max(1, upper),
            Enumerable.Range(0, 5).Max(i => digitCovered[i] / Math.Max(1, digitTotal[i])));
    }

    private static Bitmap CreateMaskBitmap(byte[] mask)
    {
        var maskSize = (int)Math.Sqrt(mask.Length);
        var bitmap = new WriteableBitmap(new PixelSize(maskSize, maskSize), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var pixels = new byte[buffer.RowBytes * maskSize];
            for (var y = 0; y < maskSize; y++)
                for (var x = 0; x < maskSize; x++)
                {
                    var index = y * buffer.RowBytes + x * 4;
                    pixels[index] = pixels[index + 1] = pixels[index + 2] = pixels[index + 3] = mask[y * maskSize + x];
                }
            global::System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }
        return bitmap;
    }

    public override void Render(DrawingContext context)
    {
        if (_maskBitmap is null || _selectedMask is null || _source is null || _clock is null || _area is null || _wallpaper?.Source != _source || (_allowed is not null && !_allowed())) return;
        var origin = _clock.TranslatePoint(default, this);
        if (origin is null) return;
        var clockClip = new Rect(origin.Value, _clock.Bounds.Size).Intersect(new Rect(Bounds.Size));
        using (context.PushClip(clockClip))
        using (context.PushOpacity(_area.Opacity))
        using (context.PushOpacityMask(new ImageBrush(_maskBitmap) { Stretch = Stretch.Fill }, new Rect(Bounds.Size)))
        {
            context.DrawImage(_source, Crop(_source.Size, Bounds.Size), new Rect(Bounds.Size));
            if (_foregroundScrim is not null) context.FillRectangle(_foregroundScrim, new Rect(Bounds.Size));
        }
    }
}
