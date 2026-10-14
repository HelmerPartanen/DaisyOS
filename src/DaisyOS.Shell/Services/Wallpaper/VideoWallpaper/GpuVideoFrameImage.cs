using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace DaisyOS.Shell.Services.Wallpaper.VideoWallpaper;

/// <summary>Opt-in Skia texture upload without an intermediate WriteableBitmap copy.
/// Decoder output is still BGRA on the CPU; this does not claim DMA-BUF zero-copy.</summary>
internal sealed class GpuVideoFrameImage : IImage, IDisposable
{
    public static bool Enabled => Environment.GetEnvironmentVariable("DAISYOS_GPU_VIDEO") == "1";
    private readonly SKBitmap _pixels;
    private int _references = 1;
    public Size Size { get; }
    private GpuVideoFrameImage(SKBitmap pixels, Size size) { _pixels = pixels; Size = size; }
    public static bool TryCreate(DaisyNativeWallpaper.NativeVideoFrame frame, out GpuVideoFrameImage? image)
    {
        image = null;
        var pointer = (ulong)frame.Stride0 | ((ulong)frame.Stride1 << 32);
        if (!Enabled || frame.Fd0 != -2 || pointer == 0) return false;
        var bitmap = new SKBitmap();
        var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        if (!bitmap.InstallPixels(info, (nint)pointer, frame.Width * 4,
            (_, _) => { var owned = frame; VideoWallpaperService.ReleaseFrame(ref owned); }, null))
        { bitmap.Dispose(); return false; }
        image = new GpuVideoFrameImage(bitmap, new Size(frame.Width, frame.Height));
        return true;
    }
    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
    {
        Interlocked.Increment(ref _references);
        context.Custom(new DrawOperation(this, sourceRect, destRect));
    }
    public void Dispose() { if (Interlocked.Decrement(ref _references) == 0) _pixels.Dispose(); }
    private sealed class DrawOperation(GpuVideoFrameImage image, Rect source, Rect target) : ICustomDrawOperation
    {
        private GpuVideoFrameImage? _image = image;
        public Rect Bounds => target;
        public bool HitTest(Point point) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() => Interlocked.Exchange(ref _image, null)?.Dispose();
        public void Render(ImmediateDrawingContext context)
        {
            if (_image is not { } current || context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            using var trace = Diagnostics.ResponsivenessTrace.Measure("wallpaper.skia.upload-draw");
            using var cpuImage = SKImage.FromBitmap(current._pixels);
            using var texture = lease.GrContext is { } gpu ? cpuImage.ToTextureImage(gpu) : null;
            var drawable = texture ?? cpuImage;
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * lease.CurrentOpacity)) };
            lease.SkCanvas.DrawImage(drawable,
                new SKRect((float)source.X, (float)source.Y, (float)source.Right, (float)source.Bottom),
                new SKRect((float)target.X, (float)target.Y, (float)target.Right, (float)target.Bottom),
                new SKSamplingOptions(SKFilterMode.Linear), paint);
        }
    }
}
