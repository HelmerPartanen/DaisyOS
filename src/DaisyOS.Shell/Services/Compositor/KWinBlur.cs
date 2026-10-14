using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Wayland;
using DaisyOS.Shell.Controls;

namespace DaisyOS.Shell.Services.Compositor;

/// <summary>Publishes rounded DaisyOS material regions to KWin.</summary>
internal sealed class KWinBlur : IDisposable
{
    internal event Action? GeometryChanged;

    // Overlay popups are siblings of Window.Content, so the scan must start at the surface.
    internal static IEnumerable<Control> PopupMaterials(Window surface) => surface.GetVisualDescendants()
        .OfType<Border>().Where(border => border.Name == "MenuBlurHost");

    private readonly Window _surface;
    private readonly Control[] _controls;
    private bool _queued;
    private bool _disposed;
    private readonly Func<IEnumerable<Control>>? _regionControls;
    private List<WaylandBlurRect>? _lastRegions;
    internal Func<IEnumerable<WaylandBlurRect>>? AdditionalRegions { get; set; }
    private readonly HashSet<Visual> _watchedVisuals = [];
    private readonly HashSet<Transform> _watchedTransforms = [];

    public KWinBlur(Window surface, Control layoutRoot, Func<IEnumerable<Control>> regionControls)
        : this(surface, layoutRoot)
    {
        _regionControls = regionControls;
    }

    public KWinBlur(Window surface, params Control[] controls)
    {
        _surface = surface;
        _controls = controls;
        foreach (var control in controls)
        {
            control.LayoutUpdated += OnLayoutUpdated;
            control.PropertyChanged += OnControlPropertyChanged;
        }
        surface.Opened += OnOpened;
        surface.Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _lastRegions = null;
        Invalidate();
    }
    // Blur is part of the surface's visual transaction. Deferring this to the
    // render queue lets a newly visible popup paint one frame before KWin has
    // its mask, and lets a closing popup disappear one frame before the mask.
    // Commit during layout/property notification; unchanged regions are still
    // deduplicated in Refresh().
    private void OnLayoutUpdated(object? sender, EventArgs e) => RefreshImmediately();
    private void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty || e.Property == Visual.OpacityProperty ||
            e.Property == Visual.BoundsProperty || e.Property == Visual.RenderTransformProperty ||
            e.Property == Visual.RenderTransformOriginProperty)
            RefreshImmediately();
    }
    private void OnTransformChanged(object? sender, EventArgs e) => RefreshImmediately();

    private void WatchGeometry(Control[] controls)
    {
        var visuals = controls.SelectMany(control => control.GetVisualAncestors().Prepend(control))
            .Where(visual => visual != _surface && !_controls.Contains(visual)).ToHashSet();
        foreach (var old in _watchedVisuals.Except(visuals).ToArray())
        {
            old.PropertyChanged -= OnControlPropertyChanged;
            _watchedVisuals.Remove(old);
        }
        foreach (var visual in visuals.Except(_watchedVisuals))
        {
            visual.PropertyChanged += OnControlPropertyChanged;
            _watchedVisuals.Add(visual);
        }
        var transforms = visuals.Concat(_controls).Select(visual => visual.RenderTransform).OfType<Transform>().ToHashSet();
        foreach (var old in _watchedTransforms.Except(transforms).ToArray())
        {
            old.Changed -= OnTransformChanged;
            _watchedTransforms.Remove(old);
        }
        foreach (var transform in transforms.Except(_watchedTransforms))
        {
            transform.Changed += OnTransformChanged;
            _watchedTransforms.Add(transform);
        }
    }
    private void OnClosed(object? sender, EventArgs e) => Dispose();

    internal void Invalidate()
    {
        if (_queued || _disposed) return;
        _queued = true;
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Render);
    }

    internal void RefreshImmediately()
    {
        var queued = _queued;
        Refresh();
        _queued = queued;
    }

    private void Refresh()
    {
        _queued = false;
        if (_disposed) return;
        GeometryChanged?.Invoke();
        var regions = new List<WaylandBlurRect>();
        var controls = (_regionControls?.Invoke() ?? _controls).ToArray();
        WatchGeometry(controls);
        foreach (var control in controls)
        {
            if (!control.IsEffectivelyVisible || control.Opacity <= 0.001 ||
                control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
                continue;
            var transform = control.TransformToVisual(_surface);
            if (transform is null) continue;
            var controlRegions = new List<WaylandBlurRect>();
            if (control is CartoucheBorder cartouche)
            {
                AddTransformedCartoucheRegion(controlRegions, cartouche, transform.Value);
            }
            else
            {
                var radius = control is Border border ? border.CornerRadius.TopLeft : 16;
                AddTransformedRegion(controlRegions, control.Bounds.Size, transform.Value, radius);
            }
            AppendClippedRegions(regions, controlRegions, GetAncestorClipBounds(control));
        }
        if (AdditionalRegions is not null) regions.AddRange(AdditionalRegions());
        if (_lastRegions is not null && _lastRegions.SequenceEqual(regions)) return;
        LayerShellWindow.SetBlurRegions(_surface, regions);
        _lastRegions = regions;
    }

    private Rect? GetAncestorClipBounds(Control control)
    {
        Rect? bounds = null;
        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>().Where(item => item.ClipToBounds))
        {
            var transform = ancestor.TransformToVisual(_surface);
            if (transform is null) continue;
            var clip = new Rect(ancestor.Bounds.Size).TransformToAABB(transform.Value);
            bounds = bounds is null ? clip : Intersect(bounds.Value, clip);
            if (bounds.Value.Width <= 0 || bounds.Value.Height <= 0)
                return new Rect(0, 0, 0, 0);
        }
        return bounds;
    }

    private static Rect Intersect(Rect first, Rect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        return right <= left || bottom <= top
            ? new Rect(0, 0, 0, 0)
            : new Rect(left, top, right - left, bottom - top);
    }

    private static void AppendClippedRegions(
        List<WaylandBlurRect> target,
        IEnumerable<WaylandBlurRect> source,
        Rect? clip)
    {
        if (clip is null)
        {
            target.AddRange(source);
            return;
        }

        // Blur rectangles are integer scanlines. Round clip edges inward so
        // KWin cannot blur through the native bottom-bar clipping viewport.
        var clipLeft = (int)Math.Ceiling(clip.Value.Left);
        var clipTop = (int)Math.Ceiling(clip.Value.Top);
        var clipRight = (int)Math.Floor(clip.Value.Right);
        var clipBottom = (int)Math.Floor(clip.Value.Bottom);
        foreach (var region in source)
        {
            var left = Math.Max(region.X, clipLeft);
            var top = Math.Max(region.Y, clipTop);
            var right = Math.Min(region.X + region.Width, clipRight);
            var bottom = Math.Min(region.Y + region.Height, clipBottom);
            if (right > left && bottom > top)
                target.Add(new WaylandBlurRect(left, top, right - left, bottom - top));
        }
    }

    internal static void AddTransformedRegion(List<WaylandBlurRect> target, Size size, Matrix transform, double radius = 16)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        var bounds = new Rect(size).TransformToAABB(transform);
        var scale = Math.Min(bounds.Width / size.Width, bounds.Height / size.Height);
        AddRoundedRegion(target, bounds.Position, bounds.Size, (int)Math.Round(radius * scale));
    }

    private static void AddTransformedCartoucheRegion(
        List<WaylandBlurRect> target, CartoucheBorder cartouche, Matrix transform)
    {
        var size = cartouche.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        var bounds = new Rect(size).TransformToAABB(transform);
        var scale = Math.Min(bounds.Width / size.Width, bounds.Height / size.Height);
        var tailHeight = Math.Max(0, (int)Math.Round(cartouche.TailHeight * scale));
        var tailWidth = Math.Max(2, (int)Math.Round(cartouche.TailWidth * scale));
        var radius = Math.Max(0, (int)Math.Round(cartouche.CornerRadius * scale));
        var left = (int)Math.Floor(bounds.X);
        var top = (int)Math.Floor(bounds.Y);
        var width = (int)Math.Ceiling(bounds.Right) - left;
        var height = (int)Math.Ceiling(bounds.Bottom) - top;
        var bodyHeight = Math.Max(1, height - tailHeight);
        var bodyTop = cartouche.TailOnTop ? top + tailHeight : top;

        AddRoundedRegion(target, new Point(left, bodyTop), new Size(width, bodyHeight), radius);
        if (tailHeight <= 0) return;

        var centerX = left + width / 2;
        if (!double.IsNaN(cartouche.TailX))
            centerX = left + (int)Math.Round(cartouche.TailX * scale);
        for (var row = 0; row < tailHeight; row++)
        {
            // Width grows toward the body for an upward tail and away from
            // the body for a downward tail.
            var progress = cartouche.TailOnTop
                ? (row + 1d) / tailHeight
                : (tailHeight - row) / (double)tailHeight;
            var rowWidth = Math.Max(2, (int)Math.Ceiling(tailWidth * progress));
            var y = cartouche.TailOnTop ? top + row : top + bodyHeight + row;
            target.Add(new WaylandBlurRect(centerX - rowWidth / 2, y, rowWidth, 1));
        }
    }

    internal static void AddRoundedRegion(List<WaylandBlurRect> target, Point origin, Size size, int radius)
    {
        var left = (int)Math.Floor(origin.X);
        var top = (int)Math.Floor(origin.Y);
        var width = (int)Math.Ceiling(origin.X + size.Width) - left;
        var height = (int)Math.Ceiling(origin.Y + size.Height) - top;
        radius = Math.Clamp(radius, 0, Math.Min(width, height) / 2);
        if (radius == 0)
        {
            target.Add(new(left, top, width, height));
            return;
        }
        for (var row = 0; row < radius; row++)
        {
            var vertical = radius - row - 0.5;
            // Include partially covered edge pixels. Rounding inward exposes
            // KWin's integer scanlines through Avalonia's antialiased glass edge.
            var inset = (int)Math.Floor(radius - Math.Sqrt(radius * radius - vertical * vertical));
            target.Add(new(left + inset, top + row, width - inset * 2, 1));
            target.Add(new(left + inset, top + height - row - 1, width - inset * 2, 1));
        }
        if (height > radius * 2)
            target.Add(new(left, top + radius, width, height - radius * 2));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var control in _controls)
        {
            control.LayoutUpdated -= OnLayoutUpdated;
            control.PropertyChanged -= OnControlPropertyChanged;
        }
        _surface.Opened -= OnOpened;
        _surface.Closed -= OnClosed;
        foreach (var visual in _watchedVisuals) visual.PropertyChanged -= OnControlPropertyChanged;
        foreach (var transform in _watchedTransforms) transform.Changed -= OnTransformChanged;
        _watchedVisuals.Clear();
        _watchedTransforms.Clear();
        LayerShellWindow.SetBlurRegions(_surface, []);
    }
}
