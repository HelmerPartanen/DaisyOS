using System.Runtime.CompilerServices;
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
    private static readonly ConditionalWeakTable<Bitmap, Dictionary<(int X, int Y, int W, int H), byte[][]>> AnalyzedBitmapMasks = new();

    private Image? _wallpaper;
    private TextBlock? _clock;
    private Control? _area;
    private Func<Rect[]>? _glyphs;
    private Func<bool>? _allowed;
    private CancellationTokenSource? _request;
    private byte[][]? _masks;
    private Bitmap? _maskBitmap;
    private ImageBrush? _maskBrush;
    private byte[]? _selectedMask;
    private byte[]? _maskBitmapSource;
    private readonly DeptEffectService _service = new();
    private readonly List<Bitmap> _retiredMasks = [];
    private string? _layoutKey;
    private string? _renderStateKey;
    private (Bitmap? Source, int X, int Y, int W, int H, bool Enabled, bool Automatic)? _scheduledAnalysisSignature;
    public double ClockTopFraction { get; set; } = 0.10;
    public double ForegroundTintOpacity { get; set; }
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

    public void SetPreferences(bool enabled, bool automatic, int strength = 15)
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
        _scheduledAnalysisSignature = null;
        ClearMask();
        foreach (var retired in _retiredMasks) retired.Dispose();
        _retiredMasks.Clear();
        _masks = null;
        _source = null;
    }

    private static (int X, int Y, int W, int H) QuantizeCrop(Rect crop) =>
        ((int)Math.Round(crop.X * 10), (int)Math.Round(crop.Y * 10),
         (int)Math.Round(crop.Width * 10), (int)Math.Round(crop.Height * 10));

    private static bool TryGetCachedMasks(Bitmap source, (int X, int Y, int W, int H) cropKey, out byte[][]? masks)
    {
        masks = null;
        if (!AnalyzedBitmapMasks.TryGetValue(source, out var map)) return false;
        lock (map)
        {
            return map.TryGetValue(cropKey, out masks);
        }
    }

    private static void CacheMasks(Bitmap source, (int X, int Y, int W, int H) cropKey, byte[][] masks)
    {
        var map = AnalyzedBitmapMasks.GetOrCreateValue(source);
        lock (map)
        {
            if (map.Count >= 8) map.Clear();
            map[cropKey] = masks;
        }
    }

    private void ScheduleAnalysis()
    {
        if (!_enabled || _wallpaper?.Source is not Bitmap bitmapSource || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _request?.Cancel();
            _request?.Dispose();
            _request = null;
            _scheduledAnalysisSignature = null;
            _masks = null;
            _source = null;
            _layoutKey = null;
            ClearMask();
            if (_area is not null && _baseMargin > 0) _area.Margin = new Thickness(0, _baseMargin, 0, 0);
            return;
        }

        var crop = Crop(bitmapSource.Size, Bounds.Size);
        var cropKey = QuantizeCrop(crop);
        var signature = ((Bitmap?)bitmapSource, cropKey.X, cropKey.Y, cropKey.W, cropKey.H, _enabled, _automatic);
        if (_scheduledAnalysisSignature == signature && (_masks is not null || _request is not null))
        {
            if (_masks is not null && _analyzedSize != Bounds.Size)
            {
                _analyzedSize = Bounds.Size;
                _layoutKey = null;
                RefreshLayout();
            }
            return;
        }

        if (TryGetCachedMasks(bitmapSource, cropKey, out var cachedMasks) && cachedMasks is not null)
        {
            _scheduledAnalysisSignature = signature;
            ApplyPreparedMasks(cachedMasks);
            return;
        }

        _scheduledAnalysisSignature = signature;
        _request?.Cancel();
        _request?.Dispose();
        _request = new CancellationTokenSource();
        var token = _request.Token;
        _masks = null;
        _source = null;
        _layoutKey = null;
        ClearMask();
        if (_area is not null && _baseMargin > 0) _area.Margin = new Thickness(0, _baseMargin, 0, 0);

        // Defer until layout and any image swap have settled. Capture only a small analysis image on the UI thread.
        Dispatcher.UIThread.Post(async () =>
        {
            if (token.IsCancellationRequested || _wallpaper.Source is not Bitmap source || Bounds.Width <= 0 || Bounds.Height <= 0) return;
            try
            {
                var currentCrop = Crop(source.Size, Bounds.Size);
                var currentCropKey = QuantizeCrop(currentCrop);
                if (TryGetCachedMasks(source, currentCropKey, out var readyMasks) && readyMasks is not null)
                {
                    ApplyPreparedMasks(readyMasks);
                    return;
                }

                _source = source;
                _analyzedSize = Bounds.Size;
                using var thumbnail = new RenderTargetBitmap(new PixelSize(DeptEffectService.OverlaySize, DeptEffectService.OverlaySize));
                using (var drawing = thumbnail.CreateDrawingContext())
                    drawing.DrawImage(source, currentCrop, new Rect(0, 0, DeptEffectService.OverlaySize, DeptEffectService.OverlaySize));
                using var stream = new MemoryStream();
                thumbnail.Save(stream, PngBitmapEncoderOptions.Default);
                var masks = await _service.AnalyzeAsync(stream.ToArray(), _automatic, token);
                if (token.IsCancellationRequested) return;
                if (masks is not null) CacheMasks(source, currentCropKey, masks);
                ApplyPreparedMasks(masks);
            }
            catch (Exception ex) { Console.Error.WriteLine($"[DeptEffect] Preview unavailable: {ex.Message}"); }
        }, DispatcherPriority.Background);
    }

    internal bool HasForeground => _maskBitmap is not null;

    internal void ApplyPreparedMasks(byte[][]? masks)
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = null;
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
        _maskBrush = null;
        _maskBitmapSource = null;
        if (retired is not null) _ = RetireMaskAsync(retired);
        _selectedMask = null;
        _renderStateKey = null;
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
        var origin = _clock.TranslatePoint(default, this);
        if (origin is null) return;
        var allowed = _allowed?.Invoke() ?? true;

        // Presence and media animations affect rendering, but never trigger inference or placement searches.
        // Only invalidate the visual when state that actually affects Render() changes.
        var renderStateKey = _maskBitmap is null
            ? "empty"
            : $"{origin.Value.X:F2}|{origin.Value.Y:F2}|{_clock.Bounds.Width:F1}|{_clock.Bounds.Height:F1}|{_area.Opacity:F3}|{Bounds.Width:F1}|{Bounds.Height:F1}|{ForegroundTintOpacity:F3}|{allowed}|{_wallpaper?.Source == _source}";
        if (renderStateKey != _renderStateKey)
        {
            _renderStateKey = renderStateKey;
            InvalidateVisual();
        }

        // Derive a stable resting X/Y origin so vertical presence translations do not invalidate _layoutKey.
        var localOrigin = _clock.TranslatePoint(default, _area) ?? default;
        var xOrigin = origin.Value.X;
        var key = $"{_clock.Text}|{_clock.FontSize}|{_clock.FontFamily}|{Bounds.Size}|{_strength}|{_enabled}|{allowed}|{xOrigin:F1}|{_area.Bounds.Size}|{ClockTopFraction}|{_clock.FontWeight}";
        if (key == _layoutKey && _area.Margin.Top.Equals(_placedTop)) return;
        _layoutKey = key;
        _baseMargin = Math.Clamp(Bounds.Height * ClockTopFraction, 24, ClockTopFraction == 0.10 ? 96 : Math.Max(24, Bounds.Height * 0.2));
        var previousTop = _area.Margin.Top;
        _placedTop = _baseMargin;
        _area.Margin = new Thickness(0, _baseMargin, 0, 0);
        if (!_enabled || !allowed || _strength == 0 || _masks is null || _glyphs is null || Bounds.Size != _analyzedSize)
        {
            ClearMask();
            return;
        }
        var bands = _glyphs();
        if (bands.Length == 0)
        {
            ClearMask();
            return;
        }
        var best = double.PositiveInfinity;
        var bestOffset = 0d;
        byte[]? selected = null;
        var parentOrigin = (_area.Parent as Visual)?.TranslatePoint(_area.Bounds.Position, this);
        var yOrigin = (parentOrigin?.Y ?? previousTop) - previousTop + _baseMargin + localOrigin.Y;
        var glyphTop = double.PositiveInfinity;
        var glyphBottom = double.NegativeInfinity;
        for (var i = 0; i < bands.Length; i++)
        {
            if (bands[i].Top < glyphTop) glyphTop = bands[i].Top;
            if (bands[i].Bottom > glyphBottom) glyphBottom = bands[i].Bottom;
        }
        var glyphHeight = glyphBottom - glyphTop;
        var upperBoundary = glyphTop + glyphHeight * 0.55;
        var targetRatio = _strength / 100d;
        var minOverlap = Math.Min(0.05, targetRatio);
        var maxOverlap = Math.Min(0.30, targetRatio + 0.04);
        var maxOffset = Bounds.Height * 0.22;
        var maxBottom = Bounds.Height * 0.48;
        foreach (var mask in _masks)
            for (var offset = 0d; offset <= maxOffset; offset += 2)
            {
                if (yOrigin + offset + glyphBottom > maxBottom) continue;
                var overlap = MeasureOverlap(mask, bands, xOrigin, yOrigin + offset, Bounds.Size, upperBoundary);
                if (overlap.Total < minOverlap || overlap.Total > maxOverlap || overlap.Upper > 0.015 || overlap.WorstDigit > 0.40) continue;
                var score = Math.Abs(overlap.Total - targetRatio) + offset * 0.00001;
                if (score >= best) continue;
                best = score;
                selected = mask;
                bestOffset = offset;
            }
        // The requested overlap is a preference, not an eligibility threshold. A readable
        // smaller overlap is preferable to discarding a correctly segmented subject.
        if (selected is null)
        {
            ClearMask();
            return;
        }
        _placedTop = _baseMargin + bestOffset;
        _area.Margin = new Thickness(0, _placedTop, 0, 0);
        _selectedMask = selected;
        if (!ReferenceEquals(_maskBitmapSource, selected) || _maskBitmap is null)
        {
            var retired = _maskBitmap;
            if (retired is not null) _ = RetireMaskAsync(retired);
            _maskBitmapSource = selected;
            _maskBitmap = CreateMaskBitmap(selected);
            _maskBrush = new ImageBrush(_maskBitmap) { Stretch = Stretch.Fill };
        }
        _renderStateKey = null;
        InvalidateVisual();
    }

    public static (double Total, double Upper, double WorstDigit) MeasureOverlap(byte[] mask, Rect[] bands,
        double left, double top, Size viewport, double upperBoundary)
    {
        var maskSize = (int)Math.Sqrt(mask.Length);
        if (viewport.Width <= 0 || viewport.Height <= 0 || maskSize == 0 || maskSize * maskSize != mask.Length) return (0, 0, 0);
        if (bands.Length == 0) return (0, 0, 0);

        double total = 0, covered = 0, upper = 0, upperCovered = 0;
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        for (var i = 0; i < bands.Length; i++)
        {
            if (bands[i].Left < minX) minX = bands[i].Left;
            if (bands[i].Right > maxX) maxX = bands[i].Right;
        }
        var width = Math.Max(1, maxX - minX);
        var invWidth5 = 5d / width;
        var scaleX = maskSize / viewport.Width;
        var scaleY = maskSize / viewport.Height;
        var maxMaskIdx = maskSize - 1;
        const double inv255 = 1d / 255d;

        Span<double> digitTotal = stackalloc double[5];
        Span<double> digitCovered = stackalloc double[5];

        for (var i = 0; i < bands.Length; i++)
        {
            var band = bands[i];
            var py = top + band.Top;
            var isUpper = band.Top < upperBoundary;
            var yInBounds = py >= 0 && py < viewport.Height;
            var rowOffset = yInBounds ? Math.Clamp((int)(py * scaleY), 0, maxMaskIdx) * maskSize : 0;

            for (var x = band.Left; x < band.Right; x++)
            {
                var px = left + x;
                var alpha = !yInBounds || px < 0 || px >= viewport.Width
                    ? 0d
                    : mask[rowOffset + Math.Clamp((int)(px * scaleX), 0, maxMaskIdx)] * inv255;
                total++;
                covered += alpha;
                var digit = Math.Clamp((int)((x - minX) * invWidth5), 0, 4);
                digitTotal[digit]++;
                digitCovered[digit] += alpha;
                if (isUpper) { upper++; upperCovered += alpha; }
            }
        }

        var worstDigit = 0d;
        for (var i = 0; i < 5; i++)
        {
            var d = digitCovered[i] / Math.Max(1, digitTotal[i]);
            if (d > worstDigit) worstDigit = d;
        }

        return (covered / Math.Max(1, total), upperCovered / Math.Max(1, upper), worstDigit);
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
        if (_maskBitmap is null || _maskBrush is null || _selectedMask is null || _source is null || _clock is null || _area is null || _wallpaper?.Source != _source || (_allowed is not null && !_allowed())) return;
        var origin = _clock.TranslatePoint(default, this);
        if (origin is null) return;
        var clockClip = new Rect(origin.Value, _clock.Bounds.Size).Intersect(new Rect(Bounds.Size));
        using (context.PushClip(clockClip))
        using (context.PushOpacity(_area.Opacity))
        using (context.PushOpacityMask(_maskBrush, new Rect(Bounds.Size)))
        {
            context.DrawImage(_source, Crop(_source.Size, Bounds.Size), new Rect(Bounds.Size));
            if (_foregroundScrim is not null) context.FillRectangle(_foregroundScrim, new Rect(Bounds.Size));
            if (ForegroundTintOpacity > 0)
                using (context.PushOpacity(ForegroundTintOpacity)) context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        }
    }
}
