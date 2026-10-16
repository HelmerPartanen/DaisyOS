using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DaisyOS.Shell.Controls;

/// <summary>Lightweight placeholders that reserve space while page data loads.</summary>
public sealed class SkeletonLoader : StackPanel
{
    public SkeletonLoader()
    {
        IsHitTestVisible = false;
        Spacing = 16;
        for (var i = 0; i < 6; i++)
        {
            var lines = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(Placeholder(16, 0));
            lines.Children.Add(Placeholder(12, 80));
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), ColumnSpacing = 16 };
            grid.Children.Add(Placeholder(48, 0));
            Grid.SetColumn(lines, 1);
            grid.Children.Add(lines);
            Children.Add(grid);
        }
    }

    private Border Placeholder(double height, double rightMargin)
    {
        var shape = new Border { Height = height, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, rightMargin, 0) };
        shape.Bind(Border.BackgroundProperty, this.GetResourceObservable("StateHoverBrush"));
        return shape;
    }
}
