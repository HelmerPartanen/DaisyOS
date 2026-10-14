using System;
using System.Collections.Generic;
using System.IO;
using DaisyOS.Core.Desktop;
using DaisyOS.Shell.Services.Desktop;
using DaisyOS.Shell.ViewModels;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaSize = Avalonia.Size;

namespace DaisyOS.Shell.Tests;

public class DesktopViewModelTests
{
    private static DesktopViewModel CreateTestViewModel() =>
        new DesktopViewModel(new DesktopLayoutStore(Path.Combine(Path.GetTempPath(), $"daisy_test_layout_{Guid.NewGuid():N}.json")));

    [Fact]
    public async global::System.Threading.Tasks.Task DroppingFileOnTrashCreatesRestorableMetadata()
    {
        var previousCulture = global::System.Globalization.CultureInfo.CurrentCulture;
        global::System.Globalization.CultureInfo.CurrentCulture = new global::System.Globalization.CultureInfo("fi-FI");
        var vm = CreateTestViewModel();
        vm.ClearItems();
        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 800, 600), 96, 100, 100);
        var path = Path.Combine(Path.GetTempPath(), "daisy-trash-drop-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "owned test fixture");
        var file = new DesktopItemViewModel(new DesktopItemState(path, "primary", 0, 1, 1));
        var trash = new DesktopItemViewModel(new DesktopItemState(DesktopItemViewModel.TrashItemId, "primary", 0, 0, 0));
        vm.AddItem(trash); vm.AddItem(file); vm.CalculateLayout(metrics);
        try
        {
            var drag = new DesktopDragController();
            var start = metrics.GetCellOrigin(new GridCell(0, 1));
            var destination = metrics.GetCellOrigin(new GridCell(0, 0));
            drag.OnPointerPressed(file, new AvaloniaPoint(start.X + 30, start.Y + 30), new AvaloniaPoint(30, 30), new AvaloniaSize(100, 100), vm, metrics);
            drag.OnPointerMoved(new AvaloniaPoint(destination.X + 30, destination.Y + 30), vm, metrics);
            Assert.Equal(new GridCell(0, 0), vm.GetCommittedCell(trash.Id));
            drag.OnPointerReleased(vm, metrics);
            for (int attempt = 0; attempt < 100 && vm.Items.Contains(file); attempt++)
                await global::System.Threading.Tasks.Task.Delay(20);
            Assert.DoesNotContain(file, vm.Items);
            Assert.False(File.Exists(path));
            var operations = await DaisyOS.Shell.Apps.Files.Services.FileSystemService.GetTrashOperationsAsync();
            var operation = Assert.Single(operations, entry => entry.OriginalPath == path);
            Assert.True(File.Exists(operation.InfoPath));
        }
        finally
        {
            var operations = await DaisyOS.Shell.Apps.Files.Services.FileSystemService.GetTrashOperationsAsync();
            foreach (var operation in operations.Where(entry => entry.OriginalPath == path))
                await DaisyOS.Shell.Apps.Files.Services.FileSystemService.RestoreFromTrashAsync(operation);
            if (File.Exists(path)) File.Delete(path);
            global::System.Globalization.CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void TrashIconTracksEmptyFilesAndDirectories()
    {
        var vm = CreateTestViewModel();
        var directory = Path.Combine(Path.GetTempPath(), "daisy-trash-icons-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var trash = Assert.Single(vm.Items, item => item.IsTrash);
            vm.RefreshTrashIcon(false, directory);
            Assert.EndsWith("Trash_Dark.png", trash.IconPath);
            Directory.CreateDirectory(Path.Combine(directory, "folder"));
            vm.RefreshTrashIcon(true, directory);
            Assert.EndsWith("Trash_Item_Light.png", trash.IconPath);
            Directory.Delete(Path.Combine(directory, "folder"));
            File.WriteAllText(Path.Combine(directory, "file"), "test");
            vm.RefreshTrashIcon(false, directory);
            Assert.EndsWith("Trash_Item_Dark.png", trash.IconPath);
            File.Delete(Path.Combine(directory, "file"));
            vm.RefreshTrashIcon(false, directory);
            Assert.EndsWith("Trash_Dark.png", trash.IconPath);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void OtherItemsCannotMoveTrashButDraggingTrashReflowsOthers()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();
        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 800, 600), 96, 100, 100);
        var first = new DesktopItemViewModel(new DesktopItemState("first", "primary", 0, 0, 0));
        var trash = new DesktopItemViewModel(new DesktopItemState(DesktopItemViewModel.TrashItemId, "primary", 0, 1, 1));
        var last = new DesktopItemViewModel(new DesktopItemState("last", "primary", 0, 2, 2));
        vm.AddItem(first); vm.AddItem(trash); vm.AddItem(last);
        vm.CalculateLayout(metrics);
        Assert.False(vm.ReorderItem(first.Id, new GridCell(0, 1), metrics));
        Assert.True(vm.ReorderItem(first.Id, new GridCell(0, 2), metrics));
        Assert.Equal(new GridCell(0, 1), vm.GetCommittedCell(trash.Id));
        Assert.True(vm.ReorderItem(trash.Id, new GridCell(0, 2), metrics));
        Assert.Equal(new GridCell(0, 2), vm.GetCommittedCell(trash.Id));
        Assert.Equal(new GridCell(0, 1), vm.GetCommittedCell(first.Id));
    }

    [Fact]
    public void MoveItemHelper_ReordersElementsCorrectly()
    {
        var list = new List<string> { "A", "B", "C", "D" };

        DesktopViewModel.MoveItem(list, 0, 2); // Move A to slot 2: B, C, A, D
        Assert.Equal(new[] { "B", "C", "A", "D" }, list);

        DesktopViewModel.MoveItem(list, 3, 0); // Move D to slot 0: D, B, C, A
        Assert.Equal(new[] { "D", "B", "C", "A" }, list);

        DesktopViewModel.MoveItem(list, 1, 1); // Same index: no change
        Assert.Equal(new[] { "D", "B", "C", "A" }, list);
    }

    [Fact]
    public void AddItem_StoresCommittedCell()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();
        var itemA = new DesktopItemViewModel(new DesktopItemState("A", "primary", 1, 2));

        vm.AddItem(itemA);
        Assert.Equal(new GridCell(1, 2), vm.GetCommittedCell(itemA.Id));
    }

    [Fact]
    public void Trash_IsAddedAsTheFirstDesktopItem()
    {
        var vm = CreateTestViewModel();
        var trash = Assert.Single(vm.Items, item => item.Id.Value == DesktopItemViewModel.TrashItemId);
        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 800, 600), 96.0, 100, 100);

        vm.CalculateLayout(metrics);

        Assert.Equal("Trash", trash.Label);
        Assert.Equal(new GridCell(0, 0), vm.GetCommittedCell(trash.Id));
    }

    [Fact]
    public void Trash_CanBeReorderedLikeAnyOtherDesktopItem()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();
        var trash = new DesktopItemViewModel(new DesktopItemState(DesktopItemViewModel.TrashItemId, "primary", 0, 0, 0));
        var app = new DesktopItemViewModel(new DesktopItemState("app", "primary", 0, 1, 1));
        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 800, 600), 96.0, 100, 100);

        vm.AddItem(trash);
        vm.AddItem(app);
        vm.CalculateLayout(metrics);

        Assert.True(vm.ReorderItem(trash.Id, new GridCell(0, 1), metrics));
        Assert.Equal(new GridCell(0, 1), vm.GetCommittedCell(trash.Id));
        Assert.Equal(new GridCell(0, 0), vm.GetCommittedCell(app.Id));
    }

    [Fact]
    public void ReorderItem_ReflowsVerticallyWithinAColumn()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();

        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 800, 600), 96.0, 100, 100);

        var itemA = new DesktopItemViewModel(new DesktopItemState("A", "primary", 0, 0, 0));
        var itemB = new DesktopItemViewModel(new DesktopItemState("B", "primary", 0, 1, 1));

        vm.AddItem(itemA);
        vm.AddItem(itemB);
        vm.CalculateLayout(metrics);

        var itemC = new DesktopItemViewModel(new DesktopItemState("C", "primary", 0, 2, 2));
        vm.AddItem(itemC);
        vm.CalculateLayout(metrics);

        // Reorder A from the top to the bottom of its column.
        bool reordered = vm.ReorderItem(itemA.Id, new GridCell(0, 2), metrics);
        Assert.True(reordered);

        // The other items move only up and down within column zero.
        Assert.Equal(new GridCell(0, 2), vm.GetCommittedCell(itemA.Id));
        Assert.Equal(new GridCell(0, 0), vm.GetCommittedCell(itemB.Id));
        Assert.Equal(new GridCell(0, 1), vm.GetCommittedCell(itemC.Id));
    }

    [Fact]
    public void ReorderItem_MovesToAnEmptyCellWithoutCollapsingTheGap()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();

        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 400, 400), 96.0, 100, 100);
        var itemA = new DesktopItemViewModel(new DesktopItemState("A", "primary", 0, 0, 0));
        var itemB = new DesktopItemViewModel(new DesktopItemState("B", "primary", 0, 1, 1));
        vm.AddItem(itemA);
        vm.AddItem(itemB);
        vm.CalculateLayout(metrics);

        // Cell (0, 3) is intentionally empty. It must remain the final drop
        // target rather than being treated as an index after the last item.
        Assert.True(vm.ReorderItem(itemA.Id, new GridCell(0, 3), metrics));
        Assert.Equal(new GridCell(0, 3), vm.GetCommittedCell(itemA.Id));
        Assert.Equal(new GridCell(0, 1), vm.GetCommittedCell(itemB.Id));
    }

    [Fact]
    public void ReorderItem_WrapsToTheNextColumnOnlyAfterFillingTheCurrentOne()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();

        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 200, 300), 96.0, 100, 100);

        var itemA = new DesktopItemViewModel(new DesktopItemState("A", "primary", 0, 0, 0));
        var itemB = new DesktopItemViewModel(new DesktopItemState("B", "primary", 0, 1, 1));
        var itemC = new DesktopItemViewModel(new DesktopItemState("C", "primary", 0, 2, 2));
        var itemD = new DesktopItemViewModel(new DesktopItemState("D", "primary", 1, 0, 3));
        vm.AddItem(itemA);
        vm.AddItem(itemB);
        vm.AddItem(itemC);
        vm.AddItem(itemD);
        vm.CalculateLayout(metrics);

        // Moving D to the first slot pushes C across the full column boundary.
        bool reordered = vm.ReorderItem(itemD.Id, new GridCell(0, 0), metrics);
        Assert.True(reordered);
        Assert.Equal(new GridCell(0, 0), vm.GetCommittedCell(itemD.Id));
        Assert.Equal(new GridCell(0, 1), vm.GetCommittedCell(itemA.Id));
        Assert.Equal(new GridCell(0, 2), vm.GetCommittedCell(itemB.Id));
        Assert.Equal(new GridCell(1, 0), vm.GetCommittedCell(itemC.Id));
    }

    [Fact]
    public void ReorderItem_SameCell_ReturnsFalse_Idempotent()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();

        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 800, 600), 96.0, 100, 100);
        var itemA = new DesktopItemViewModel(new DesktopItemState("A", "primary", 0, 0, 0));
        vm.AddItem(itemA);
        vm.CalculateLayout(metrics);

        bool reordered = vm.ReorderItem(itemA.Id, new GridCell(0, 0), metrics);
        Assert.False(reordered);
    }

    [Fact]
    public void IsEditMode_PropagatesToItems()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();
        var itemA = new DesktopItemViewModel(new DesktopItemState("A", "primary", 0, 0, 0));
        vm.AddItem(itemA);

        Assert.False(itemA.IsEditMode);
        vm.IsEditMode = true;
        Assert.True(itemA.IsEditMode);
        vm.IsEditMode = false;
        Assert.False(itemA.IsEditMode);
    }

    [Fact]
    public void SelectItem_SelectsOnlyTheClickedDesktopItem()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();
        var itemA = new DesktopItemViewModel(new DesktopItemState("A", "primary", 0, 0));
        var itemB = new DesktopItemViewModel(new DesktopItemState("B", "primary", 0, 1));
        vm.AddItem(itemA);
        vm.AddItem(itemB);

        vm.SelectItem(itemB);

        Assert.False(itemA.IsSelected);
        Assert.True(itemB.IsSelected);
        vm.ClearSelection();
        Assert.False(itemB.IsSelected);
    }

    [Fact]
    public void DragController_DoesNotStartDraggingUntilPointerCrossesThreshold()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();
        var item = new DesktopItemViewModel(new DesktopItemState("A", "primary", 0, 0));
        vm.AddItem(item);
        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 400, 400), 96.0, 100, 100);
        var controller = new DesktopDragController();
        vm.SelectItem(item);

        controller.OnPointerPressed(item, new AvaloniaPoint(10, 10), new AvaloniaPoint(5, 5), new AvaloniaSize(74, 88), vm, metrics);
        controller.OnPointerMoved(new AvaloniaPoint(14, 14), vm, metrics);
        Assert.False(item.IsDragging);

        controller.OnPointerMoved(new AvaloniaPoint(17, 17), vm, metrics);
        Assert.True(item.IsDragging);
        Assert.False(item.IsSelected);
    }

    [Fact]
    public void ClearSelection_RemovesDesktopItemHighlightBeforeActivation()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();
        var item = new DesktopItemViewModel(new DesktopItemState("A", "primary", 0, 0));
        vm.AddItem(item);
        vm.SelectItem(item);

        vm.ClearSelection();

        Assert.False(item.IsSelected);
    }

    [Fact]
    public void WidgetCard_IsAddedAndPositionedInTopRightCorner()
    {
        var vm = CreateTestViewModel();
        var widget = Assert.Single(vm.Items, item => item.Id.Value == DesktopItemViewModel.WidgetCardItemId);
        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 800, 600), 96.0, 100, 100);

        vm.CalculateLayout(metrics);

        Assert.True(widget.IsWidgetCard);
        Assert.Equal(4, widget.ColumnSpan);
        Assert.Equal(2, widget.RowSpan);
        Assert.Equal(new GridCell(metrics.ColumnCount - 4, 0), vm.GetCommittedCell(widget.Id));
    }

    [Fact]
    public void WidgetCard_CanBeMovedAndReorderedLikeNormalDesktopApps()
    {
        var vm = CreateTestViewModel();
        var widget = Assert.Single(vm.Items, item => item.Id.Value == DesktopItemViewModel.WidgetCardItemId);
        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 800, 600), 96.0, 100, 100);

        vm.CalculateLayout(metrics);

        bool moved = vm.ReorderItem(widget.Id, new GridCell(0, 2), metrics);
        Assert.True(moved);
        Assert.Equal(new GridCell(0, 2), vm.GetCommittedCell(widget.Id));
    }

    [Fact]
    public void WidgetCard_DisplacesIconsDownWhenMovedOverThem()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();
        var widget = new DesktopItemViewModel(new DesktopItemState(DesktopItemViewModel.WidgetCardItemId, "primary", 4, 0, 999))
        {
            ColumnSpan = 4,
            RowSpan = 2,
            IsWidgetCard = true
        };
        var itemA = new DesktopItemViewModel(new DesktopItemState("A", "primary", 0, 0, 0));
        var itemB = new DesktopItemViewModel(new DesktopItemState("B", "primary", 0, 1, 1));
        var itemC = new DesktopItemViewModel(new DesktopItemState("C", "primary", 0, 2, 2));

        vm.AddItem(widget);
        vm.AddItem(itemA);
        vm.AddItem(itemB);
        vm.AddItem(itemC);

        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 800, 600), 96.0, 100, 100);
        vm.CalculateLayout(metrics);

        // Move widget to (0, 0) overlapping column 0 rows 0..1
        bool moved = vm.ReorderItem(widget.Id, new GridCell(0, 0), metrics);
        Assert.True(moved);

        Assert.Equal(new GridCell(0, 0), vm.GetCommittedCell(widget.Id));
        Assert.Equal(new GridCell(0, 2), vm.GetCommittedCell(itemA.Id));
        Assert.Equal(new GridCell(0, 3), vm.GetCommittedCell(itemB.Id));
        Assert.Equal(new GridCell(0, 4), vm.GetCommittedCell(itemC.Id));
    }

    [Fact]
    public void WidgetCard_ReturnsIconsBackUpWhenMovedAway()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();
        var widget = new DesktopItemViewModel(new DesktopItemState(DesktopItemViewModel.WidgetCardItemId, "primary", 0, 0, 999))
        {
            ColumnSpan = 4,
            RowSpan = 2,
            IsWidgetCard = true
        };
        var itemA = new DesktopItemViewModel(new DesktopItemState("A", "primary", 0, 2, 0));
        var itemB = new DesktopItemViewModel(new DesktopItemState("B", "primary", 0, 3, 1));

        vm.AddItem(widget);
        vm.AddItem(itemA);
        vm.AddItem(itemB);

        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 800, 600), 96.0, 100, 100);

        // Move widget away to (4, 0)
        bool moved = vm.ReorderItem(widget.Id, new GridCell(4, 0), metrics);
        Assert.True(moved);

        Assert.Equal(new GridCell(4, 0), vm.GetCommittedCell(widget.Id));
        Assert.Equal(new GridCell(0, 0), vm.GetCommittedCell(itemA.Id));
        Assert.Equal(new GridCell(0, 1), vm.GetCommittedCell(itemB.Id));
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(4, 2)]
    [InlineData(4, 4)]
    public void WidgetContainer_SupportsCustomCellDimensions(int colSpan, int rowSpan)
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();
        var widget = vm.AddWidget("custom-widget", colSpan, rowSpan);

        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 800, 600), 96.0, 100, 100);
        vm.CalculateLayout(metrics);

        Assert.Equal(colSpan, widget.ColumnSpan);
        Assert.Equal(rowSpan, widget.RowSpan);
        Assert.True(widget.IsWidgetCard);

        // Can change cell dimensions dynamically via SetGridSize
        widget.SetGridSize(2, 4);
        Assert.Equal(2, widget.ColumnSpan);
        Assert.Equal(4, widget.RowSpan);
    }

    [Fact]
    public void ReorderItem_DoesNotShiftItemAtFarEdgeWhenReorderingOtherItems()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();

        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 1200, 600), 96.0, 100, 100);

        var itemA = new DesktopItemViewModel(new DesktopItemState("A", "primary", 0, 0, 0));
        var itemB = new DesktopItemViewModel(new DesktopItemState("B", "primary", 0, 1, 1));
        var itemC = new DesktopItemViewModel(new DesktopItemState("C", "primary", 0, 2, 2));
        var itemZ = new DesktopItemViewModel(new DesktopItemState("Z", "primary", 10, 2, 3));

        vm.AddItem(itemA);
        vm.AddItem(itemB);
        vm.AddItem(itemC);
        vm.AddItem(itemZ);
        vm.CalculateLayout(metrics);

        Assert.Equal(new GridCell(10, 2), vm.GetCommittedCell(itemZ.Id));

        // Reorder A to (0, 1), triggering a flip transition between A and B
        bool reordered = vm.ReorderItem(itemA.Id, new GridCell(0, 1), metrics);
        Assert.True(reordered);

        Assert.Equal(new GridCell(0, 1), vm.GetCommittedCell(itemA.Id));
        Assert.Equal(new GridCell(0, 0), vm.GetCommittedCell(itemB.Id));
        Assert.Equal(new GridCell(0, 2), vm.GetCommittedCell(itemC.Id));

        // Far edge item Z must NOT have moved back to the left edge
        Assert.Equal(new GridCell(10, 2), vm.GetCommittedCell(itemZ.Id));

        // Reorder A again to (0, 2)
        bool reordered2 = vm.ReorderItem(itemA.Id, new GridCell(0, 2), metrics);
        Assert.True(reordered2);

        Assert.Equal(new GridCell(0, 2), vm.GetCommittedCell(itemA.Id));
        Assert.Equal(new GridCell(0, 1), vm.GetCommittedCell(itemC.Id));

        // Far edge item Z still firmly at (10, 2)
        Assert.Equal(new GridCell(10, 2), vm.GetCommittedCell(itemZ.Id));
    }

    [Fact]
    public void ReorderItem_DirectSwapBetweenFarEdgeAndLeftEdgeItem()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();

        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 1200, 600), 96.0, 100, 100);

        var itemA = new DesktopItemViewModel(new DesktopItemState("A", "primary", 0, 0, 0));
        var itemZ = new DesktopItemViewModel(new DesktopItemState("Z", "primary", 10, 2, 1));

        vm.AddItem(itemA);
        vm.AddItem(itemZ);
        vm.CalculateLayout(metrics);

        // Drag A directly to Z's position (10, 2)
        bool moved = vm.ReorderItem(itemA.Id, new GridCell(10, 2), metrics);
        Assert.True(moved);

        Assert.Equal(new GridCell(10, 2), vm.GetCommittedCell(itemA.Id));
        Assert.Equal(new GridCell(0, 0), vm.GetCommittedCell(itemZ.Id));
    }

    [Fact]
    public void CalculateLayout_PreservesDistantItemAndPlacesNewUnplacedItemAtLeftEdge()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();

        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 1200, 600), 96.0, 100, 100);

        var itemZ = new DesktopItemViewModel(new DesktopItemState("Z", "primary", 10, 2, 0));
        var newUnplaced = new DesktopItemViewModel(new DesktopItemState("New", "primary", -1, -1, 1));

        vm.AddItem(itemZ);
        vm.AddItem(newUnplaced);

        vm.CalculateLayout(metrics, forceAutoArrange: false);

        // Distant item Z preserved at (10, 2)
        Assert.Equal(new GridCell(10, 2), vm.GetCommittedCell(itemZ.Id));

        // Unplaced item defaults to the left edge at (0, 0)
        Assert.Equal(new GridCell(0, 0), vm.GetCommittedCell(newUnplaced.Id));
    }

    [Fact]
    public void CalculateLayout_PacksAllItemsWhenForceAutoArrangeIsTrue()
    {
        var vm = CreateTestViewModel();
        vm.ClearItems();

        var metrics = new DesktopGridMetrics("primary", new Rect(0, 0, 1200, 600), 96.0, 100, 100);

        var itemA = new DesktopItemViewModel(new DesktopItemState("A", "primary", 0, 0, 0));
        var itemZ = new DesktopItemViewModel(new DesktopItemState("Z", "primary", 10, 2, 1));

        vm.AddItem(itemA);
        vm.AddItem(itemZ);

        // Explicit forceAutoArrange = true packs all items from (0, 0) column-major
        vm.CalculateLayout(metrics, forceAutoArrange: true);

        Assert.Equal(new GridCell(0, 0), vm.GetCommittedCell(itemA.Id));
        Assert.Equal(new GridCell(0, 1), vm.GetCommittedCell(itemZ.Id));
    }

    [Fact]
    public void SaveLayout_And_LoadDesktopItems_PersistsItemCoordinates()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"daisy_test_layout_{Guid.NewGuid():N}.json");
        var store = new DesktopLayoutStore(tempFile);

        var config = new DesktopLayoutConfig();
        config.ItemSortOrders["test_item"] = 5;
        config.ItemColumns["test_item"] = 10;
        config.ItemRows["test_item"] = 2;
        store.Save(config);

        var loaded = store.Load();
        Assert.Equal(5, loaded.ItemSortOrders["test_item"]);
        Assert.Equal(10, loaded.ItemColumns["test_item"]);
        Assert.Equal(2, loaded.ItemRows["test_item"]);

        if (File.Exists(tempFile))
        {
            File.Delete(tempFile);
        }
    }
}



