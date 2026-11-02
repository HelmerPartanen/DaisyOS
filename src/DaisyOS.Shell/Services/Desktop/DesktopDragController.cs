using System;
using Avalonia;
using DaisyOS.Core.Desktop;
using DaisyOS.Shell.ViewModels;
using Point = Avalonia.Point;

namespace DaisyOS.Shell.Services.Desktop;

public sealed class DesktopDragController
{
    // Use a small but intentional desktop-style threshold. This prevents an
    // ordinary click (and the tiny pointer movement common on touchpads) from
    // unexpectedly turning into a drag.
    private const double DragStartThreshold = 6.0;
    private readonly DesktopMotionSettings _settings;
    private DesktopItemViewModel? _pressedItem;
    private DesktopItemViewModel? _draggedItem;
    private bool _overTrash;
    private Size _pressedItemSize = new(74, 88);
    private Point _pressPoint;
    private Point _grabOffset;

    public DragSession? ActiveSession { get; private set; }
    public DragState State { get; private set; } = DragState.Idle;

    public event Action<DragSession>? SessionStarted;
    public event Action<DragSession>? SessionUpdated;
    public event Action<DragSession, bool>? SessionEnded;
    public event Action? ReorderReflowed;

    public DesktopDragController(DesktopMotionSettings? settings = null)
    {
        _settings = settings ?? DesktopMotionSettings.Default;
    }

    public void OnPointerPressed(
        DesktopItemViewModel? item,
        Point pointerPositionInSurface,
        Point pointerPositionInItem,
        Size itemSize,
        DesktopViewModel viewModel,
        DesktopGridMetrics? metrics,
        Action? invalidatePanel = null)
    {
        if (item is null) return;

        _pressedItem = item;
        _pressedItemSize = itemSize;
        _pressPoint = pointerPositionInSurface;
        _grabOffset = pointerPositionInItem;

        // Selection is handled by DesktopView immediately. Keep the press
        // pending until the pointer makes a deliberate movement.
    }

    public void StartDirectDrag(
        DesktopItemViewModel item,
        Point pointerPos,
        Point grabOffset,
        Size itemSize,
        DesktopViewModel viewModel,
        DesktopGridMetrics? metrics,
        Action? invalidatePanel)
    {
        if (ActiveSession != null || _draggedItem != null) return;

        _pressPoint = pointerPos;
        _grabOffset = grabOffset;
        _pressedItem = item;
        _pressedItemSize = itemSize;

        StartDrag(item, pointerPos, viewModel, metrics, itemSize, invalidatePanel);
    }

    private void StartDrag(
        DesktopItemViewModel item,
        Point pointerPos,
        DesktopViewModel viewModel,
        DesktopGridMetrics? metrics,
        Size itemSize,
        Action? invalidatePanel)
    {
        if (ActiveSession != null || _draggedItem != null) return;

        int originalIndex = viewModel.GetCommittedIndex(item.Id);
        // A drag is a manipulation gesture, not a retained selection. Clearing
        // this here keeps the selected/hover treatment from following the item
        // as a stale background during and after the drop.
        viewModel.ClearSelection();
        _draggedItem = item;
        item.IsDragging = true;
        item.IsHiddenPlaceholder = true;
        item.DragX = pointerPos.X - _grabOffset.X;
        item.DragY = pointerPos.Y - _grabOffset.Y;

        State = DragState.Reordering;
        ActiveSession = new DragSession
        {
            SourceId = item.Id,
            OriginalIndex = originalIndex,
            PressPoint = _pressPoint,
            GrabOffset = _grabOffset,
            CurrentPointer = pointerPos,
            CurrentDragCenter = new Point(item.DragX + itemSize.Width / 2.0, item.DragY + itemSize.Height / 2.0),
            PreviewIndex = originalIndex,
            State = DragState.Reordering,
            ItemSize = itemSize
        };

        SessionStarted?.Invoke(ActiveSession);
        invalidatePanel?.Invoke();
    }

    public void OnPointerMoved(
        Point currentPointer,
        DesktopViewModel viewModel,
        DesktopGridMetrics? metrics,
        Action? invalidatePanel = null)
    {
        if (_draggedItem == null && _pressedItem != null)
        {
            var deltaX = currentPointer.X - _pressPoint.X;
            var deltaY = currentPointer.Y - _pressPoint.Y;
            if (deltaX * deltaX + deltaY * deltaY < DragStartThreshold * DragStartThreshold)
            {
                return;
            }

            StartDrag(_pressedItem, currentPointer, viewModel, metrics, _pressedItemSize, invalidatePanel);
        }

        if (_draggedItem != null && ActiveSession != null && metrics != null)
        {
            _draggedItem.DragX = currentPointer.X - _grabOffset.X;
            _draggedItem.DragY = currentPointer.Y - _grabOffset.Y;

            ActiveSession.CurrentPointer = currentPointer;

            double itemWidth = metrics.CellWidth * _draggedItem.ColumnSpan;
            double itemHeight = metrics.CellHeight * _draggedItem.RowSpan;

            ActiveSession.CurrentDragCenter = new Point(
                _draggedItem.DragX + itemWidth / 2.0,
                _draggedItem.DragY + itemHeight / 2.0);

            GridCell centerCell = metrics.GetNearestCell(
                ActiveSession.CurrentDragCenter.X,
                ActiveSession.CurrentDragCenter.Y);

            int targetCol = centerCell.Column - (_draggedItem.ColumnSpan - 1) / 2;
            int targetRow = centerCell.Row - (_draggedItem.RowSpan - 1) / 2;

            int maxCol = Math.Max(0, metrics.ColumnCount - _draggedItem.ColumnSpan);
            int maxRow = Math.Max(0, metrics.RowCount - _draggedItem.RowSpan);
            GridCell targetCell = new GridCell(Math.Clamp(targetCol, 0, maxCol), Math.Clamp(targetRow, 0, maxRow));

            _overTrash = _draggedItem.CanBeTrashed && viewModel.GetTrashAtPoint(currentPointer, metrics) != null;
            if (!_overTrash && metrics.IsValid(targetCell))
            {
                bool moved = viewModel.ReorderItem(_draggedItem.Id, targetCell, centerCell, metrics);
                if (moved)
                {
                    ReorderReflowed?.Invoke();
                }
            }

            SessionUpdated?.Invoke(ActiveSession);
            invalidatePanel?.Invoke();
        }

    }

    public void OnPointerReleased(
        DesktopViewModel? viewModel,
        DesktopGridMetrics? metrics,
        Action? invalidatePanel = null)
    {
        if (_draggedItem != null && viewModel != null && metrics != null)
        {
            var cell = viewModel.GetCommittedCell(_draggedItem.Id);
            if (metrics.IsValid(cell))
            {
                var origin = metrics.GetCellOrigin(cell);
                _draggedItem.DragX = origin.X;
                _draggedItem.DragY = origin.Y;
            }

            _draggedItem.IsDragging = false;
            _draggedItem.IsHiddenPlaceholder = false;

            if (_overTrash) _ = viewModel.MoveItemToTrash(_draggedItem, metrics);
            viewModel.SaveLayout();
            if (ActiveSession is { } session)
            {
                SessionEnded?.Invoke(session, true);
            }
        }

        _overTrash = false;
        _draggedItem = null;
        ActiveSession = null;
        _pressedItem = null;
        State = DragState.Idle;
        invalidatePanel?.Invoke();
    }

    public void CancelDrag(DesktopViewModel? viewModel = null, Action? invalidatePanel = null)
    {
        if (_pressedItem == null && _draggedItem == null && ActiveSession == null) return;

        if (_draggedItem != null)
        {
            _draggedItem.IsDragging = false;
            _draggedItem.IsHiddenPlaceholder = false;
            if (ActiveSession != null)
            {
                SessionEnded?.Invoke(ActiveSession, false);
            }
        }

        _overTrash = false;
        _draggedItem = null;
        ActiveSession = null;
        _pressedItem = null;
        State = DragState.Idle;
        invalidatePanel?.Invoke();
    }

    public void OnPointerCaptureLost(DesktopViewModel? viewModel, Action? invalidatePanel = null)
    {
        CancelDrag(viewModel, invalidatePanel);
    }
}
