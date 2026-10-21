using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input.Platform;
using DaisyOS.Core.Desktop;
using DaisyOS.Shell.Services.Desktop;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.ViewModels;

public enum DesktopSortMode
{
    Name,
    DateModified,
    Size,
    Type,
    AutoArrange
}

public class DesktopItemViewModel : INotifyPropertyChanged
{
    public const string TrashItemId = "daisyos://trash";
    public const string WidgetCardItemId = "daisyos://widget-card";

    public DesktopItemId Id { get; }
    public DesktopItemState State { get; }

    private int _columnSpan = 1;
    public int ColumnSpan
    {
        get => _columnSpan;
        set
        {
            if (SetField(ref _columnSpan, value))
            {
                OnPropertyChanged(nameof(IsSizeSmall));
                OnPropertyChanged(nameof(IsSizeMedium));
                OnPropertyChanged(nameof(IsSizeLarge));
                OnPropertyChanged(nameof(IsSizeExtraLarge));
            }
        }
    }

    private int _rowSpan = 1;
    public int RowSpan
    {
        get => _rowSpan;
        set
        {
            if (SetField(ref _rowSpan, value))
            {
                OnPropertyChanged(nameof(IsSizeSmall));
                OnPropertyChanged(nameof(IsSizeMedium));
                OnPropertyChanged(nameof(IsSizeLarge));
                OnPropertyChanged(nameof(IsSizeExtraLarge));
            }
        }
    }

    public bool IsSizeSmall => ColumnSpan == 1 && RowSpan == 1;
    public bool IsSizeMedium => ColumnSpan == 3 && RowSpan == 2;
    public bool IsSizeLarge => ColumnSpan >= 4 && RowSpan == 2;
    public bool IsSizeExtraLarge => IsMediaWidget ? RowSpan > 2 : ColumnSpan == 5 && RowSpan == 4;
    public int ExtraLargeRowSpan => IsMediaWidget ? DesktopWidgets.MediaWidget.ExtraLargeRows : 4;
    public string ExtraLargeSizeIcon => IsMediaWidget ? "widget_extralarge2" : "widget_extralarge";

    private bool _supportsSmallSize = true;
    public bool SupportsSmallSize
    {
        get => _supportsSmallSize;
        set => SetField(ref _supportsSmallSize, value);
    }

    private bool _supportsMediumSize = true;
    public bool SupportsMediumSize
    {
        get => _supportsMediumSize;
        set => SetField(ref _supportsMediumSize, value);
    }

    private bool _supportsLargeSize = true;
    public bool SupportsLargeSize
    {
        get => _supportsLargeSize;
        set => SetField(ref _supportsLargeSize, value);
    }

    private bool _supportsExtraLargeSize;
    public bool SupportsExtraLargeSize
    {
        get => _supportsExtraLargeSize;
        set => SetField(ref _supportsExtraLargeSize, value);
    }

    private bool _isWidgetCard;
    public bool IsWidgetCard
    {
        get => _isWidgetCard;
        set => SetField(ref _isWidgetCard, value);
    }

    private object? _widgetContent;
    public object? WidgetContent
    {
        get => _widgetContent;
        set
        {
            if (SetField(ref _widgetContent, value))
            {
                OnPropertyChanged(nameof(IsMediaWidget));
                OnPropertyChanged(nameof(IsSizeExtraLarge));
                OnPropertyChanged(nameof(ExtraLargeRowSpan));
                OnPropertyChanged(nameof(ExtraLargeSizeIcon));
                OnPropertyChanged(nameof(IsImageWidget));
            }
        }
    }

    public bool IsMediaWidget => WidgetContent is DaisyOS.Shell.DesktopWidgets.MediaWidget;
    public bool IsImageWidget => WidgetContent is DaisyOS.Shell.DesktopWidgets.ImageWidget;

    public void SetGridSize(int columnSpan, int rowSpan)
    {
        ColumnSpan = Math.Max(1, columnSpan);
        RowSpan = Math.Max(1, rowSpan);
    }

    private string _label = string.Empty;
    public string Label
    {
        get => _label;
        set => SetField(ref _label, value);
    }

    private string _originalLabel = string.Empty;
    public string OriginalLabel
    {
        get => _originalLabel;
        set => SetField(ref _originalLabel, value);
    }

    private string _iconPath = string.Empty;
    public string IconPath
    {
        get => _iconPath;
        set
        {
            if (SetField(ref _iconPath, value))
            {
                IconBitmap = DesktopItemLoader.LoadBitmapSafe(value);
            }
        }
    }

    private Avalonia.Media.IImage? _iconBitmap;
    public Avalonia.Media.IImage? IconBitmap
    {
        get => _iconBitmap;
        set => SetField(ref _iconBitmap, value);
    }

    private double _x;
    public double X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    private double _y;
    public double Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }

    private double _dragX;
    public double DragX
    {
        get => _dragX;
        set => SetField(ref _dragX, value);
    }

    private double _dragY;
    public double DragY
    {
        get => _dragY;
        set => SetField(ref _dragY, value);
    }

    private bool _isEditMode;
    public bool IsEditMode
    {
        get => _isEditMode;
        set => SetField(ref _isEditMode, value);
    }

    private bool _isRenaming;
    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetField(ref _isRenaming, value);
    }

    private bool _isDragging;
    public bool IsDragging
    {
        get => _isDragging;
        set => SetField(ref _isDragging, value);
    }

    private bool _isCut;
    public bool IsCut
    {
        get => _isCut;
        set => SetField(ref _isCut, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    private bool _isHiddenPlaceholder;
    public bool IsHiddenPlaceholder
    {
        get => _isHiddenPlaceholder;
        set => SetField(ref _isHiddenPlaceholder, value);
    }

    public bool IsTrash => Id.Value == TrashItemId;
    public bool IsDesktopEntry => State.Id.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase);
    public bool IsDirectory => !IsTrash && !IsWidgetCard && Directory.Exists(State.Id);
    public bool IsImage => !IsTrash && !IsWidgetCard && (Path.GetExtension(State.Id).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" or ".svg");
    public bool IsRegularFile => !IsTrash && !IsWidgetCard && !IsDesktopEntry && !IsDirectory && File.Exists(State.Id);
    public bool IsShareable => !IsTrash && !IsWidgetCard && (File.Exists(State.Id) || Directory.Exists(State.Id));
    public bool CanBeRenamed => !IsTrash && !IsWidgetCard && (File.Exists(State.Id) || Directory.Exists(State.Id));
    public bool CanBeTrashed => !IsTrash && !IsWidgetCard && (File.Exists(State.Id) || Directory.Exists(State.Id));
    public bool CanBeCompressed => !IsTrash && !IsWidgetCard && (File.Exists(State.Id) || Directory.Exists(State.Id));
    public bool CanSetAsWallpaper => IsImage && File.Exists(State.Id);
    public bool CanOpenInTerminal => IsDirectory && Directory.Exists(State.Id);

    public DesktopItemViewModel(DesktopItemState state, DesktopItemId? id = null)
    {
        State = state;
        Id = id ?? (string.IsNullOrEmpty(state.Id) ? DesktopItemId.NewId() : new DesktopItemId(state.Id));
        state.Id = Id.Value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class DesktopViewModel : INotifyPropertyChanged
{
    public ObservableCollection<DesktopItemViewModel> Items { get; } = new();

    private readonly Dictionary<DesktopItemId, DesktopItemViewModel> _itemMap = new();
    private readonly Dictionary<DesktopItemId, GridCell> _committedCells = new();

    private bool _bottomMediaWidgetVisible = true;
    public bool BottomMediaWidgetVisible
    {
        get => _bottomMediaWidgetVisible;
        set
        {
            if (SetField(ref _bottomMediaWidgetVisible, value))
            {
                BottomMediaWidgetVisibilityChanged?.Invoke(this, value);
            }
        }
    }
    public event EventHandler<bool>? BottomMediaWidgetVisibilityChanged;

    public void MoveMediaWidgetToBottomShelf(DesktopItemViewModel? item = null)
    {
        var mediaItems = item != null ? [item] : Items.Where(i => i.IsMediaWidget).ToList();
        foreach (var m in mediaItems)
        {
            RemoveWidget(m, null);
        }
        BottomMediaWidgetVisible = true;
        SaveLayout();
    }

    public void MoveMediaWidgetToDesktop(DesktopGridMetrics? metrics = null, int columnSpan = 3, int rowSpan = 2)
    {
        BottomMediaWidgetVisible = false;
        var existingMedia = Items.FirstOrDefault(i => i.IsMediaWidget);
        if (existingMedia == null)
        {
            AddNewWidget("media", columnSpan, rowSpan, metrics);
        }
        else
        {
            SaveLayout();
        }
    }

    public void RemoveBottomMediaWidget()
    {
        BottomMediaWidgetVisible = false;
        SaveLayout();
    }

    private bool _isEditMode;
    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (SetField(ref _isEditMode, value))
            {
                foreach (var item in Items)
                {
                    item.IsEditMode = value;
                }
            }
        }
    }

    private readonly DesktopLayoutStore _layoutStore;
    private readonly HashSet<string> _removedWidgetIds = new();
    private FileSystemWatcher? _desktopWatcher;

    public event Action? LayoutRequested;

    public DesktopViewModel(DesktopLayoutStore? layoutStore = null)
    {
        _layoutStore = layoutStore ?? new DesktopLayoutStore();
        LoadDesktopItems();
        InitializeDesktopWatcher();
        RefreshTrashIcon();
    }

    private void InitializeDesktopWatcher()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (string.IsNullOrWhiteSpace(desktopPath))
        {
            desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
        }

        try
        {
            Directory.CreateDirectory(desktopPath);
            _desktopWatcher = new FileSystemWatcher(desktopPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };

            _desktopWatcher.Created += OnDesktopEntryCreated;
            _desktopWatcher.Deleted += OnDesktopEntryDeleted;
            _desktopWatcher.Renamed += OnDesktopEntryRenamed;
            _desktopWatcher.Changed += OnDesktopEntryChanged;
        }
        catch (Exception ex)
        {
            global::System.Console.Error.WriteLine($"[Desktop] Could not start FileSystemWatcher on {desktopPath}: {ex.Message}");
        }
    }

    private void OnDesktopEntryCreated(object sender, FileSystemEventArgs e)
    {
        if (Path.GetFileName(e.FullPath).StartsWith('.')) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_itemMap.ContainsKey(new DesktopItemId(e.FullPath)))
            {
                AddDesktopPath(e.FullPath);
                LayoutRequested?.Invoke();
            }
        });
    }

    private void OnDesktopEntryDeleted(object sender, FileSystemEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RemoveDesktopPath(e.FullPath);
            LayoutRequested?.Invoke();
        });
    }

    private void OnDesktopEntryRenamed(object sender, RenamedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RemoveDesktopPath(e.OldFullPath);
            if (!Path.GetFileName(e.FullPath).StartsWith('.'))
            {
                AddDesktopPath(e.FullPath);
            }
            LayoutRequested?.Invoke();
        });
    }

    private void OnDesktopEntryChanged(object sender, FileSystemEventArgs e)
    {
        if (Path.GetFileName(e.FullPath).StartsWith('.')) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_itemMap.TryGetValue(new DesktopItemId(e.FullPath), out var item))
            {
                if (IsPreviewableImage(e.FullPath))
                {
                    item.IconBitmap = DesktopItemLoader.LoadBitmapSafe(e.FullPath);
                }
            }
        });
    }

    public void RemoveDesktopPath(string path, DesktopGridMetrics? metrics = null)
    {
        var id = new DesktopItemId(path);
        if (_itemMap.TryGetValue(id, out var item))
        {
            _itemMap.Remove(id);
            _committedCells.Remove(id);
            Items.Remove(item);
            if (metrics != null)
            {
                CalculateLayout(metrics);
            }
        }
    }

    private void LoadDesktopItems()
    {
        var layoutConfig = _layoutStore.Load();

        int trashCol = layoutConfig.ItemColumns.TryGetValue(DesktopItemViewModel.TrashItemId, out var tc) ? tc : 0;
        int trashRow = layoutConfig.ItemRows.TryGetValue(DesktopItemViewModel.TrashItemId, out var tr) ? tr : 0;
        int trashSort = layoutConfig.ItemSortOrders.TryGetValue(DesktopItemViewModel.TrashItemId, out var ts) ? ts : 0;

        AddItem(new DesktopItemViewModel(new DesktopItemState(DesktopItemViewModel.TrashItemId, "primary", trashCol, trashRow, trashSort))
        {
            Label = Loc.Get("Trash"),
            IconPath = DesktopItemLoader.GetAppIconPath("trash_item", DesktopItemLoader.IsCurrentThemeLight())
        });

        foreach (var w in layoutConfig.Widgets.Where(w => w.IsRemoved))
        {
            _removedWidgetIds.Add(w.Id);
        }

        var activeWidgets = layoutConfig.Widgets.Where(w => !w.IsRemoved).ToList();
        bool hasDesktopMedia = activeWidgets.Any(w => w.Id.Contains("media", StringComparison.OrdinalIgnoreCase) || w.Id.Contains("music", StringComparison.OrdinalIgnoreCase));
        if (hasDesktopMedia)
        {
            BottomMediaWidgetVisible = false;
        }
        else
        {
            BottomMediaWidgetVisible = layoutConfig.BottomMediaWidgetVisible;
        }

        if (activeWidgets.Count > 0 || _removedWidgetIds.Count > 0)
        {
            // Restore all saved widgets from layoutConfig
            foreach (var widgetConfig in activeWidgets)
            {
                bool isAnalogClock = widgetConfig.Id.Contains("analog", StringComparison.OrdinalIgnoreCase);
                bool isDigitalClock = widgetConfig.Id.Contains("digital", StringComparison.OrdinalIgnoreCase);
                bool isGlowClock = widgetConfig.Id.Contains("glow", StringComparison.OrdinalIgnoreCase);
                bool isClock = !isAnalogClock && !isDigitalClock && !isGlowClock && widgetConfig.Id.Contains("clock", StringComparison.OrdinalIgnoreCase);
                bool isMedia = widgetConfig.Id.Contains("media", StringComparison.OrdinalIgnoreCase) || widgetConfig.Id.Contains("music", StringComparison.OrdinalIgnoreCase);
                bool isImage = widgetConfig.Id.Contains("image", StringComparison.OrdinalIgnoreCase) || widgetConfig.Id.Contains("photo", StringComparison.OrdinalIgnoreCase);
                string label = isAnalogClock ? Loc.Get("Analog Clock") : isGlowClock ? Loc.Get("Glow Clock") : isDigitalClock ? Loc.Get("Digital Clock") : isClock ? Loc.Get("Sweep Clock") : isMedia ? Loc.Get("Media") : isImage ? Loc.Get("Photos") : Loc.Get("Calendar");
                int colSpan = Math.Max(1, widgetConfig.ColumnSpan);
                int rowSpan = Math.Max(1, widgetConfig.RowSpan);
                if (!isAnalogClock && !isDigitalClock && !isGlowClock && !isClock && !isMedia && !isImage)
                {
                    colSpan = Math.Max(3, colSpan);
                    rowSpan = Math.Max(2, rowSpan);
                }

                object content;
                if (isAnalogClock)
                {
                    var analogClock = new DesktopWidgets.AnalogClockWidget();
                    analogClock.SetGridSize(colSpan, rowSpan);
                    content = analogClock;
                }
                else if (isGlowClock)
                {
                    var glowClock = new DesktopWidgets.GlowClockWidget();
                    glowClock.SetGridSize(colSpan, rowSpan);
                    content = glowClock;
                }
                else if (isDigitalClock)
                {
                    var digitalClock = new DesktopWidgets.DigitalClockWidget();
                    digitalClock.SetGridSize(colSpan, rowSpan);
                    content = digitalClock;
                }
                else if (isClock)
                {
                    var clock = new DesktopWidgets.ClockWidget();
                    clock.SetGridSize(colSpan, rowSpan);
                    content = clock;
                }
                else if (isMedia)
                {
                    var media = new DesktopWidgets.MediaWidget();
                    media.SetGridSize(colSpan, rowSpan);
                    content = media;
                }
                else if (isImage)
                {
                    var imageWidget = new DesktopWidgets.ImageWidget();
                    imageWidget.SetGridSize(colSpan, rowSpan);
                    if (widgetConfig.ImagePaths != null && widgetConfig.ImagePaths.Count > 0)
                    {
                        imageWidget.SetImages(widgetConfig.ImagePaths);
                    }
                    imageWidget.ImagesChanged += () => SaveLayout();
                    content = imageWidget;
                }
                else
                {
                    var cal = new DesktopWidgets.CalendarWidget();
                    cal.SetGridSize(colSpan, rowSpan);
                    content = cal;
                }

                var item = AddWidget(
                    widgetConfig.Id,
                    columnSpan: colSpan,
                    rowSpan: rowSpan,
                    content: content,
                    label: label,
                    initialCol: widgetConfig.Column,
                    initialRow: widgetConfig.Row);

                if (isImage)
                {
                    item.SupportsSmallSize = false;
                    item.SupportsMediumSize = true;
                    item.SupportsLargeSize = true;
                    item.SupportsExtraLargeSize = true;
                }
            }
        }
        else
        {
            // Initial defaults for first-time session (Calendar 4x2, Clock 3x2)
            var calWidget = new DesktopWidgets.CalendarWidget();
            calWidget.SetGridSize(4, 2);
            AddWidget(DesktopItemViewModel.WidgetCardItemId, columnSpan: 4, rowSpan: 2, content: calWidget, label: "Calendar", initialCol: 999, initialRow: 0);

            var clockWidget = new DesktopWidgets.ClockWidget();
            clockWidget.SetGridSize(3, 2);
            AddWidget("desktop.widget.clock", columnSpan: 3, rowSpan: 2, content: clockWidget, label: "Sweep Clock", initialCol: 999, initialRow: 2);
        }

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        {
            var index = 1;
            foreach (var path in Directory.EnumerateFileSystemEntries(desktopPath)
                         .Where(path => !Path.GetFileName(path).StartsWith('.'))
                         .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
            {
                int sort = layoutConfig.ItemSortOrders.TryGetValue(path, out var savedSort) ? savedSort : index++;
                int col = layoutConfig.ItemColumns.TryGetValue(path, out var savedCol) ? savedCol : -1;
                int row = layoutConfig.ItemRows.TryGetValue(path, out var savedRow) ? savedRow : -1;
                AddDesktopPath(path, sort, col, row);
            }
        }
    }

    public void UpdateTheme(bool isLight)
    {
        var trashItem = Items.FirstOrDefault(i => i.IsTrash);
        if (trashItem != null)
        {
            RefreshTrashIcon(isLight);
        }

        foreach (var item in Items.Where(i => !i.IsTrash && !i.IsWidgetCard && !i.IsImage))
        {
            if (!item.IsDesktopEntry)
                item.IconPath = DesktopItemLoader.ResolveFileIconPath(item.State.Id);
        }
    }

    public void AddDesktopPath(string path, int? sortOrder = null, int column = -1, int row = -1)
    {
        if (string.IsNullOrWhiteSpace(path)
            || Path.GetFileName(path).StartsWith('.')
            || _itemMap.ContainsKey(new DesktopItemId(path)))
        {
            return;
        }

        var isDesktopEntry = path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase);
        var label = Path.GetFileName(path);
        var iconPath = DesktopItemLoader.ResolveFileIconPath(path);
        if (isDesktopEntry)
        {
            var parsed = DesktopItemLoader.ParseDesktopFile(path);
            label = parsed.name;
            iconPath = string.IsNullOrWhiteSpace(parsed.iconPath) ? iconPath : parsed.iconPath;
        }
        else if (IsPreviewableImage(path))
        {
            iconPath = path;
        }

        AddItem(new DesktopItemViewModel(new DesktopItemState(path, "primary", column, row, sortOrder ?? Items.Count))
        {
            Label = label,
            IconPath = iconPath
        });
    }

    public DesktopItemViewModel AddWidget(string id, int columnSpan = 4, int rowSpan = 2, object? content = null, string label = "Calendar", int initialCol = 999, int initialRow = 0)
    {
        content ??= new DaisyOS.Shell.DesktopWidgets.CalendarWidget();

        var item = new DesktopItemViewModel(new DesktopItemState(id, "primary", initialCol, initialRow, 999, columnSpan, rowSpan))
        {
            Label = label,
            ColumnSpan = Math.Max(1, columnSpan),
            RowSpan = Math.Max(1, rowSpan),
            IsWidgetCard = true,
            WidgetContent = content,
            SupportsSmallSize = false,
            SupportsExtraLargeSize = content is DesktopWidgets.MediaWidget
        };
        AddItem(item);
        return item;
    }
    public DesktopItemViewModel AddNewWidget(string widgetType, int columnSpan, int rowSpan, DesktopGridMetrics? metrics = null)
    {
        bool isAnalogClock = string.Equals(widgetType, "clock-analog", StringComparison.OrdinalIgnoreCase)
            || string.Equals(widgetType, "analog-clock", StringComparison.OrdinalIgnoreCase);
        bool isDigitalClock = string.Equals(widgetType, "clock-digital", StringComparison.OrdinalIgnoreCase)
            || string.Equals(widgetType, "digital-clock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(widgetType, "digitalclock", StringComparison.OrdinalIgnoreCase);
        bool isGlowClock = string.Equals(widgetType, "clock-glow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(widgetType, "glow-clock", StringComparison.OrdinalIgnoreCase);
        bool isClock = !isAnalogClock && !isDigitalClock && !isGlowClock && (string.Equals(widgetType, "clock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(widgetType, "clock-sweep", StringComparison.OrdinalIgnoreCase)
            || string.Equals(widgetType, "sweep-clock", StringComparison.OrdinalIgnoreCase));
        bool isMedia = string.Equals(widgetType, "media", StringComparison.OrdinalIgnoreCase) || string.Equals(widgetType, "music", StringComparison.OrdinalIgnoreCase);
        bool isImage = string.Equals(widgetType, "image", StringComparison.OrdinalIgnoreCase)
            || string.Equals(widgetType, "images", StringComparison.OrdinalIgnoreCase)
            || string.Equals(widgetType, "photo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(widgetType, "photos", StringComparison.OrdinalIgnoreCase);

        if (!isAnalogClock && !isDigitalClock && !isGlowClock && !isClock && !isMedia && !isImage)
        {
            columnSpan = Math.Max(3, columnSpan);
            rowSpan = Math.Max(2, rowSpan);
        }

        if (isMedia)
        {
            var existingMedia = Items.Where(i => i.IsMediaWidget).ToList();
            foreach (var m in existingMedia)
            {
                RemoveWidget(m, null);
            }
            BottomMediaWidgetVisible = false;
        }

        string uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        string id = isAnalogClock ? $"desktop.widget.clock.analog.{uniqueSuffix}" : isGlowClock ? $"desktop.widget.clock.glow.{uniqueSuffix}" : isDigitalClock ? $"desktop.widget.clock.digital.{uniqueSuffix}" : isClock ? $"desktop.widget.clock.sweep.{uniqueSuffix}" : isMedia ? $"desktop.widget.media.{uniqueSuffix}" : isImage ? $"desktop.widget.image.{uniqueSuffix}" : $"desktop.widget.calendar.{uniqueSuffix}";
        string label = isAnalogClock ? Loc.Get("Analog Clock") : isGlowClock ? Loc.Get("Glow Clock") : isDigitalClock ? Loc.Get("Digital Clock") : isClock ? Loc.Get("Sweep Clock") : isMedia ? Loc.Get("Media") : isImage ? Loc.Get("Photos") : Loc.Get("Calendar");

        object content;
        if (isAnalogClock)
        {
            var analogClock = new DesktopWidgets.AnalogClockWidget();
            analogClock.SetGridSize(columnSpan, rowSpan);
            content = analogClock;
        }
        else if (isGlowClock)
        {
            var glowClock = new DesktopWidgets.GlowClockWidget();
            glowClock.SetGridSize(columnSpan, rowSpan);
            content = glowClock;
        }
        else if (isDigitalClock)
        {
            var digitalClock = new DesktopWidgets.DigitalClockWidget();
            digitalClock.SetGridSize(columnSpan, rowSpan);
            content = digitalClock;
        }
        else if (isClock)
        {
            var clock = new DesktopWidgets.ClockWidget();
            clock.SetGridSize(columnSpan, rowSpan);
            content = clock;
        }
        else if (isMedia)
        {
            var media = new DesktopWidgets.MediaWidget();
            media.SetGridSize(columnSpan, rowSpan);
            content = media;
        }
        else if (isImage)
        {
            var imageWidget = new DesktopWidgets.ImageWidget();
            imageWidget.SetGridSize(columnSpan, rowSpan);
            imageWidget.ImagesChanged += () => SaveLayout();
            content = imageWidget;
        }
        else
        {
            var cal = new DesktopWidgets.CalendarWidget();
            cal.SetGridSize(columnSpan, rowSpan);
            content = cal;
        }

        var item = AddWidget(id, columnSpan: columnSpan, rowSpan: rowSpan, content: content, label: label, initialCol: 999, initialRow: 999);
        if (isImage)
        {
            item.SupportsSmallSize = false;
            item.SupportsMediumSize = true;
            item.SupportsLargeSize = true;
            item.SupportsExtraLargeSize = true;
        }
        _removedWidgetIds.Remove(id);

        if (metrics != null)
        {
            CalculateLayout(metrics);
        }

        SaveLayout();

        return item;
    }

    private static bool IsPreviewableImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" or ".svg";

    public void AddItem(DesktopItemViewModel item)
    {
        if (_itemMap.ContainsKey(item.Id)) return;
        item.IsEditMode = IsEditMode;
        _itemMap[item.Id] = item;
        Items.Add(item);
        var cell = new GridCell(item.State.Column, item.State.Row);
        _committedCells[item.Id] = cell;
    }

    public DesktopItemViewModel? GetItem(DesktopItemId id)
    {
        return _itemMap.TryGetValue(id, out var item) ? item : null;
    }

    public void SelectItem(DesktopItemViewModel? selectedItem)
    {
        foreach (var item in Items)
        {
            item.IsSelected = ReferenceEquals(item, selectedItem);
        }
    }

    public void ClearSelection() => SelectItem(null);

    public GridCell GetCommittedCell(DesktopItemId id)
    {
        return _committedCells.TryGetValue(id, out var cell) ? cell : new GridCell(0, 0);
    }

    public void ClearItems()
    {
        Items.Clear();
        _itemMap.Clear();
        _committedCells.Clear();
    }

    public int GetCommittedIndex(DesktopItemId id)
    {
        var keys = _committedCells.Keys.ToList();
        return keys.IndexOf(id);
    }

    // ============================================================
    // FILE CREATION & EDITING OPERATIONS
    // ============================================================

    public DesktopItemViewModel? CreateNewFolder(DesktopGridMetrics? metrics = null)
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        Directory.CreateDirectory(desktopPath);

        string folderName = "New Folder";
        string targetPath = Path.Combine(desktopPath, folderName);
        int counter = 2;
        while (Directory.Exists(targetPath) || File.Exists(targetPath))
        {
            folderName = $"New Folder ({counter++})";
            targetPath = Path.Combine(desktopPath, folderName);
        }

        try
        {
            Directory.CreateDirectory(targetPath);
            AddDesktopPath(targetPath);
            var item = GetItem(new DesktopItemId(targetPath));
            if (item != null)
            {
                item.OriginalLabel = item.Label;
                item.IsRenaming = true;
                SelectItem(item);
                if (metrics != null) CalculateLayout(metrics);
            }
            return item;
        }
        catch (Exception ex)
        {
            global::System.Console.Error.WriteLine($"[Desktop] Failed to create folder: {ex.Message}");
            return null;
        }
    }

    public DesktopItemViewModel? CreateNewTextFile(DesktopGridMetrics? metrics = null)
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        Directory.CreateDirectory(desktopPath);

        string fileName = "New Document.txt";
        string targetPath = Path.Combine(desktopPath, fileName);
        int counter = 2;
        while (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            fileName = $"New Document ({counter++}).txt";
            targetPath = Path.Combine(desktopPath, fileName);
        }

        try
        {
            File.WriteAllText(targetPath, string.Empty);
            AddDesktopPath(targetPath);
            var item = GetItem(new DesktopItemId(targetPath));
            if (item != null)
            {
                item.OriginalLabel = item.Label;
                item.IsRenaming = true;
                SelectItem(item);
                if (metrics != null) CalculateLayout(metrics);
            }
            return item;
        }
        catch (Exception ex)
        {
            global::System.Console.Error.WriteLine($"[Desktop] Failed to create text file: {ex.Message}");
            return null;
        }
    }

    public void StartRenaming(DesktopItemViewModel item)
    {
        if (!item.CanBeRenamed) return;
        item.OriginalLabel = item.Label;
        item.IsRenaming = true;
        SelectItem(item);
    }

    public void CommitRename(DesktopItemViewModel item, string newName)
    {
        if (!item.IsRenaming) return;
        item.IsRenaming = false;

        newName = newName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newName) || newName == item.OriginalLabel)
        {
            item.Label = item.OriginalLabel;
            return;
        }

        // Reject names that could escape the desktop directory.
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || newName.Contains("..", StringComparison.Ordinal)
            || newName.Contains(Path.DirectorySeparatorChar)
            || newName.Contains(Path.AltDirectorySeparatorChar))
        {
            item.Label = item.OriginalLabel;
            return;
        }

        var oldPath = item.State.Id;
        if (!File.Exists(oldPath) && !Directory.Exists(oldPath))
        {
            item.Label = newName;
            return;
        }

        try
        {
            var parentDir = Path.GetDirectoryName(oldPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var newPath = Path.Combine(parentDir, newName);

            // Final guard: ensure the resolved path stays within the parent directory.
            var resolvedParent = Path.GetFullPath(parentDir);
            var resolvedNew = Path.GetFullPath(newPath);
            if (!resolvedNew.StartsWith(resolvedParent + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && resolvedNew != resolvedParent)
            {
                item.Label = item.OriginalLabel;
                return;
            }

            if (File.Exists(oldPath))
            {
                File.Move(oldPath, newPath);
            }
            else if (Directory.Exists(oldPath))
            {
                Directory.Move(oldPath, newPath);
            }

            // Update item mappings
            var oldId = item.Id;
            var newId = new DesktopItemId(newPath);

            _itemMap.Remove(oldId);
            if (_committedCells.TryGetValue(oldId, out var cell))
            {
                _committedCells.Remove(oldId);
                _committedCells[newId] = cell;
            }

            item.State.Id = newPath;
            _itemMap[newId] = item;

            item.Label = newName;
            item.IconPath = IsPreviewableImage(newPath) ? newPath :
                item.IsDesktopEntry ? DesktopItemLoader.ParseDesktopFile(newPath).iconPath :
                DesktopItemLoader.ResolveFileIconPath(newPath);
        }
        catch (Exception ex)
        {
            global::System.Console.Error.WriteLine($"[Desktop] Failed to rename: {ex.Message}");
            item.Label = item.OriginalLabel;
        }
    }

    public void CancelRename(DesktopItemViewModel item)
    {
        if (!item.IsRenaming) return;
        item.Label = string.IsNullOrEmpty(item.OriginalLabel) ? item.Label : item.OriginalLabel;
        item.IsRenaming = false;
    }

    public async Task<bool> MoveItemToTrash(DesktopItemViewModel item, DesktopGridMetrics? metrics = null)
    {
        if (!item.CanBeTrashed) return false;
        if (!await DaisyOS.Shell.Apps.Files.Services.FileSystemService.MoveToTrashAsync(item.State.Id))
        {
            global::System.Console.Error.WriteLine($"[Desktop] Failed to move to trash: {item.State.Id}");
            return false;
        }
        Items.Remove(item);
        _itemMap.Remove(item.Id);
        _committedCells.Remove(item.Id);
        RefreshTrashIcon();
        SaveLayout();
        LayoutRequested?.Invoke();
        return true;
    }

    public DesktopItemViewModel? GetTrashAtPoint(Avalonia.Point point, DesktopGridMetrics? metrics)
    {
        if (metrics == null) return null;
        var trash = Items.FirstOrDefault(item => item.IsTrash);
        if (trash == null) return null;
        var origin = metrics.GetCellOrigin(GetCommittedCell(trash.Id));
        return new Avalonia.Rect(origin.X, origin.Y, metrics.CellWidth, metrics.CellHeight).Contains(point) ? trash : null;
    }

    public void RefreshTrashIcon(bool? isLight = null, string? trashFilesPath = null)
    {
        var trash = Items.FirstOrDefault(item => item.IsTrash);
        if (trash == null) return;
        var files = trashFilesPath ?? DaisyOS.Shell.Apps.Files.UserTrashPaths.Files;
        try
        {
            var populated = Directory.Exists(files) && Directory.EnumerateFileSystemEntries(files).Any();
            trash.IconPath = DesktopItemLoader.GetAppIconPath(populated ? "trash_item" : "trash", isLight ?? DesktopItemLoader.IsCurrentThemeLight());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private Task<bool>? _emptyTrashTask;

    public Task<bool> EmptyTrashAsync()
    {
        if (_emptyTrashTask is { IsCompleted: false }) return _emptyTrashTask;
        return _emptyTrashTask = EmptyTrashCoreAsync();
    }

    private async Task<bool> EmptyTrashCoreAsync()
    {
        var result = await Apps.Files.Services.FileSystemService.EmptyTrashAsync();
        RefreshTrashIcon();
        return result;
    }

    public void RemoveWidget(DesktopItemViewModel item, DesktopGridMetrics? metrics = null)
    {
        if (!item.IsWidgetCard) return;

        _removedWidgetIds.Add(item.Id.Value);
        Items.Remove(item);
        _itemMap.Remove(item.Id);
        _committedCells.Remove(item.Id);

        if (metrics != null) CalculateLayout(metrics);
        SaveLayout();
    }

    public void ResizeWidget(DesktopItemViewModel item, int columnSpan, int rowSpan, DesktopGridMetrics? metrics = null)
    {
        if (!item.IsWidgetCard) return;

        columnSpan = Math.Max(1, columnSpan);
        rowSpan = Math.Max(1, rowSpan);
        if (item.WidgetContent is DesktopWidgets.CalendarWidget)
        {
            columnSpan = Math.Max(3, columnSpan);
            rowSpan = Math.Max(2, rowSpan);
        }

        if (metrics is not null)
        {
            // Keep the widget anchored where it is when possible. If growing
            // would cross an output edge, shift only the overflowing axis
            // inward before the normal occupancy pass resolves collisions.
            columnSpan = Math.Min(columnSpan, metrics.ColumnCount);
            rowSpan = Math.Min(rowSpan, metrics.RowCount);
            var clamped = ClampWidgetOrigin(GetCommittedCell(item.Id), columnSpan, rowSpan, metrics);
            item.State.Column = clamped.Column;
            item.State.Row = clamped.Row;
        }

        item.SetGridSize(columnSpan, rowSpan);
        item.State.ColumnSpan = columnSpan;
        item.State.RowSpan = rowSpan;

        if (item.WidgetContent is DesktopWidgets.CalendarWidget calendar)
        {
            calendar.SetGridSize(columnSpan, rowSpan);
        }
        else if (item.WidgetContent is DesktopWidgets.ClockWidget clock)
        {
            clock.SetGridSize(columnSpan, rowSpan);
        }
        else if (item.WidgetContent is DesktopWidgets.AnalogClockWidget analogClock)
        {
            analogClock.SetGridSize(columnSpan, rowSpan);
        }
        else if (item.WidgetContent is DesktopWidgets.GlowClockWidget glowClock)
        {
            glowClock.SetGridSize(columnSpan, rowSpan);
        }
        else if (item.WidgetContent is DesktopWidgets.DigitalClockWidget digitalClock)
        {
            digitalClock.SetGridSize(columnSpan, rowSpan);
        }
        else if (item.WidgetContent is DesktopWidgets.MediaWidget media)
        {
            media.SetGridSize(columnSpan, rowSpan);
        }
        else if (item.WidgetContent is DesktopWidgets.ImageWidget imageWidget)
        {
            imageWidget.SetGridSize(columnSpan, rowSpan);
        }

        if (metrics is not null)
        {
            CalculateLayout(metrics);
        }

        SaveLayout();
    }

    public static GridCell ClampWidgetOrigin(
        GridCell current, int columnSpan, int rowSpan, DesktopGridMetrics metrics)
    {
        columnSpan = Math.Clamp(columnSpan, 1, metrics.ColumnCount);
        rowSpan = Math.Clamp(rowSpan, 1, metrics.RowCount);
        return new GridCell(
            Math.Clamp(current.Column, 0, metrics.ColumnCount - columnSpan),
            Math.Clamp(current.Row, 0, metrics.RowCount - rowSpan));
    }

    public void SetAsWallpaper(DesktopItemViewModel item)
    {
        if (!item.CanSetAsWallpaper) return;
        _ = (Application.Current as App)?.SetWallpaperAsync(item.State.Id);
    }

    public void CompressItem(DesktopItemViewModel item, DesktopGridMetrics? metrics = null)
    {
        if (!item.CanBeCompressed) return;

        var sourcePath = item.State.Id;
        var parentDir = Path.GetDirectoryName(sourcePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var zipPath = Path.Combine(parentDir, $"{baseName}.zip");
        int counter = 2;
        while (File.Exists(zipPath) || Directory.Exists(zipPath))
        {
            zipPath = Path.Combine(parentDir, $"{baseName} ({counter++}).zip");
        }

        try
        {
            if (Directory.Exists(sourcePath))
            {
                ZipFile.CreateFromDirectory(sourcePath, zipPath);
            }
            else if (File.Exists(sourcePath))
            {
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                archive.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath));
            }

            AddDesktopPath(zipPath);
            if (metrics != null) CalculateLayout(metrics);
        }
        catch (Exception ex)
        {
            global::System.Console.Error.WriteLine($"[Desktop] Failed to compress: {ex.Message}");
        }
    }

    public void CopyPathToClipboard(DesktopItemViewModel item, IClipboard? clipboard = null)
    {
        if (clipboard is not null) _ = clipboard.SetTextAsync(item.State.Id);
    }

    public async Task CopyItemToClipboardAsync(DesktopItemViewModel item, IClipboard? clipboard = null)
    {
        if (!File.Exists(item.State.Id) && !Directory.Exists(item.State.Id)) return;
        ClearCutState();
        await DaisyOS.Shell.Apps.Files.Services.ClipboardService.SetFilesAsync(new[] { item.State.Id }, DaisyOS.Shell.Apps.Files.Services.ClipboardOperation.Copy, clipboard);
    }

    public async Task CutItemToClipboardAsync(DesktopItemViewModel item, IClipboard? clipboard = null)
    {
        if (!File.Exists(item.State.Id) && !Directory.Exists(item.State.Id)) return;
        ClearCutState();
        item.IsCut = true;
        await DaisyOS.Shell.Apps.Files.Services.ClipboardService.SetFilesAsync(new[] { item.State.Id }, DaisyOS.Shell.Apps.Files.Services.ClipboardOperation.Cut, clipboard);
    }

    private void ClearCutState()
    {
        foreach (var desktopItem in Items) desktopItem.IsCut = false;
    }

    public async Task PasteFromClipboardAsync(IClipboard? clipboard = null, DesktopGridMetrics? metrics = null)
    {
        var (files, op) = await DaisyOS.Shell.Apps.Files.Services.ClipboardService.GetFilesAsync(clipboard);
        if (files == null || files.Count == 0) return;

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        Directory.CreateDirectory(desktopPath);

        bool anyAdded = false;

        foreach (var cleanPath in files)
        {
            if (File.Exists(cleanPath))
            {
                var fileName = Path.GetFileName(cleanPath);
                var destPath = Path.Combine(desktopPath, fileName);
                int counter = 2;
                while (File.Exists(destPath) || Directory.Exists(destPath))
                {
                    var stem = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    destPath = Path.Combine(desktopPath, $"{stem} ({counter++}){ext}");
                }

                if (op == DaisyOS.Shell.Apps.Files.Services.ClipboardOperation.Cut)
                {
                    File.Move(cleanPath, destPath);
                }
                else
                {
                    File.Copy(cleanPath, destPath);
                }
                AddDesktopPath(destPath);
                anyAdded = true;
            }
            else if (Directory.Exists(cleanPath))
            {
                var dirName = Path.GetFileName(cleanPath.TrimEnd(Path.DirectorySeparatorChar));
                var destPath = Path.Combine(desktopPath, dirName);
                int counter = 2;
                while (Directory.Exists(destPath) || File.Exists(destPath))
                {
                    destPath = Path.Combine(desktopPath, $"{dirName} ({counter++})");
                }

                if (op == DaisyOS.Shell.Apps.Files.Services.ClipboardOperation.Cut)
                {
                    Directory.Move(cleanPath, destPath);
                }
                else
                {
                    CopyDirectoryRecursive(cleanPath, destPath);
                }
                AddDesktopPath(destPath);
                anyAdded = true;
            }
        }

        if (op == DaisyOS.Shell.Apps.Files.Services.ClipboardOperation.Cut)
        {
            DaisyOS.Shell.Apps.Files.Services.ClipboardService.ClearInternal();
            ClearCutState();
        }

        if (anyAdded && metrics != null)
        {
            CalculateLayout(metrics);
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)

    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectoryRecursive(subDir, Path.Combine(targetDir, Path.GetFileName(subDir)));
        }
    }

    public void SortItemsBy(DesktopSortMode mode, DesktopGridMetrics? metrics = null)
    {
        var itemsToSort = Items.Where(i => !i.IsTrash && !i.IsWidgetCard).ToList();

        var sorted = mode switch
        {
            DesktopSortMode.Name => itemsToSort.OrderBy(i => i.Label, StringComparer.CurrentCultureIgnoreCase).ToList(),
            DesktopSortMode.DateModified => itemsToSort.OrderByDescending(i => GetModifiedDate(i.State.Id)).ToList(),
            DesktopSortMode.Size => itemsToSort.OrderByDescending(i => GetItemSize(i.State.Id)).ToList(),
            DesktopSortMode.Type => itemsToSort.OrderBy(i => GetItemTypeOrder(i)).ThenBy(i => i.Label).ToList(),
            _ => itemsToSort.OrderBy(i => i.State.SortOrder).ToList()
        };

        // Reassign sort orders
        int order = 1;
        foreach (var item in sorted)
        {
            item.State.SortOrder = order++;
        }

        if (metrics != null)
        {
            CalculateLayout(metrics, forceAutoArrange: true);
        }
    }

    private static DateTime GetModifiedDate(string path)
    {
        if (File.Exists(path)) return File.GetLastWriteTime(path);
        if (Directory.Exists(path)) return Directory.GetLastWriteTime(path);
        return DateTime.MinValue;
    }

    private static long GetItemSize(string path)
    {
        if (File.Exists(path)) return new FileInfo(path).Length;
        return 0;
    }

    private static int GetItemTypeOrder(DesktopItemViewModel item)
    {
        if (item.IsDirectory) return 0;
        if (item.IsDesktopEntry) return 1;
        if (item.IsImage) return 2;
        return 3;
    }

    public void OpenInTerminal(string? directoryPath = null)
    {
        directoryPath ??= Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        TerminalLauncher.OpenTerminal(directoryPath);
    }

    public void OpenInFiles(string? directoryPath = null)
    {
        (Application.Current as App)?.ShowFiles(directoryPath ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
    }

    public void OpenSettings(string? section = null)
    {
        (Application.Current as App)?.ShowSettings(section ?? "desktop");
    }

    public void ShareWithLocalSend(DesktopItemViewModel item)
    {
        (Application.Current as App)?.ShowLocalSend(new[] { item.State.Id });
    }

    public bool ReorderItem(DesktopItemId sourceId, GridCell targetCell, DesktopGridMetrics? metrics)
        => ReorderItem(sourceId, targetCell, targetCell, metrics);

    public bool ReorderItem(DesktopItemId sourceId, GridCell targetCell, GridCell centerCell, DesktopGridMetrics? metrics)
    {
        if (metrics == null || !_itemMap.TryGetValue(sourceId, out var sourceItem))
            return false;

        int maxCol = Math.Max(0, metrics.ColumnCount - sourceItem.ColumnSpan);
        int maxRow = Math.Max(0, metrics.RowCount - sourceItem.RowSpan);
        targetCell = new GridCell(Math.Clamp(targetCell.Column, 0, maxCol), Math.Clamp(targetCell.Row, 0, maxRow));

        if (!metrics.IsValid(targetCell))
            return false;

        // Trash is a fixed drop destination unless it is itself the drag source.
        var fixedTrash = Items.FirstOrDefault(item => item.IsTrash && !item.Id.Equals(sourceId));
        if (fixedTrash != null && DesktopOccupancyMap.ItemsOverlap(targetCell,
            sourceItem.ColumnSpan, sourceItem.RowSpan, GetCommittedCell(fixedTrash.Id), 1, 1))
            return false;

        var currentCell = GetCommittedCell(sourceId);

        if (sourceItem.IsWidgetCard)
        {
            // Check if the center of the dragged widget is inside another widget
            var hoveredWidget = Items.FirstOrDefault(other =>
                other.IsWidgetCard &&
                !other.Id.Equals(sourceId) &&
                centerCell.Column >= other.State.Column &&
                centerCell.Column < other.State.Column + other.ColumnSpan &&
                centerCell.Row >= other.State.Row &&
                centerCell.Row < other.State.Row + other.RowSpan);

            var map = new DesktopOccupancyMap(metrics);
            if (fixedTrash != null) map.Occupy(fixedTrash.Id.Value, GetCommittedCell(fixedTrash.Id));

            if (hoveredWidget != null)
            {
                var widgetBCell = GetCommittedCell(hoveredWidget.Id);
                if (currentCell.Equals(widgetBCell))
                    return false;

                // Source item takes hovered widget's top-left cell (clamped)
                int targetACol = Math.Clamp(widgetBCell.Column, 0, maxCol);
                int targetARow = Math.Clamp(widgetBCell.Row, 0, maxRow);
                var resolvedCellA = new GridCell(targetACol, targetARow);

                // Hovered widget swaps to source item's previous location (currentCell)
                int maxBCol = Math.Max(0, metrics.ColumnCount - hoveredWidget.ColumnSpan);
                int maxBRow = Math.Max(0, metrics.RowCount - hoveredWidget.RowSpan);
                int targetBCol = Math.Clamp(currentCell.Column, 0, maxBCol);
                int targetBRow = Math.Clamp(currentCell.Row, 0, maxBRow);
                var resolvedCellB = new GridCell(targetBCol, targetBRow);

                // If resolvedCellB still overlaps with resolvedCellA (e.g. different widget sizes), shift B away
                if (DesktopOccupancyMap.ItemsOverlap(resolvedCellA, sourceItem.ColumnSpan, sourceItem.RowSpan, resolvedCellB, hoveredWidget.ColumnSpan, hoveredWidget.RowSpan))
                {
                    if (currentCell.Row < widgetBCell.Row)
                    {
                        resolvedCellB = new GridCell(targetBCol, Math.Max(0, resolvedCellA.Row - hoveredWidget.RowSpan));
                    }
                    else if (currentCell.Row > widgetBCell.Row)
                    {
                        resolvedCellB = new GridCell(targetBCol, Math.Min(maxBRow, resolvedCellA.Row + sourceItem.RowSpan));
                    }
                    else if (currentCell.Column < widgetBCell.Column)
                    {
                        resolvedCellB = new GridCell(Math.Max(0, resolvedCellA.Column - hoveredWidget.ColumnSpan), targetBRow);
                    }
                    else
                    {
                        resolvedCellB = new GridCell(Math.Min(maxBCol, resolvedCellA.Column + sourceItem.ColumnSpan), targetBRow);
                    }
                }

                map.Occupy(sourceItem.Id.Value, resolvedCellA, sourceItem.ColumnSpan, sourceItem.RowSpan);
                CommitCell(sourceItem, resolvedCellA, metrics);

                if (map.IsRegionOccupied(resolvedCellB, hoveredWidget.ColumnSpan, hoveredWidget.RowSpan))
                    resolvedCellB = FindNearestFreeRegion(map, metrics, hoveredWidget.ColumnSpan, hoveredWidget.RowSpan, resolvedCellB);
                map.Occupy(hoveredWidget.Id.Value, resolvedCellB, hoveredWidget.ColumnSpan, hoveredWidget.RowSpan);
                CommitCell(hoveredWidget, resolvedCellB, metrics);

                // Preserve any other widgets
                foreach (var other in Items.Where(i => i.IsWidgetCard && !i.Id.Equals(sourceId) && !i.Id.Equals(hoveredWidget.Id)))
                {
                    var c = GetCommittedCell(other.Id);
                    if (!map.IsRegionOccupied(c, other.ColumnSpan, other.RowSpan))
                    {
                        map.Occupy(other.Id.Value, c, other.ColumnSpan, other.RowSpan);
                        CommitCell(other, c, metrics);
                    }
                }
            }
            else
            {
                if (currentCell.Equals(targetCell))
                    return false;

                // Check if targetCell overlaps with any other widget
                bool overlapsOther = Items.Any(other =>
                    other.IsWidgetCard &&
                    !other.Id.Equals(sourceId) &&
                    DesktopOccupancyMap.ItemsOverlap(
                        targetCell, sourceItem.ColumnSpan, sourceItem.RowSpan,
                        GetCommittedCell(other.Id), other.ColumnSpan, other.RowSpan));

                if (overlapsOther)
                    return false;

                map.Occupy(sourceItem.Id.Value, targetCell, sourceItem.ColumnSpan, sourceItem.RowSpan);
                CommitCell(sourceItem, targetCell, metrics);

                // Preserve all other widgets
                foreach (var other in Items.Where(i => i.IsWidgetCard && !i.Id.Equals(sourceId)))
                {
                    var c = GetCommittedCell(other.Id);
                    if (!map.IsRegionOccupied(c, other.ColumnSpan, other.RowSpan))
                    {
                        map.Occupy(other.Id.Value, c, other.ColumnSpan, other.RowSpan);
                        CommitCell(other, c, metrics);
                    }
                }
            }

            // Flow standard items into free cells in order
            var normalItems = Items
                .Where(i => !i.IsWidgetCard && !i.IsTrash)
                .OrderBy(i => i.State.SortOrder)
                .ToList();

            int currentCol = 0;
            int currentRow = 0;
            for (int i = 0; i < normalItems.Count; i++)
            {
                while (currentCol < metrics.ColumnCount && map.IsOccupied(new GridCell(currentCol, currentRow)))
                {
                    currentRow++;
                    if (currentRow >= metrics.RowCount)
                    {
                        currentRow = 0;
                        currentCol++;
                    }
                }

                if (currentCol < metrics.ColumnCount)
                {
                    var cell = new GridCell(currentCol, currentRow);
                    map.Occupy(normalItems[i].Id.Value, cell);
                    CommitCell(normalItems[i], cell, metrics, i);

                    currentRow++;
                    if (currentRow >= metrics.RowCount)
                    {
                        currentRow = 0;
                        currentCol++;
                    }
                }
            }

            return true;
        }
        else
        {
            if (currentCell.Equals(targetCell))
                return false;

            var targetItem = Items.FirstOrDefault(other =>
                !other.Id.Equals(sourceId) &&
                DesktopOccupancyMap.ItemsOverlap(
                    targetCell, sourceItem.ColumnSpan, sourceItem.RowSpan,
                    GetCommittedCell(other.Id), other.ColumnSpan, other.RowSpan));

            if (targetItem == null)
            {
                CommitCell(sourceItem, targetCell, metrics);
                return true;
            }

            if (targetItem.IsWidgetCard)
            {
                return false;
            }

            int sourceSlot = metrics.GetSlotIndex(currentCell);
            int targetSlot = metrics.GetSlotIndex(targetCell);
            int minSlot = Math.Min(sourceSlot, targetSlot);
            int maxSlot = Math.Max(sourceSlot, targetSlot);

            // Find all standard items in the range [minSlot, maxSlot]
            var chainItems = Items
                .Where(i => !i.IsWidgetCard && (sourceItem.IsTrash || !i.IsTrash) &&
                            metrics.GetSlotIndex(GetCommittedCell(i.Id)) >= minSlot &&
                            metrics.GetSlotIndex(GetCommittedCell(i.Id)) <= maxSlot)
                .OrderBy(i => metrics.GetSlotIndex(GetCommittedCell(i.Id)))
                .ToList();

            // Check if the range forms a fully contiguous chain of items
            if (chainItems.Count == maxSlot - minSlot + 1)
            {
                if (sourceSlot < targetSlot)
                {
                    // Dragged forward/down: items between source and target shift back/up by 1 slot
                    for (int i = 1; i < chainItems.Count; i++)
                    {
                        int newSlot = minSlot + i - 1;
                        CommitCell(chainItems[i], metrics.GetCellForSlot(newSlot), metrics, newSlot);
                    }
                }
                else
                {
                    // Dragged backward/up: items between target and source shift forward/down by 1 slot
                    for (int i = 0; i < chainItems.Count - 1; i++)
                    {
                        int newSlot = minSlot + i + 1;
                        CommitCell(chainItems[i], metrics.GetCellForSlot(newSlot), metrics, newSlot);
                    }
                }

                CommitCell(sourceItem, targetCell, metrics, targetSlot);
                return true;
            }
            else
            {
                // Non-contiguous (e.g. gaps between items or isolated item at the edge): direct swap
                CommitCell(targetItem, currentCell, metrics, sourceSlot);
                CommitCell(sourceItem, targetCell, metrics, targetSlot);
                return true;
            }
        }
    }

    private static GridCell FindNearestFreeRegion(
        DesktopOccupancyMap map,
        DesktopGridMetrics metrics,
        int colSpan,
        int rowSpan,
        GridCell origin)
    {
        int maxCol = Math.Max(0, metrics.ColumnCount - colSpan);
        int maxRow = Math.Max(0, metrics.RowCount - rowSpan);

        var queue = new Queue<GridCell>();
        var visited = new HashSet<GridCell>();

        var start = new GridCell(Math.Clamp(origin.Column, 0, maxCol), Math.Clamp(origin.Row, 0, maxRow));
        queue.Enqueue(start);
        visited.Add(start);

        int[] dx = { 0, 1, 0, -1, 1, 1, -1, -1 };
        int[] dy = { 1, 0, -1, 0, 1, -1, 1, -1 };

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            if (cell.Column >= 0 && cell.Column + colSpan <= metrics.ColumnCount &&
                cell.Row >= 0 && cell.Row + rowSpan <= metrics.RowCount &&
                !map.IsRegionOccupied(cell, colSpan, rowSpan))
            {
                return cell;
            }

            for (int i = 0; i < 8; i++)
            {
                int nc = cell.Column + dx[i];
                int nr = cell.Row + dy[i];
                if (nc >= 0 && nc + colSpan <= metrics.ColumnCount &&
                    nr >= 0 && nr + rowSpan <= metrics.RowCount)
                {
                    var next = new GridCell(nc, nr);
                    if (visited.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        return new GridCell(Math.Max(0, metrics.ColumnCount - colSpan), 0);
    }

    public void SaveLayout()
    {
        var config = new DesktopLayoutConfig
        {
            BottomMediaWidgetVisible = BottomMediaWidgetVisible
        };

        foreach (var item in Items.Where(i => i.IsWidgetCard))
        {
            var widgetConfig = new DesktopWidgetConfig
            {
                Id = item.Id.Value,
                Column = item.State.Column,
                Row = item.State.Row,
                ColumnSpan = item.ColumnSpan,
                RowSpan = item.RowSpan,
                IsRemoved = false
            };
            if (item.WidgetContent is DesktopWidgets.ImageWidget imageWidget)
            {
                widgetConfig.ImagePaths = imageWidget.ImagePaths.ToList();
            }
            config.Widgets.Add(widgetConfig);
        }

        foreach (var removedId in _removedWidgetIds)
        {
            config.Widgets.Add(new DesktopWidgetConfig
            {
                Id = removedId,
                IsRemoved = true
            });
        }

        foreach (var item in Items.Where(i => !i.IsWidgetCard && !i.IsTrash))
        {
            config.ItemSortOrders[item.Id.Value] = item.State.SortOrder;
            config.ItemColumns[item.Id.Value] = item.State.Column;
            config.ItemRows[item.Id.Value] = item.State.Row;
        }

        var trash = Items.FirstOrDefault(i => i.IsTrash);
        if (trash != null)
        {
            config.ItemSortOrders[trash.Id.Value] = trash.State.SortOrder;
            config.ItemColumns[trash.Id.Value] = trash.State.Column;
            config.ItemRows[trash.Id.Value] = trash.State.Row;
        }

        _layoutStore.Save(config);
    }

    private void CommitCell(
        DesktopItemViewModel item,
        GridCell cell,
        DesktopGridMetrics metrics,
        int? sortOrder = null)
    {
        _committedCells[item.Id] = cell;
        item.State.Column = cell.Column;
        item.State.Row = cell.Row;
        item.State.SortOrder = sortOrder ?? metrics.GetSlotIndex(cell);

        var origin = metrics.GetCellOrigin(cell);
        item.X = origin.X;
        item.Y = origin.Y;
    }

    public void UpdateCommittedCells(DesktopGridMetrics metrics)
    {
        CalculateLayout(metrics);
    }

    public void CalculateLayout(DesktopGridMetrics metrics, bool forceAutoArrange = false)
    {
        var map = new DesktopOccupancyMap(metrics);
        var trash = Items.FirstOrDefault(item => item.IsTrash);
        var states = Items.Where(item => !item.IsTrash).Select(item => item.State).ToList();
        DesktopItemState? trashState = null;
        if (trash != null)
        {
            var cell = new GridCell(Math.Clamp(trash.State.Column, 0, Math.Max(0, metrics.ColumnCount - 1)),
                Math.Clamp(trash.State.Row, 0, Math.Max(0, metrics.RowCount - 1)));
            map.Occupy(trash.Id.Value, cell);
            trashState = new DesktopItemState(trash.Id.Value, metrics.MonitorId, cell.Column, cell.Row, trash.State.SortOrder);
        }
        var newStates = map.AutoArrange(states, forceAutoArrange).ToList();
        if (trashState != null) newStates.Insert(0, trashState);

        _committedCells.Clear();

        foreach (var state in newStates)
        {
            var item = Items.FirstOrDefault(i => i.State.Id == state.Id);
            if (item != null)
            {
                item.State.Column = state.Column;
                item.State.Row = state.Row;
                item.State.MonitorId = state.MonitorId;

                var cell = new GridCell(state.Column, state.Row);
                _committedCells[item.Id] = cell;

                var cellOrigin = metrics.GetCellOrigin(cell);
                item.X = cellOrigin.X;
                item.Y = cellOrigin.Y;
            }
        }
    }

    public static void MoveItem<T>(IList<T> list, int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex || fromIndex < 0 || toIndex < 0 || fromIndex >= list.Count || toIndex >= list.Count)
            return;
        var item = list[fromIndex];
        list.RemoveAt(fromIndex);
        list.Insert(toIndex, item);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
