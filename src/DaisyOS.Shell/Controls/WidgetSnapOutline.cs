using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

namespace DaisyOS.Shell.Controls;

/// <summary>Non-interactive outline of the resolved widget drop position.</summary>
public sealed class WidgetSnapOutline : Control
{
    public static readonly StyledProperty<Rect?> TargetBoundsProperty =
        AvaloniaProperty.Register<WidgetSnapOutline, Rect?>(nameof(TargetBounds));
    internal static readonly StyledProperty<Rect> AnimatedBoundsProperty =
        AvaloniaProperty.Register<WidgetSnapOutline, Rect>(nameof(AnimatedBounds));
    private readonly BoundsTransition _movement = new()
    {
        Property = AnimatedBoundsProperty,
        Duration = TimeSpan.FromMilliseconds(160),
        Easing = new SpringEasing(1, 256, 32)
    };

    static WidgetSnapOutline() => AffectsRender<WidgetSnapOutline>(TargetBoundsProperty, AnimatedBoundsProperty);

    public WidgetSnapOutline() => Transitions = new Transitions { _movement };

    private sealed class BoundsTransition : InterpolatingTransitionBase<Rect>
    {
        protected override Rect Interpolate(double progress, Rect from, Rect to) =>
            new(from.Position + (to.Position - from.Position) * progress,
                from.Size + (to.Size - from.Size) * progress);
    }

    internal Rect AnimatedBounds => GetValue(AnimatedBoundsProperty);

    private void SetAnimatedBounds(Rect bounds) => SetValue(AnimatedBoundsProperty, bounds);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TargetBoundsProperty) return;
        _movement.Duration = Application.Current is App { ReduceMotionEnabled: true } ? TimeSpan.Zero
            : this.TryFindResource("MotionDurationBase", ActualThemeVariant, out var duration) && duration is TimeSpan value
                ? value : TimeSpan.FromMilliseconds(160);
        if (TargetBounds is { } target && change.GetOldValue<Rect?>() is not null)
            SetAnimatedBounds(target);
        else
        {
            // Each drag starts at its own destination; cancellation clears any in-flight movement.
            var transitions = Transitions;
            Transitions = null;
            SetAnimatedBounds(TargetBounds ?? default);
            Transitions = transitions;
        }
    }

    public Rect? TargetBounds
    {
        get => GetValue(TargetBoundsProperty);
        set => SetValue(TargetBoundsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (TargetBounds is null) return;
        var bounds = AnimatedBounds;
        if (!this.TryFindResource("ActionPrimaryBrush", ActualThemeVariant, out var brush)
            || brush is not IBrush accent) return;
        double radius = this.TryFindResource("RoundedXXL", ActualThemeVariant, out var corner)
            && corner is CornerRadius rounded ? rounded.TopLeft : 32;
        context.DrawRectangle(null, new Pen(accent, 2), bounds, radius, radius);
    }
}
