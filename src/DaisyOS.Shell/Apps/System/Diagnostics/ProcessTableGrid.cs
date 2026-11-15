using Avalonia;
using Avalonia.Controls;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

/// <summary>One column definition for sortable headers and virtualized process rows.</summary>
public sealed class ProcessTableGrid : Grid
{
    public static readonly StyledProperty<bool> AdvancedColumnsProperty = AvaloniaProperty.Register<ProcessTableGrid, bool>(nameof(AdvancedColumns));
    public bool AdvancedColumns { get => GetValue(AdvancedColumnsProperty); set => SetValue(AdvancedColumnsProperty, value); }
    public ProcessTableGrid() => UpdateColumns();
    static ProcessTableGrid() => AdvancedColumnsProperty.Changed.AddClassHandler<ProcessTableGrid>((grid, _) => grid.UpdateColumns());
    private void UpdateColumns() => ColumnDefinitions = new(AdvancedColumns ? "*,92,92,92,92,92,92,92,92,92" : "*,92,92,92,92,92,0,0,0,0");
}
