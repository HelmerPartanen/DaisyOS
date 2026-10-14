using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;

namespace DaisyOS.Shell.Services.Theming;

/// <summary>Cross-fades themed raster sources without replacing their resource/data binding.</summary>
internal sealed class ThemeImageTransition : IObservable<IImage?>
{
    private readonly Image _image;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private IObserver<IImage?>? _observer;
    private IDisposable? _binding;
    private IImage? _from, _to, _frame;
    private long _started;
    private TimeSpan _duration;
    private Size _targetSize;
    private bool _updating;

    public ThemeImageTransition(Image image)
    {
        _image = image;
        image.PropertyChanged += (_, change) =>
        {
            if (!_updating && change.Property == Image.SourceProperty && TopLevel.GetTopLevel(image) != null)
                Start(change.OldValue as IImage, change.NewValue as IImage);
        };
        image.DetachedFromVisualTree += (_, _) => Finish();
        _timer.Tick += (_, _) => Tick();
    }

    private void Start(IImage? from, IImage? to)
    {
        if (!ThemeTransitionPolicy.GetEnabled(_image)) { Finish(); return; }
        if (from == null || to == null || ReferenceEquals(from, to)) { Finish(); return; }
        _duration = _image.TryFindResource("MotionDurationBase", out var value) && value is TimeSpan duration ? duration : TimeSpan.Zero;
        if (_duration <= TimeSpan.Zero) { Finish(); return; }
        _from = from;
        _to = to;
        // Never publish a raw old Bitmap at animation priority: its owner may
        // dispose it as soon as the base Source changes.
        try { _targetSize = to.Size; _frame = new BlendedImage(from, to, 0, _targetSize); }
        catch (ObjectDisposedException) { Finish(); return; }
        _started = Stopwatch.GetTimestamp();
        _updating = true;
        try { _binding ??= _image.Bind(Image.SourceProperty, this, BindingPriority.Animation); }
        finally { _updating = false; }
        _timer.Start();
    }

    private void Tick()
    {
        if (!ThemeTransitionPolicy.GetEnabled(_image)) { Finish(); return; }
        var target = _image.GetBaseValue(Image.SourceProperty).GetValueOrDefault();
        if (target == null) { Finish(); return; }
        if (!ReferenceEquals(target, _to)) Start(_frame, target);
        if (_binding == null) return;
        if (_image.TryFindResource("MotionDurationBase", out var value) && value is TimeSpan duration && duration <= TimeSpan.Zero)
        { Finish(); return; }
        var progress = Math.Clamp(Stopwatch.GetElapsedTime(_started).TotalMilliseconds / _duration.TotalMilliseconds, 0, 1);
        if (progress >= 1) { Finish(); return; }
        _frame = new BlendedImage(_from!, _to!, progress, _targetSize);
        _updating = true;
        try { _observer?.OnNext(_frame); }
        finally { _updating = false; }
    }

    private void Finish()
    {
        _timer.Stop();
        _updating = true;
        try { _binding?.Dispose(); _binding = null; _observer = null; _from = _to = _frame = null; }
        finally { _updating = false; }
    }

    public IDisposable Subscribe(IObserver<IImage?> observer)
    {
        _observer = observer;
        observer.OnNext(_frame);
        return new Subscription(this, observer);
    }

    private sealed class Subscription(ThemeImageTransition owner, IObserver<IImage?> observer) : IDisposable
    {
        public void Dispose() { if (ReferenceEquals(owner._observer, observer)) owner._observer = null; }
    }

    private sealed class BlendedImage(IImage from, IImage to, double progress, Size size) : IImage
    {
        public Size Size { get; } = size;
        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
            if (Size.Width <= 0 || Size.Height <= 0) return;
            try
            {
                using (context.PushOpacity(1 - progress))
                    from.Draw(context, new Rect(sourceRect.X * from.Size.Width / Size.Width,
                        sourceRect.Y * from.Size.Height / Size.Height, sourceRect.Width * from.Size.Width / Size.Width,
                        sourceRect.Height * from.Size.Height / Size.Height), destRect);
            }
            catch (ObjectDisposedException)
            {
                DrawTarget(context, sourceRect, destRect, 1);
                return;
            }
            DrawTarget(context, sourceRect, destRect, progress);
        }
        private void DrawTarget(DrawingContext context, Rect sourceRect, Rect destRect, double opacity)
        {
            try
            {
                using (context.PushOpacity(opacity)) to.Draw(context, sourceRect, destRect);
            }
            catch (ObjectDisposedException) { /* A replacement invalidated this transition frame. */ }
        }
    }
}
