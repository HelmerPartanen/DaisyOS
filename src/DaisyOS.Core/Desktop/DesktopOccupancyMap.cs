using System;
using System.Collections.Generic;
using System.Linq;

namespace DaisyOS.Core.Desktop;

/// <summary>
/// Maintains an occupancy map for a specific monitor's grid and provides collision resolution and auto-arrange logic.
/// </summary>
public class DesktopOccupancyMap
{
    private readonly DesktopGridMetrics _metrics;
    private readonly string?[,] _occupied; // stores item IDs

    public DesktopOccupancyMap(DesktopGridMetrics metrics)
    {
        _metrics = metrics;
        _occupied = new string?[metrics.ColumnCount, metrics.RowCount];
    }

    /// <summary>
    /// Checks if two grid regions overlap.
    /// </summary>
    public static bool ItemsOverlap(GridCell cellA, int colSpanA, int rowSpanA, GridCell cellB, int colSpanB, int rowSpanB)
    {
        return cellA.Column < cellB.Column + colSpanB &&
               cellA.Column + colSpanA > cellB.Column &&
               cellA.Row < cellB.Row + rowSpanB &&
               cellA.Row + rowSpanA > cellB.Row;
    }

    /// <summary>
    /// Checks if a given cell is occupied.
    /// </summary>
    public bool IsOccupied(GridCell cell)
    {
        if (!_metrics.IsValid(cell)) return true; // Treat out of bounds as occupied
        return _occupied[cell.Column, cell.Row] != null;
    }

    /// <summary>
    /// Checks if a region of cells is occupied or out of bounds.
    /// </summary>
    public bool IsRegionOccupied(GridCell cell, int colSpan = 1, int rowSpan = 1)
    {
        for (int c = 0; c < colSpan; c++)
        {
            for (int r = 0; r < rowSpan; r++)
            {
                var checkCell = new GridCell(cell.Column + c, cell.Row + r);
                if (!_metrics.IsValid(checkCell) || _occupied[checkCell.Column, checkCell.Row] != null)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Occupies a region of cells for an item.
    /// </summary>
    public void Occupy(string itemId, GridCell cell, int colSpan = 1, int rowSpan = 1)
    {
        for (int c = 0; c < colSpan; c++)
        {
            for (int r = 0; r < rowSpan; r++)
            {
                var target = new GridCell(cell.Column + c, cell.Row + r);
                if (_metrics.IsValid(target))
                {
                    _occupied[target.Column, target.Row] = itemId;
                }
            }
        }
    }

    /// <summary>
    /// Attempts to place an item at the target cell. If it is occupied, it resolves the collision using Manhattan distance.
    /// </summary>
    /// <returns>The final resolved cell.</returns>
    public GridCell PlaceItem(string itemId, GridCell targetCell)
    {
        var resolved = ResolveCollision(targetCell);
        _occupied[resolved.Column, resolved.Row] = itemId;
        return resolved;
    }

    /// <summary>
    /// Unregisters an item from its current cell.
    /// </summary>
    public void RemoveItem(GridCell cell)
    {
        if (_metrics.IsValid(cell))
        {
            _occupied[cell.Column, cell.Row] = null;
        }
    }

    private GridCell ResolveCollision(GridCell target)
    {
        if (!IsOccupied(target))
            return target;

        var queue = new Queue<GridCell>();
        var visited = new HashSet<GridCell>();

        queue.Enqueue(target);
        visited.Add(target);

        // Prioritize candidates by lowest column first, then lowest row
        int[] dx = { -1, 0, 0, 1 };
        int[] dy = { 0, -1, 1, 0 };

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();

            if (!IsOccupied(cell))
            {
                return cell;
            }

            for (int i = 0; i < 4; i++)
            {
                var next = new GridCell(cell.Column + dx[i], cell.Row + dy[i]);
                if (_metrics.IsValid(next) && visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        throw new InvalidOperationException("The desktop grid is entirely full.");
    }

    /// <summary>
    /// Calculates the auto-arrange sequence.
    /// </summary>
    /// <param name="items">The list of items to arrange, sorted in the desired order.</param>
    /// <returns>A list of new states for the items.</returns>
    public IEnumerable<DesktopItemState> AutoArrange(IEnumerable<DesktopItemState> items, bool forceAutoArrange = false)
    {
        var result = new List<DesktopItemState>();
        var itemsList = items.ToList();

        // 1. Position multi-cell widget items (colSpan > 1 or rowSpan > 1 or widget card)
        var widgetItems = itemsList.Where(x => x.ColumnSpan > 1 || x.RowSpan > 1 || x.Id == "daisyos://widget-card" || x.Id.StartsWith("daisyos://widget") || x.Id.StartsWith("desktop.widget.")).ToList();
        foreach (var widget in widgetItems)
        {
            int colSpan = widget.ColumnSpan > 0 ? widget.ColumnSpan : 4;
            int rowSpan = widget.RowSpan > 0 ? widget.RowSpan : 2;
            int targetCol = -1;
            int targetRow = -1;

            if (widget.Column >= 0 && widget.Column + colSpan <= _metrics.ColumnCount &&
                widget.Row >= 0 && widget.Row + rowSpan <= _metrics.RowCount &&
                !IsRegionOccupied(new GridCell(widget.Column, widget.Row), colSpan, rowSpan))
            {
                targetCol = widget.Column;
                targetRow = widget.Row;
            }
            else
            {
                // Find next free slot starting from top-right down
                for (int c = _metrics.ColumnCount - colSpan; c >= 0; c--)
                {
                    for (int r = 0; r <= _metrics.RowCount - rowSpan; r++)
                    {
                        if (!IsRegionOccupied(new GridCell(c, r), colSpan, rowSpan))
                        {
                            targetCol = c;
                            targetRow = r;
                            break;
                        }
                    }
                    if (targetCol >= 0) break;
                }
            }

            if (targetCol < 0 || targetRow < 0)
            {
                // Grid is full, skip placement to avoid bypassing collision detection
                continue;
            }

            Occupy(widget.Id, new GridCell(targetCol, targetRow), colSpan, rowSpan);
            result.Add(new DesktopItemState(widget.Id, _metrics.MonitorId, targetCol, targetRow, widget.SortOrder, colSpan, rowSpan));
        }

        // 2. Position standard 1x1 items
        var standardItems = itemsList.Where(x => !widgetItems.Contains(x)).OrderBy(x => x.SortOrder).ToList();

        if (forceAutoArrange)
        {
            int currentColumn = 0;
            int currentRow = 0;

            foreach (var item in standardItems)
            {
                while (currentColumn < _metrics.ColumnCount && IsOccupied(new GridCell(currentColumn, currentRow)))
                {
                    currentRow++;
                    if (currentRow >= _metrics.RowCount)
                    {
                        currentRow = 0;
                        currentColumn++;
                    }
                }

                if (currentColumn >= _metrics.ColumnCount)
                {
                    break;
                }

                _occupied[currentColumn, currentRow] = item.Id;
                result.Add(new DesktopItemState(item.Id, _metrics.MonitorId, currentColumn, currentRow, item.SortOrder, 1, 1));

                currentRow++;
                if (currentRow >= _metrics.RowCount)
                {
                    currentRow = 0;
                    currentColumn++;
                }
            }
        }
        else
        {
            var unplaced = new List<DesktopItemState>();

            // First pass: preserve valid assigned positions
            foreach (var item in standardItems)
            {
                var cell = new GridCell(item.Column, item.Row);
                if (item.Column >= 0 && item.Row >= 0 && _metrics.IsValid(cell) && !IsOccupied(cell))
                {
                    _occupied[item.Column, item.Row] = item.Id;
                    result.Add(new DesktopItemState(item.Id, _metrics.MonitorId, item.Column, item.Row, item.SortOrder, 1, 1));
                }
                else
                {
                    unplaced.Add(item);
                }
            }

            // Second pass: position unplaced items into free cells column-major starting from column 0
            int currentColumn = 0;
            int currentRow = 0;

            foreach (var item in unplaced)
            {
                while (currentColumn < _metrics.ColumnCount && IsOccupied(new GridCell(currentColumn, currentRow)))
                {
                    currentRow++;
                    if (currentRow >= _metrics.RowCount)
                    {
                        currentRow = 0;
                        currentColumn++;
                    }
                }

                if (currentColumn >= _metrics.ColumnCount)
                {
                    break;
                }

                _occupied[currentColumn, currentRow] = item.Id;
                result.Add(new DesktopItemState(item.Id, _metrics.MonitorId, currentColumn, currentRow, item.SortOrder, 1, 1));

                currentRow++;
                if (currentRow >= _metrics.RowCount)
                {
                    currentRow = 0;
                    currentColumn++;
                }
            }
        }

        return result;
    }

}

