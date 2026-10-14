using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DaisyOS.Shell.Controls;

/// <summary>
/// Resolves a semantic icon name to DaisyOS's tree-shaken HugeIcons resource
/// bundle. UI code never needs to know the library's source location.
/// </summary>
public sealed class DaisyIcon : Avalonia.Svg.Skia.Svg
{
    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<DaisyIcon, string?>(nameof(Icon));

    public static readonly StyledProperty<DaisyIconWeight> WeightProperty =
        AvaloniaProperty.Register<DaisyIcon, DaisyIconWeight>(nameof(Weight));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<DaisyIcon, double>(nameof(Size), 16d);

    // Share Avalonia's inheritable text foreground property. A separately
    // registered property only inherits from other DaisyIcon instances, not
    // from the Button, TextBlock, or panel that provides the icon color.
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<DaisyIcon>();

    private SolidColorBrush? _observedBrush;

    static DaisyIcon()
    {
        ForegroundProperty.OverrideMetadata<DaisyIcon>(new StyledPropertyMetadata<IBrush?>(null));
        IconProperty.Changed.AddClassHandler<DaisyIcon>((icon, _) => icon.UpdateSource());
        WeightProperty.Changed.AddClassHandler<DaisyIcon>((icon, _) => icon.UpdateSource());
        SizeProperty.Changed.AddClassHandler<DaisyIcon>((icon, _) => icon.UpdateSize());
        ForegroundProperty.Changed.AddClassHandler<DaisyIcon>((icon, _) => icon.UpdateColor());
    }

    public DaisyIcon() : base(new Uri("file:///"))
    {
        Stretch = Stretch.Uniform;
        // Color transitions produce many intermediate paints; do not retain a
        // separately parsed SVG for every transient color in the per-control cache.
        EnableCache = false;
        IsHitTestVisible = false;
        UpdateSize();
        UpdateColor();
        ActualThemeVariantChanged += (_, _) => UpdateColor();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Resolve once before the first popup frame, then again after the
        // attachment style pass. Waiting only for Loaded leaves currentColor
        // at SVG's black default for one frame on newly-created menus.
        UpdateColor();
        Dispatcher.UIThread.Post(UpdateColor, DispatcherPriority.Loaded);
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        UpdateColor();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ObserveBrush(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void ObserveBrush(IBrush? brush)
    {
        var mutable = brush as SolidColorBrush;
        if (ReferenceEquals(mutable, _observedBrush)) return;
        if (_observedBrush != null) _observedBrush.PropertyChanged -= OnBrushChanged;
        _observedBrush = mutable;
        if (_observedBrush != null) _observedBrush.PropertyChanged += OnBrushChanged;
    }

    private void OnBrushChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SolidColorBrush.ColorProperty || e.Property == Brush.OpacityProperty) UpdateColor();
    }

    private void UseBrush(ISolidColorBrush brush)
    {
        ObserveBrush(VisualRoot != null ? brush : null);
        var color = brush.Color;
        CurrentColor = Color.FromArgb((byte)Math.Round(color.A * brush.Opacity), color.R, color.G, color.B);
    }

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public DaisyIconWeight Weight
    {
        get => GetValue(WeightProperty);
        set => SetValue(WeightProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private void UpdateSource()
    {
        var name = UIIcons.Parse(Icon?.Trim()).Glyph.ToLowerInvariant();
        Path = string.IsNullOrWhiteSpace(name)
            ? null
            : $"avares://DaisyOS.Shell/Assets/Icons/HugeIcons/{name}.svg";
        UpdateColor();
    }

    private void UpdateSize()
    {
        if (Size is > 0 and <= 512)
        {
            Width = Size;
            Height = Size;
        }
    }

    private void UpdateColor()
    {
        // Prefer explicitly-set Foreground (handles SolidColorBrush and any IBrush).
        if (Foreground is ISolidColorBrush solidBrush)
        {
            UseBrush(solidBrush);
            return;
        }

        // Walk the visual parent chain to find an ancestor's Foreground.
        // This handles icons placed inside ContentControl/ContentPresenter
        // where TextElement.ForegroundProperty inheritance is broken.
        Visual? parent = this.GetVisualParent();
        while (parent != null)
        {
            if (parent.GetValue(TextElement.ForegroundProperty) is ISolidColorBrush parentBrush)
            {
                UseBrush(parentBrush);
                return;
            }
            parent = parent.GetVisualParent();
        }

        // Fall back to the nearest resource dictionary lookup.
        if (this.TryFindResource("ContentPrimaryBrush", out var localResource) &&
            localResource is ISolidColorBrush localBrush)
        {
            UseBrush(localBrush);
            return;
        }

        if (Application.Current?.Resources.TryGetResource("ContentPrimaryBrush", ActualThemeVariant, out var resource) == true &&
            resource is ISolidColorBrush fallback)
        {
            UseBrush(fallback);
        }
    }
}

public enum DaisyIconWeight
{
    // HugeIcons Free is Stroke Rounded. Retain both values for compatibility
    // while using one resource per logical name rather than duplicate assets.
    Regular,
    Fill
}
