using System;
using Avalonia;
using Avalonia.Controls;

namespace DaisyOS.Shell.Controls;

public class WidgetContainer : ContentControl
{
    public static readonly StyledProperty<int> ColumnSpanProperty =
        AvaloniaProperty.Register<WidgetContainer, int>(nameof(ColumnSpan), defaultValue: 4);

    public int ColumnSpan
    {
        get => GetValue(ColumnSpanProperty);
        set => SetValue(ColumnSpanProperty, Math.Max(1, value));
    }

    public static readonly StyledProperty<int> RowSpanProperty =
        AvaloniaProperty.Register<WidgetContainer, int>(nameof(RowSpan), defaultValue: 2);

    public int RowSpan
    {
        get => GetValue(RowSpanProperty);
        set => SetValue(RowSpanProperty, Math.Max(1, value));
    }

    public WidgetContainer()
    {
    }

    public WidgetContainer(int columnSpan, int rowSpan)
    {
        ColumnSpan = columnSpan;
        RowSpan = rowSpan;
    }

    /// <summary>
    /// Configures the widget dimensions in desktop grid cells (e.g. 2x2, 4x2, 4x4).
    /// </summary>
    public void SetGridSize(int columnSpan, int rowSpan)
    {
        ColumnSpan = Math.Max(1, columnSpan);
        RowSpan = Math.Max(1, rowSpan);
    }
}
