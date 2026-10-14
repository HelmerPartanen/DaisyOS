using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DaisyOS.Shell.Controls;

/// <summary>Only draw the exposed edge; a transparent front tile cannot reveal it.</summary>
internal sealed class TaskbarStackedWindowBorder : Border
{
    protected override global::System.Type StyleKeyOverride => typeof(Border);

    public TaskbarStackedWindowBorder()
    {
        // This edge mirrors the already-animated front tile paint exactly.
        Services.Theming.ThemeTransitionPolicy.SetEnabled(this, false);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var fill = Background;
        if ((fill is null || fill is ISolidColorBrush { Color.A: 0 })
            && this.TryFindResource("SurfaceRaisedSubtleBrush", out var neutral))
            fill = neutral as IBrush;
        context.DrawRectangle(fill, null, new Rect(Bounds.Size), CornerRadius.TopLeft, CornerRadius.TopLeft);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == CornerRadiusProperty)
        {
            var radius = CornerRadius.TopLeft;
            // Expand the front tile by 2px so matching fills remain visibly separate.
            Geometry cutout = new RectangleGeometry(new Rect(-8, -2, 44, 44), radius + 2, radius + 2);
            Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(Bounds.Size)), cutout);
        }
    }
}
