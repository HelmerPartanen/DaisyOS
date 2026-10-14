using System;

namespace DaisyOS.Core.Desktop;

public record struct GridCell(int Column, int Row);

public record struct Point(double X, double Y);

public record struct Rect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public class DesktopGridMetrics
{
    public string MonitorId { get; }
    public Rect WorkArea { get; }
    public double Dpi { get; }
    public double Scale { get; }
    public double CellWidth { get; }
    public double CellHeight { get; }
    
    public double GridOriginX { get; }
    public double GridOriginY { get; }
    
    public int ColumnCount { get; }
    public int RowCount { get; }
    
    public DesktopGridMetrics(
        string monitorId,
        Rect workArea,
        double dpi,
        double cellWidth,
        double cellHeight,
        double edgeInsetX = 0,
        double edgeInsetY = 0)
    {
        MonitorId = monitorId;
        WorkArea = workArea;
        Dpi = dpi;
        Scale = dpi / 96.0;
        
        // Requirements:
        // cellWidth  = GetSystemMetricsForDpi(SM_CXICONSPACING, monitorDpi)
        // cellHeight = GetSystemMetricsForDpi(SM_CYICONSPACING, monitorDpi)
        // Ensure they are strictly positive.
        if (cellWidth <= 0 || cellHeight <= 0)
            throw new ArgumentException("Cell dimensions must be strictly positive.");
            
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        
        // Calculate available grid region
        double availableWidth = Math.Max(0, WorkArea.Width - edgeInsetX * 2);
        double availableHeight = Math.Max(0, WorkArea.Height - edgeInsetY * 2);
        
        // Calculate optimal column and row count
        int cols = Math.Max(1, (int)Math.Floor(availableWidth / cellWidth));
        int rows = Math.Max(1, (int)Math.Floor(availableHeight / cellHeight));
        
        ColumnCount = cols;
        RowCount = rows;
        
        // Fine-tune CellWidth and CellHeight to distribute spacing evenly so bottom row ends exactly at taskbar top border
        CellWidth = availableWidth / cols;
        CellHeight = availableHeight / rows;
        
        GridOriginX = WorkArea.Left + edgeInsetX;
        GridOriginY = WorkArea.Top + edgeInsetY;
    }
    
    /// <summary>
    /// Snaps a screen coordinate (typically mouse pointer position) to the grid cell containing it.
    /// </summary>
    public GridCell GetNearestCell(double x, double y)
    {
        int col = (int)Math.Floor((x - GridOriginX) / CellWidth);
        int row = (int)Math.Floor((y - GridOriginY) / CellHeight);
        
        col = Math.Clamp(col, 0, ColumnCount - 1);
        row = Math.Clamp(row, 0, RowCount - 1);
        
        return new GridCell(col, row);
    }
    
    /// <summary>
    /// Maps a grid cell coordinate to its physical pixel origin (top-left).
    /// </summary>
    public Point GetCellOrigin(GridCell cell)
    {
        // Enforce invariants on mapping
        if (cell.Column < 0 || cell.Column >= ColumnCount)
            throw new ArgumentOutOfRangeException(nameof(cell.Column), "Column is out of grid bounds.");
        if (cell.Row < 0 || cell.Row >= RowCount)
            throw new ArgumentOutOfRangeException(nameof(cell.Row), "Row is out of grid bounds.");
            
        double x = GridOriginX + cell.Column * CellWidth;
        double y = GridOriginY + cell.Row * CellHeight;
        
        return new Point(x, y);
    }
    
    /// <summary>
    /// Checks if a cell is within the valid grid bounds.
    /// </summary>
    public bool IsValid(GridCell cell)
    {
        return cell.Column >= 0 && cell.Column < ColumnCount &&
               cell.Row >= 0 && cell.Row < RowCount;
    }

    /// <summary>
    /// Converts a cell to its index in the same column-major fill order AutoArrange uses
    /// (top-to-bottom down a column, then wrapping to the next column). Clamped to the grid.
    /// </summary>
    public int GetSlotIndex(GridCell cell)
    {
        int col = Math.Clamp(cell.Column, 0, ColumnCount - 1);
        int row = Math.Clamp(cell.Row, 0, RowCount - 1);
        return col * RowCount + row;
    }

    /// <summary>
    /// Inverse of <see cref="GetSlotIndex"/>: maps a fill-order slot back to its cell.
    /// </summary>
    public GridCell GetCellForSlot(int slot)
    {
        slot = Math.Clamp(slot, 0, ColumnCount * RowCount - 1);
        return new GridCell(slot / RowCount, slot % RowCount);
    }
}