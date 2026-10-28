using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Files.Models;

namespace DaisyOS.Shell.Apps.Files.Picker;

public partial class DaisyFilePickerWindow : Window
{
    private readonly DaisyFilePickerOptions _options;
    private readonly ObservableCollection<PickerEntry> _entries = [];
    private readonly ObservableCollection<PickerEntry> _visibleEntries = [];
    private readonly ObservableCollection<PickerSelection> _selection = [];
    private readonly Stack<string> _history = new();
    private readonly List<Button> _locationButtons = [];
    private readonly bool _hadInitialSelection;
    private readonly HashSet<string> _allowedExtensions;
    private readonly bool _filterByExtensions;
    private static readonly HashSet<string> ThumbnailExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".ico" };
    private string _currentFolder;
    private string _sortColumn;
    private bool _sortAscending;
    private bool _isGridView;
    private int _navigationVersion;
    private bool _isLoading;
    private string? _loadError;
    private readonly HashSet<IDisposable> _ownedThumbnails = new(ReferenceEqualityComparer.Instance);
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public DaisyFilePickerWindow() : this(new DaisyFilePickerOptions(
        "Choose files", true, false, Array.Empty<string>()))
    { }

    public DaisyFilePickerWindow(DaisyFilePickerOptions options)
    {
        _options = options;
        var preferences = DaisyFilePickerPreferencesStore.Load();
        _sortColumn = preferences.SortColumn is "Name" or "Date" or "Type" or "Size"
            ? preferences.SortColumn
            : "Name";
        _sortAscending = preferences.SortAscending;
        _isGridView = preferences.IsGridView;
        var requestedTypes = options.AllowedExtensions.Select(extension => extension.Trim())
            .Where(extension => extension.Length > 0).ToArray();
        _filterByExtensions = requestedTypes.Length > 0 && !requestedTypes.Any(IsAllFilesPattern);
        _allowedExtensions = requestedTypes.Where(extension => !IsAllFilesPattern(extension))
            .Select(NormalizeExtension).Where(extension => extension is not null)
            .Select(extension => extension!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _hadInitialSelection = options.InitialSelection?.Any(IsAllowedFile) == true;
        _currentFolder = ResolveInitialFolder(options);
        InitializeComponent();
        // Handle window navigation before a tile consumes arrow keys, and
        // keep a focus target when navigation removes the focused tile.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Loc.Bind(this, Window.TitleProperty, Loc.Original(options.Title));
        Loc.RefreshOnChange(this, () => { ApplyViewMode(); ApplyFilter(); UpdateSelectionUi(); });
        EntriesGridView.ItemsSource = _visibleEntries;
        EntriesListView.ItemsSource = _visibleEntries;
        ApplyViewMode();
        SelectionTray.ItemsSource = _selection;
        AddFolderButton.IsVisible = options.AllowFolderContents || (options.SelectDirectory && options.AllowMultiple);
        if (options.SelectDirectory) AddFolderButton.Content = Loc.Get("Add this folder");
        if (options.SaveFile)
        {
            _saveName.Text = Path.GetFileName(options.SuggestedName ?? "Untitled");
            var panel = (StackPanel)SelectionTitle.Parent!;
            panel.Children.Insert(0, _saveName);
            _saveName.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) UpdateSelectionUi(); };
        }
        SearchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == SearchBar.TextProperty) ApplyFilter();
        };
        LoadLocations();
        SeedSelection(options.InitialSelection ?? []);
        OpenFolder(_currentFolder, rememberCurrent: false);
    }

    private readonly TextBox _saveName = new() { PlaceholderText = "File name" };
    private IReadOnlyList<string>? _portalSelection;
    public IReadOnlyList<string> SelectedPaths => _portalSelection ?? _selection.Select(item => item.Path).ToArray();
    public bool WasConfirmed { get; private set; }

    private static string ResolveInitialFolder(DaisyFilePickerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.InitialFolder) && Directory.Exists(options.InitialFolder))
            return Path.GetFullPath(options.InitialFolder);
        var first = options.InitialSelection?.FirstOrDefault(File.Exists);
        var parent = first is null ? null : Path.GetDirectoryName(first);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) return parent;
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var imageOnly = options.AllowedExtensions.Count > 0 && options.AllowedExtensions.All(extension =>
            ThumbnailExtensions.Contains("." + extension.Trim().TrimStart('*').TrimStart('.')));
        return imageOnly && Directory.Exists(pictures) ? pictures : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private void SeedSelection(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(IsAllowedFile).Distinct(PathComparer).Take(_options.AllowMultiple ? int.MaxValue : 1))
            _selection.Add(new PickerSelection(path, OwnThumbnail(LoadThumbnail(path, 80))));
        UpdateSelectionUi();
    }

    private void LoadLocations()
    {
        AddLocation("Home", UserDirectoryPaths.Current["Home"], "house");
        AddLocation("Documents", UserDirectoryPaths.Current["Documents"], "file-text");
        AddLocation("Pictures", UserDirectoryPaths.Current["Pictures"], "image");
        AddLocation("Desktop", UserDirectoryPaths.Current["Desktop"], "desktop");
        AddLocation("Downloads", UserDirectoryPaths.Current["Downloads"], "download-simple");
        foreach (var drive in DriveInfo.GetDrives().Where(IsUserFacingDrive))
            AddLocation(drive.VolumeLabel.Length > 0 ? drive.VolumeLabel : drive.Name, drive.RootDirectory.FullName, "hard-drives", localize: false);
    }

    private static bool IsUserFacingDrive(DriveInfo drive)
    {
        if (!drive.IsReady) return false;
        var path = drive.RootDirectory.FullName;
        if (path == Path.GetPathRoot(path)) return path == "/";
        return path.StartsWith("/run/media/", StringComparison.Ordinal)
            || path.StartsWith("/media/", StringComparison.Ordinal)
            || path.StartsWith("/mnt/", StringComparison.Ordinal);
    }

    private void AddLocation(string name, string path, string icon, bool localize = true)
    {
        if (!Directory.Exists(path)) return;
        var button = new Button { Classes = { "PickerSidebarItem" }, Tag = path };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 9 };
        grid.Children.Add(new DaisyIcon { Icon = icon, Size = 17, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        var label = new TextBlock { Text = name, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        if (localize) Loc.Bind(label, TextBlock.TextProperty, name);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        button.Content = grid;
        button.Click += (_, _) => OpenFolder(path);
        _locationButtons.Add(button);
        LocationsPanel.Children.Add(button);
    }

    private void OpenFolder(string path, bool rememberCurrent = true)
    {
        if (!Directory.Exists(path))
        {
            FolderSummary.Text = Loc.Get("This folder is no longer available. Choose another location.");
            return;
        }
        path = Path.GetFullPath(path);
        if (rememberCurrent && !string.Equals(_currentFolder, path, PathComparison)) _history.Push(_currentFolder);
        _currentFolder = path;
        (_isGridView ? (Control)EntriesGridView : EntriesListView).Focus();
        foreach (var location in _locationButtons)
            location.Classes.Set("Active", string.Equals(location.Tag as string, _currentFolder, PathComparison));
        _isLoading = true;
        _loadError = null;
        foreach (var entry in _entries) entry.ClearVisual();
        _entries.Clear();
        _visibleEntries.Clear();
        ReleaseUnusedThumbnails();
        SearchBox.Text = string.Empty;
        EmptyState.IsVisible = false;
        AddFolderButton.IsEnabled = false;
        BackButton.IsEnabled = _history.Count > 0;
        UpButton.IsEnabled = Directory.GetParent(_currentFolder) is not null;
        RebuildBreadcrumbs();
        UpdateSelectionUi();
        _ = LoadFolderAsync(++_navigationVersion);
    }

    private async Task LoadFolderAsync(int version)
    {
        FolderTitle.Text = DisplayFolderName(_currentFolder);
        FolderSummary.Text = Loc.Get("Loading…");
        var folder = _currentFolder;
        string? error = null;
        var items = await Task.Run(() =>
        {
            try
            {
                var directories = Directory.EnumerateDirectories(folder)
                    .Where(path => !IsHidden(path)).Select(path => new PickerEntry(path, true));
                var files = Directory.EnumerateFiles(folder)
                    .Where(IsAllowedFile).Where(path => !IsHidden(path))
                    .Select(path => new PickerEntry(path, false));
                return directories.Concat(files).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = "This folder could not be opened. Check that it is available and that you have permission to read it.";
                return [];
            }
        });
        if (version != _navigationVersion) return;
        _isLoading = false;
        _loadError = error;
        _entries.Clear();
        foreach (var item in SortEntries(items))
        {
            item.IsSelected = _selection.Any(selected => string.Equals(selected.Path, item.Path, PathComparison));
            _entries.Add(item);
        }
        ApplyFilter();
        var folderCount = items.Count(item => item.IsDirectory);
        var fileCount = items.Length - folderCount;
        FolderSummary.Text = error ?? $"{folderCount} {(folderCount == 1 ? "folder" : "folders")} · {fileCount} {Pluralize(fileCount)}";
        // Decode one image at a time and abandon the old folder between images.
        // A large folder must not queue thousands of thread-pool jobs.
        foreach (var item in items)
        {
            if (version != _navigationVersion) return;
            await item.LoadVisualAsync();
        }
    }

    private IImage? OwnThumbnail(IImage? image)
    {
        if (image is IDisposable disposable) _ownedThumbnails.Add(disposable);
        return image;
    }

    private void ReleaseUnusedThumbnails()
    {
        var retained = _entries.Select(item => item.Thumbnail).Concat(_selection.Select(item => item.Thumbnail))
            .OfType<IDisposable>().ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var image in _ownedThumbnails.Where(image => !retained.Contains(image)).ToArray())
        {
            _ownedThumbnails.Remove(image);
            image.Dispose();
        }
    }

    private static IImage? LoadThumbnail(string path, int width)
    {
        if (!ThumbnailExtensions.Contains(Path.GetExtension(path))) return null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 64L * 1024 * 1024) return null;
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, width);
        }
        catch { return null; }
    }

    private bool IsAllowedFile(string path) => File.Exists(path) &&
        (!_filterByExtensions || _allowedExtensions.Contains(Path.GetExtension(path)));

    private static bool IsAllFilesPattern(string pattern) => pattern is "*" or "*.*";

    private static string? NormalizeExtension(string pattern)
    {
        // Accept the common extension forms supplied by callers: "png",
        // ".png", and "*.png". A non-extension filter must not silently
        // broaden a restricted picker to every file.
        var extension = pattern.StartsWith("*.", StringComparison.Ordinal)
            ? pattern[1..]
            : pattern.StartsWith(".", StringComparison.Ordinal)
                ? pattern
                : pattern.IndexOfAny(['*', '/', '\\']) < 0
                    ? "." + pattern.TrimStart('.')
                    : string.Empty;
        return extension.Length > 1 && extension[1..].IndexOfAny(['*', '/', '\\']) < 0
            ? extension
            : null;
    }

    private static bool IsHidden(string path)
    {
        try { return Path.GetFileName(path).StartsWith('.') || File.GetAttributes(path).HasFlag(FileAttributes.Hidden); }
        catch { return true; }
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Children.Clear();
        var root = Path.GetPathRoot(_currentFolder) ?? "/";
        var relative = _currentFolder[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        AddBreadcrumb(root == "/" ? "System" : root, current);
        foreach (var segment in relative)
        {
            Breadcrumbs.Children.Add(new DaisyIcon { Icon = "caret-right", Size = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Opacity = .55 });
            current = Path.Combine(current, segment);
            AddBreadcrumb(segment, current);
        }
        var last = Breadcrumbs.Children.LastOrDefault();
        Dispatcher.UIThread.Post(() => last?.BringIntoView(), DispatcherPriority.Loaded);
    }

    private void AddBreadcrumb(string name, string path)
    {
        var button = new Button { Content = name, Tag = path, Padding = new Thickness(7, 2), MinWidth = 0, Background = Brushes.Transparent };
        button.Click += (_, _) => OpenFolder(path);
        Breadcrumbs.Children.Add(button);
    }

    private static string DisplayFolderName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private void OnEntryClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PickerEntry entry }) return;
        if (entry.IsDirectory) OpenFolder(entry.Path);
        else if (_options.SaveFile) _saveName.Text = entry.Name;
        else if (!_options.SelectDirectory) ToggleSelection(entry);
    }

    private void OnEntryDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PickerEntry entry }) return;
        e.Handled = true;
        if (entry.IsDirectory)
        {
            OpenFolder(entry.Path);
            return;
        }
        if (_options.AllowMultiple || _options.SaveFile || _options.SelectDirectory || !IsAllowedFile(entry.Path))
            return;

        // Two Click events may have toggled the row before DoubleTapped fires.
        // Establish the intended single selection deterministically, then use
        // the normal confirmation path so validation and portal results match.
        ClearSelection();
        _selection.Add(new PickerSelection(entry.Path, OwnThumbnail(LoadThumbnail(entry.Path, 80))));
        entry.IsSelected = true;
        UpdateSelectionUi();
        OnConfirmClicked(ConfirmButton, new RoutedEventArgs(Button.ClickEvent));
    }

    private void ToggleSelection(PickerEntry entry)
    {
        var existing = _selection.FirstOrDefault(item => string.Equals(item.Path, entry.Path, PathComparison));
        if (existing is not null)
        {
            _selection.Remove(existing);
            entry.IsSelected = false;
        }
        else
        {
            if (!_options.AllowMultiple) ClearSelection();
            _selection.Add(new PickerSelection(entry.Path, OwnThumbnail(LoadThumbnail(entry.Path, 80))));
            entry.IsSelected = true;
        }
        UpdateSelectionUi();
    }

    private void ClearSelection()
    {
        _selection.Clear();
        foreach (var entry in _entries) entry.IsSelected = false;
    }

    private void OnRemoveSelectionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PickerSelection selection }) return;
        _selection.Remove(selection);
        var entry = _entries.FirstOrDefault(item => string.Equals(item.Path, selection.Path, PathComparison));
        if (entry is not null) entry.IsSelected = false;
        UpdateSelectionUi();
    }

    private void OnAddFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (_options.SelectDirectory)
        {
            if (!_selection.Any(item => PathComparer.Equals(item.Path, _currentFolder)))
                _selection.Add(new PickerSelection(_currentFolder, null));
            UpdateSelectionUi();
            return;
        }
        var candidates = _entries.Where(entry => entry.IsVisible && !entry.IsDirectory && IsAllowedFile(entry.Path)).ToArray();
        if (!_options.AllowMultiple) ClearSelection();
        if (!_options.AllowMultiple && candidates.Length > 0) candidates = [candidates[0]];
        foreach (var entry in candidates)
        {
            if (_selection.Any(item => string.Equals(item.Path, entry.Path, PathComparison))) continue;
            _selection.Add(new PickerSelection(entry.Path, OwnThumbnail(LoadThumbnail(entry.Path, 80))));
            entry.IsSelected = true;
        }
        UpdateSelectionUi();
    }

    private void UpdateSelectionUi()
    {
        ReleaseUnusedThumbnails();
        if (_options.SaveFile || _options.SelectDirectory)
        {
            SelectionTitle.Text = _options.SelectDirectory && _selection.Count > 0 ? Loc.Format("{0} folders selected", _selection.Count) : _currentFolder;
            SelectionHint.Text = _options.SaveFile ? Loc.Get("Choose a name and location.") : Loc.Get("Choose this folder.");
            ConfirmButton.Content = _options.SaveFile ? Loc.Get("Save") : Loc.Get("Choose folder");
            var name = _saveName.Text;
            ConfirmButton.IsEnabled = Directory.Exists(_currentFolder) && (!_options.SaveFile ||
                (!string.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) && name is not "." and not ".." && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0));
            SelectionTrayScroller.IsVisible = _options.SelectDirectory && _selection.Count > 0;
            return;
        }
        SelectionTitle.Text = _selection.Count switch
        {
            0 => Loc.Format("No {0} selected", _options.ItemNamePlural),
            1 => Loc.Format("1 {0} selected", _options.ItemNameSingular),
            _ => Loc.Format("{0} {1} selected", _selection.Count, _options.ItemNamePlural)
        };
        SelectionHint.Text = _selection.Count == 0
            ? Loc.Get(_options.AllowMultiple ? "Select a file to continue." : "Select a file, or double-click it to open.")
            : Loc.Get("Select an item again, or use ×, to remove it.");
        SelectionTrayScroller.IsVisible = _selection.Count > 0;
        ConfirmButton.IsEnabled = _selection.Count > 0 || _hadInitialSelection;
        ConfirmButton.Content = _selection.Count switch
        {
            0 when _hadInitialSelection => Loc.Format("Remove all {0}", _options.ItemNamePlural),
            0 => Loc.Get("Choose"),
            1 => Loc.Format("Choose {0}", _options.ItemNameSingular),
            _ => Loc.Format("Choose {0} {1}", _selection.Count, _options.ItemNamePlural)
        };
    }

    private string Pluralize(int count) => count == 1 ? _options.ItemNameSingular : _options.ItemNamePlural;

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var visible = 0;
        _visibleEntries.Clear();
        foreach (var entry in SortEntries(_entries.Where(entry => query.Length == 0 || entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))))
        {
            entry.IsVisible = true;
            _visibleEntries.Add(entry);
            visible++;
        }
        foreach (var entry in _entries.Except(_visibleEntries)) entry.IsVisible = false;
        AddFolderButton.IsEnabled = !_isLoading && (_options.SelectDirectory || _entries.Any(entry => !entry.IsDirectory && entry.IsVisible));
        EmptyState.IsVisible = !_isLoading && visible == 0;
        EmptyStateTitle.Text = _loadError is not null ? Loc.Get("Folder unavailable") : query.Length == 0
            ? Loc.Format("No {0} here", _options.ItemNamePlural)
            : Loc.Format("No matching {0}", _options.ItemNamePlural);
    }

    private static IOrderedEnumerable<PickerEntry> SortEntries(IEnumerable<PickerEntry> entries, string column, bool ascending)
    {
        // Directories stay grouped first, matching Files; the selected key and
        // direction apply independently within each group.
        var grouped = entries.OrderBy(entry => entry.IsDirectory ? 0 : 1);
        return column switch
        {
            "Date" => grouped.ThenBy(entry => entry.ModifiedAt, ascending ? Comparer<DateTime>.Default : Comparer<DateTime>.Create((a, b) => b.CompareTo(a))).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            "Type" => grouped.ThenBy(entry => entry.Type, ascending ? StringComparer.CurrentCultureIgnoreCase : Comparer<string>.Create((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(b, a))).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            "Size" => grouped.ThenBy(entry => entry.Size, ascending ? Comparer<long>.Default : Comparer<long>.Create((a,b) => b.CompareTo(a))).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => grouped.ThenBy(entry => entry.Name, ascending ? StringComparer.CurrentCultureIgnoreCase : Comparer<string>.Create((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(b, a)))
        };
    }

    private IOrderedEnumerable<PickerEntry> SortEntries(IEnumerable<PickerEntry> entries) => SortEntries(entries, _sortColumn, _sortAscending);

    private void SetSort(string column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
        SavePickerPreferences();
        ApplyFilter();
    }

    private void OnSortMenuClicked(object? sender, RoutedEventArgs e) => SortMenu.Open(SortButton);
    private void OnSortNameClicked(object? sender, RoutedEventArgs e) => SetSort("Name");
    private void OnSortDateClicked(object? sender, RoutedEventArgs e) => SetSort("Date");
    private void OnSortTypeClicked(object? sender, RoutedEventArgs e) => SetSort("Type");
    private void OnSortSizeClicked(object? sender, RoutedEventArgs e) => SetSort("Size");
    private void OnSortAscendingClicked(object? sender, RoutedEventArgs e) { _sortAscending = true; SavePickerPreferences(); ApplyFilter(); }
    private void OnSortDescendingClicked(object? sender, RoutedEventArgs e) { _sortAscending = false; SavePickerPreferences(); ApplyFilter(); }
    private void OnToggleViewClicked(object? sender, RoutedEventArgs e)
    {
        _isGridView = !_isGridView;
        ApplyViewMode();
        SavePickerPreferences();
    }

    private void ApplyViewMode()
    {
        EntriesGridView.IsVisible = _isGridView;
        EntriesListView.IsVisible = !_isGridView;
        ViewModeIcon.Icon = _isGridView ? "list-view" : "squares-four";
        ToolTip.SetTip(ViewModeIcon, Loc.Get(_isGridView ? "Switch to list view" : "Switch to grid view"));
    }

    private void SavePickerPreferences() => DaisyFilePickerPreferencesStore.Save(
        new DaisyFilePickerPreferences(_sortColumn, _sortAscending, _isGridView));

    private void OnBackClicked(object? sender, RoutedEventArgs e)
    {
        if (_history.TryPop(out var path)) OpenFolder(path, rememberCurrent: false);
    }

    private void OnUpClicked(object? sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentFolder)?.FullName;
        if (parent is not null) OpenFolder(parent);
    }

    private void OnConfirmClicked(object? sender, RoutedEventArgs e)
    {
        if (!ConfirmButton.IsEnabled) return;
        if (_options.SaveFile || _options.SelectDirectory)
        {
            _portalSelection = _options.SaveFile ? [Path.Combine(_currentFolder, _saveName.Text!)]
                : _selection.Count > 0 ? _selection.Select(item => item.Path).ToArray() : [_currentFolder];
            if (!_options.SaveFile && _portalSelection.Any(path => !Directory.Exists(path)))
            {
                SelectionHint.Text = Loc.Get("A selected folder is no longer available.");
                return;
            }
            WasConfirmed = true;
            Close(SelectedPaths);
            return;
        }
        if (_selection.Any(item => !IsAllowedFile(item.Path)))
        {
            SelectionHint.Text = Loc.Get("Some selected files are no longer available. Remove them or choose them again.");
            return;
        }
        WasConfirmed = true;
        Close(SelectedPaths);
    }
    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = string.Empty;
            else Close(null);
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.FocusInput();
            e.Handled = true;
        }
        else if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt)
        {
            OnBackClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Up && e.KeyModifiers == KeyModifiers.Alt)
        {
            OnUpClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            OnConfirmClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Interlocked.Increment(ref _navigationVersion);
        foreach (var entry in _entries) entry.ClearVisual();
        foreach (var image in _ownedThumbnails) image.Dispose();
        _ownedThumbnails.Clear();
        base.OnClosed(e);
    }
}

public sealed class PickerEntry : INotifyPropertyChanged
{
    private readonly FileEntryViewModel _file;
    private bool _isSelected;
    private bool _isVisible = true;
    public PickerEntry(string path, bool isDirectory)
    {
        FileEntrySnapshot snapshot;
        try
        {
            var info = isDirectory ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
            var attributes = info.Attributes;
            var length = !isDirectory && info is FileInfo file ? file.Length : 0;
            snapshot = new FileEntrySnapshot(global::System.IO.Path.GetFullPath(path), global::System.IO.Path.GetFileName(path.TrimEnd(global::System.IO.Path.DirectorySeparatorChar)), isDirectory ? string.Empty : global::System.IO.Path.GetExtension(path).ToLowerInvariant(), isDirectory, attributes, length, info.LastWriteTimeUtc.Ticks);
        }
        catch
        {
            snapshot = new FileEntrySnapshot(global::System.IO.Path.GetFullPath(path), global::System.IO.Path.GetFileName(path.TrimEnd(global::System.IO.Path.DirectorySeparatorChar)), isDirectory ? string.Empty : global::System.IO.Path.GetExtension(path).ToLowerInvariant(), isDirectory, isDirectory ? FileAttributes.Directory : FileAttributes.Normal, 0, 0);
        }
        _file = new FileEntryViewModel(snapshot);
        _file.PropertyChanged += (_, e) => Changed(e.PropertyName);
    }
    public string Path => _file.Path;
    public string Name => _file.Name;
    public string DisplayName => _file.DisplayName;
    public bool IsDirectory => _file.IsDirectory;
    public DateTime ModifiedAt => _file.LastWriteTime;
    public long Size => _file.Length;
    public string Type => _file.Extension;
    public string DisplayType => _file.DisplayType;
    public string DisplaySize => _file.DisplaySize;
    public string LastWriteTimeDisplay => _file.LastWriteTimeDisplay;
    public string Icon => _file.Icon;
    public string IconColor => _file.IconColor;
    public IImage? Thumbnail => _file.Thumbnail;
    public IImage? ApplicationIcon => _file.ApplicationIcon;
    public bool HasThumbnail => _file.HasThumbnail;
    public bool HasApplicationIcon => _file.HasApplicationIcon;
    public bool HasVisualImage => _file.HasVisualImage;
    public Task LoadVisualAsync() => _file.LoadVisualAsync();
    public void ClearVisual() => _file.ClearVisual();
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; Changed(); } }
    public bool IsVisible { get => _isVisible; set { if (_isVisible == value) return; _isVisible = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class PickerSelection
{
    public PickerSelection(string path, IImage? thumbnail) { Path = path; Thumbnail = thumbnail; }
    public string Path { get; }
    public string Name => global::System.IO.Path.GetFileName(Path);
    public IImage? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;
}
