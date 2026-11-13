using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using DaisyOS.Core.Desktop;
using DaisyOS.Shell.ViewModels;
using Rect = Avalonia.Rect;
using Point = Avalonia.Point;

namespace DaisyOS.Shell.Controls;

public class DesktopReorderPanel : Panel
{
    private readonly Dictionary<DesktopItemId, (Point Origin, Size Size)> _previousGeometry = new();
    private bool _suppressFlipOnce;

    public DesktopReorderPanel()
    {
        ClipToBounds = false;
    }

    internal Rect? WidgetSnapBounds
    {
        get
        {
            if (Metrics is not { } metrics || ViewModel is not { } viewModel) return null;
            var item = viewModel.Items.FirstOrDefault(item => item.IsWidgetCard && item.IsDragging);
            if (item is null) return null;
            var cell = viewModel.GetCommittedCell(item.Id);
            if (!metrics.IsValid(cell)) return null;
            var origin = metrics.GetCellOrigin(cell);
            // WidgetContainer's outer frame sits four pixels inside its grid footprint.
            return new Rect(origin.X + 4, origin.Y + 4,
                Math.Max(0, metrics.CellWidth * item.ColumnSpan - 8),
                Math.Max(0, metrics.CellHeight * item.RowSpan - 8));
        }
    }

    public static readonly StyledProperty<DesktopGridMetrics?> MetricsProperty =
        AvaloniaProperty.Register<DesktopReorderPanel, DesktopGridMetrics?>(nameof(Metrics));

    public DesktopGridMetrics? Metrics
    {
        get => GetValue(MetricsProperty);
        set => SetValue(MetricsProperty, value);
    }

    public static readonly StyledProperty<DesktopViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<DesktopReorderPanel, DesktopViewModel?>(nameof(ViewModel));

    public DesktopViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    static DesktopReorderPanel()
    {
        MetricsProperty.Changed.AddClassHandler<DesktopReorderPanel>((x, _) =>
        {
            x.SuppressFlipOnce();
            x.InvalidateArrange();
        });
        ViewModelProperty.Changed.AddClassHandler<DesktopReorderPanel>((x, _) => x.InvalidateArrange());
    }

    public void SuppressFlipOnce()
    {
        _suppressFlipOnce = true;
        _previousGeometry.Clear();
    }

    private static readonly Transitions FlipTransitions = new()
    {
        new TransformOperationsTransition
        {
            Property = Visual.RenderTransformProperty,
            Duration = TimeSpan.FromMilliseconds(240),
            Easing = new Avalonia.Animation.Easings.CubicEaseOut()
        }
    };

    protected override Size MeasureOverride(Size availableSize)
    {
        var cellWidth = Metrics?.CellWidth ?? 74;
        var cellHeight = Metrics?.CellHeight ?? 88;
        foreach (var child in Children)
        {
            if (child is Control control && control.DataContext is DesktopItemViewModel item)
            {
                child.Measure(new Size(cellWidth * item.ColumnSpan, cellHeight * item.RowSpan));
            }
            else
            {
                child.Measure(new Size(cellWidth, cellHeight));
            }
        }
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Metrics == null || ViewModel == null)
        {
            foreach (var child in Children)
            {
                child.Arrange(new Rect(0, 0, child.DesiredSize.Width, child.DesiredSize.Height));
            }
            return finalSize;
        }

        var cellWidth = Metrics.CellWidth;
        var cellHeight = Metrics.CellHeight;

        foreach (var child in Children)
        {
            if (child is Control control && control.DataContext is DesktopItemViewModel item)
            {
                control.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);

                var itemWidth = cellWidth * item.ColumnSpan;
                var itemHeight = cellHeight * item.RowSpan;
                var newSize = new Size(itemWidth, itemHeight);

                if (item.IsDragging)
                {
                    control.ZIndex = 100;
                    control.Transitions = null;
                    control.RenderTransform = null;
                    child.Arrange(new Rect(item.DragX, item.DragY, itemWidth, itemHeight));
                    _previousGeometry[item.Id] = (new Point(item.DragX, item.DragY), newSize);
                    continue;
                }
                else
                {
                    control.ZIndex = item.IsRenaming ? 50 : 0;
                }

                var cell = ViewModel.GetCommittedCell(item.Id);
                if (!Metrics.IsValid(cell))
                {
                    child.Arrange(new Rect(0, 0, itemWidth, itemHeight));
                    continue;
                }

                var origin = Metrics.GetCellOrigin(cell);
                var newOrigin = new Point(origin.X, origin.Y);
                item.X = origin.X;
                item.Y = origin.Y;

                if (!item.IsTrash && Application.Current is not App { ReduceMotionEnabled: true } && !_suppressFlipOnce && _previousGeometry.TryGetValue(item.Id, out var prev))
                {
                    double dX = prev.Origin.X - newOrigin.X;
                    double dY = prev.Origin.Y - newOrigin.Y;
                    double scaleX = (newSize.Width > 0 && prev.Size.Width > 0) ? prev.Size.Width / newSize.Width : 1.0;
                    double scaleY = (newSize.Height > 0 && prev.Size.Height > 0) ? prev.Size.Height / newSize.Height : 1.0;

                    bool hasMove = Math.Abs(dX) > 0.5 || Math.Abs(dY) > 0.5;
                    bool hasResize = Math.Abs(scaleX - 1.0) > 0.01 || Math.Abs(scaleY - 1.0) > 0.01;

                    if (hasMove || hasResize)
                    {
                        // Retarget from the visible animated position, not the old destination.
                        // Only retarget when layout changes; idle arrange passes must not restart motion.
                        var live = control.RenderTransform?.Value ?? Matrix.Identity;
                        dX += live.M31;
                        dY += live.M32;
                        scaleX *= live.M11;
                        scaleY *= live.M22;
                        double transX = Math.Abs(dX) < 0.001 ? 0 : dX;
                        double transY = Math.Abs(dY) < 0.001 ? 0 : dY;
                        string transformStr = FormattableString.Invariant(
                            $"translate({transX:F2}px, {transY:F2}px) scale({scaleX:F4}, {scaleY:F4})");

                        control.Transitions = null;
                        control.RenderTransform = TransformOperations.Parse(transformStr);

                        Dispatcher.UIThread.Post(() =>
                        {
                            control.Transitions = FlipTransitions;
                            control.RenderTransform = TransformOperations.Identity;
                        }, DispatcherPriority.Render);
                    }
                }

                _previousGeometry[item.Id] = (newOrigin, newSize);
                child.Arrange(new Rect(origin.X, origin.Y, itemWidth, itemHeight));
            }
            else
            {
                child.Arrange(new Rect(0, 0, cellWidth, cellHeight));
            }
        }

        _suppressFlipOnce = false;

        var currentIds = new global::System.Collections.Generic.HashSet<DesktopItemId>();
        foreach (var child in Children)
        {
            if (child is Control control && control.DataContext is DesktopItemViewModel item)
            {
                currentIds.Add(item.Id);
            }
        }
        var keysToRemove = new global::System.Collections.Generic.List<DesktopItemId>();
        foreach (var key in _previousGeometry.Keys)
        {
            if (!currentIds.Contains(key))
            {
                keysToRemove.Add(key);
            }
        }
        foreach (var key in keysToRemove)
        {
            _previousGeometry.Remove(key);
        }

        return finalSize;
    }

}
