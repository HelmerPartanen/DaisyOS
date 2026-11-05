using System;
using Directory = global::System.IO.Directory;
using File = global::System.IO.File;
using Path = global::System.IO.Path;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps;
using DaisyOS.Shell.Services.Motion;
using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Apps.Files;

public partial class FilesWindow : Window
{
    private static readonly DataFormat<FileDragPayload> FileDragPayloadFormat =
        DataFormat.CreateInProcessFormat<FileDragPayload>("files-drag-payload");

    private readonly FilesViewModel _viewModel;
    private readonly SystemAppWindowChrome _windowChrome;
    private PointerPressedEventArgs? _fileDragPress;
    private FileEntryViewModel? _fileDragSource;
    private Point _fileDragStart;
    private bool _fileDragInProgress;
    private string[] _draggedPaths = [];
    private CancellationTokenSource? _tabDragActivationCancellation;
    private FilesTabViewModel? _tabDragActivationTarget;
    private string _typeAheadBuffer = string.Empty;
    private CancellationTokenSource? _typeAheadReset;
    private FilesTabViewModel? _hookedTab;
    private bool _visualUpdatePending;
    private (FileEntryViewModel Entry, double Y, Vector Offset)? _viewportAnchor;

    public FilesWindow() : this(null, false) { }

    internal FilesWindow(string? initialPath, bool selectItem)
    {
        InitializeComponent();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty) UpdateCaptionState();
        };
        UpdateCaptionState();
        _windowChrome = new SystemAppWindowChrome(
            this,
            TabRow,
            ContentLayout,
            normalRowDefinitions: "44,Auto,*,Auto",
            fullscreenRowDefinitions: "0,Auto,*,Auto");
        _viewModel = new FilesViewModel(ResolveInitialFolder(initialPath), initialPath, selectItem);
        Loc.RefreshOnChange(this, () =>
        {
            _viewModel.RefreshStandardLocationLabels();
            Dispatcher.UIThread.Post(UpdateSidebarActiveState, DispatcherPriority.Render);
        });
        DataContext = _viewModel;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(FilesViewModel.IsPreviewPaneVisible) or nameof(FilesViewModel.ActiveTab))
            {
                UpdatePreviewPaneLayout();
            }
            if (args.PropertyName is nameof(FilesViewModel.ActiveTab))
            {
                HookActiveTab();
                UpdateSidebarActiveState();
            }
        };
        EntriesList.ContainerPrepared += (_, _) => ScheduleVisuals();
        EntriesList.ContainerClearing += (_, _) => ScheduleVisuals();
        EntriesGrid.ContainerPrepared += (_, _) => ScheduleVisuals();
        EntriesGrid.ContainerClearing += (_, _) => ScheduleVisuals();
        AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => ScheduleVisuals(), RoutingStrategies.Bubble);
        HookActiveTab();
        if (Application.Current is App app) app.AppearanceChanged += OnAppearanceChanged;
        FileContentGrid.SizeChanged += (_, _) => UpdateDetailsLayout();
        BreadcrumbItems.SizeChanged += (_, _) => UpdateBreadcrumbLayout();
        UpdatePreviewPaneLayout();
        // ListBox may capture the pointer while it updates selection. Listen at
        // the window as well so a drag remains reliable after that capture and
        // can leave the app for another Wayland client.
        AddHandler(PointerMovedEvent, OnEntryPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnEntryPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);

        Activated += (_, _) => _viewModel.RefreshVolumes();
        Loaded += (_, _) =>
        {
            HookActiveTab();
            UpdateSidebarActiveState();
        };
    }

    internal void ReportFeedback(string message) => _viewModel.ActiveTab?.ReportFeedback(message);

    internal void Reveal(string path, bool selectItem)
    {
        _viewModel.Reveal(path, selectItem);
    }

    private static string ResolveInitialFolder(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            return path;
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                return parent;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private bool _animateNavigationPending;

    private void HookActiveTab()
    {
        if (_hookedTab != null)
        {
            _animateNavigationPending = IsVisible;
            _hookedTab.PropertyChanged -= OnActiveTabPropertyChanged;
            _hookedTab.RenameRequested -= StartRenameForEntry;
            _hookedTab.EntriesChanged -= ScheduleVisuals;
            _hookedTab.ProjectionChanging -= CaptureViewportAnchor;
            _hookedTab.ProjectionChanged -= RestoreViewportAnchor;
            _hookedTab.SetViewport([]);
        }

        _hookedTab = _viewModel.ActiveTab;
        if (_hookedTab != null)
        {
            _hookedTab.SystemClipboard = Clipboard;
            _hookedTab.StorageProvider = StorageProvider;
            _hookedTab.PropertyChanged += OnActiveTabPropertyChanged;
            _hookedTab.RenameRequested += StartRenameForEntry;
            _hookedTab.EntriesChanged += ScheduleVisuals;
            _hookedTab.ProjectionChanging += CaptureViewportAnchor;
            _hookedTab.ProjectionChanged += RestoreViewportAnchor;
            ScheduleVisuals();
        }
    }

    private void OnActiveTabPropertyChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FilesTabViewModel.CurrentPath) or nameof(FilesTabViewModel.IsGridView))
        {
            _animateNavigationPending = IsVisible;
            Dispatcher.UIThread.Post(UpdateBreadcrumbLayout, DispatcherPriority.Loaded);
            UpdateSidebarActiveState();
            ScheduleVisuals();
        }
    }

    private ListBox CurrentList => _viewModel.ActiveTab?.IsGridView == true ? EntriesGrid : EntriesList;
    private void ScheduleVisuals()
    {
        if (_visualUpdatePending) return;
        _visualUpdatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _visualUpdatePending = false;
            var tab = _viewModel.ActiveTab;
            if (tab?.CurrentSession is not { IsReady: true }) return;
            var list = CurrentList;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroll == null) return;
            var viewport = new Rect(scroll.Bounds.Size);
            var visible = new global::System.Collections.Generic.List<FileEntryViewModel>();
            var nearby = new global::System.Collections.Generic.List<FileEntryViewModel>();
            foreach (var container in list.GetRealizedContainers())
            {
                if (container.DataContext is not FileEntryViewModel entry || !container.IsVisible) continue;
                var point = container.TranslatePoint(default, scroll);
                if (point != null && new Rect(point.Value, container.Bounds.Size).Intersects(viewport)) visible.Add(entry);
                else if (!container.IsKeyboardFocusWithin) nearby.Add(entry);
            }
            if (_animateNavigationPending)
            {
                _animateNavigationPending = false;
                // A short content fade keeps the chrome still and avoids travel/overshoot.
                var duration = list.TryFindResource("MotionDurationFast", out var value) && value is TimeSpan configured
                    ? configured : TimeSpan.FromMilliseconds(100);
                _ = GuiMotion.RevealAsync(list, default(Point), duration);
            }
            tab.SetViewport(visible, nearby);
            tab.CurrentSession.Performance.Count("Realized containers sampled", list.GetRealizedContainers().Count());
        }, DispatcherPriority.Loaded);
    }
    private void CaptureViewportAnchor()
    {
        _viewportAnchor = null;
        var list = CurrentList;
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll == null) return;
        foreach (var container in list.GetRealizedContainers().OrderBy(c => list.IndexFromContainer(c)))
        {
            var point = container.TranslatePoint(default, scroll);
            if (container.DataContext is FileEntryViewModel entry && point is { } position && position.Y + container.Bounds.Height > 0)
            {
                _viewportAnchor = (entry, position.Y, scroll.Offset);
                break;
            }
        }
    }
    private void RestoreViewportAnchor()
    {
        var anchor = _viewportAnchor;
        _viewportAnchor = null;
        if (anchor == null) return;
        var tab = _viewModel.ActiveTab;
        var session = tab?.CurrentSession;
        var list = CurrentList;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(tab, _viewModel.ActiveTab) || !ReferenceEquals(session, tab?.CurrentSession) || !tab!.FilteredEntries.Contains(anchor.Value.Entry)) return;
            list.ScrollIntoView(anchor.Value.Entry);
            Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(session, tab.CurrentSession)) return;
                var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                var container = list.ContainerFromItem(anchor.Value.Entry);
                var point = scroll == null ? null : container?.TranslatePoint(default, scroll);
                if (scroll != null && point != null) scroll.Offset = new Vector(anchor.Value.Offset.X, scroll.Offset.Y + point.Value.Y - anchor.Value.Y);
                ScheduleVisuals();
            }, DispatcherPriority.Loaded);
        }, DispatcherPriority.Loaded);
    }

    private void UpdateSidebarActiveState()
    {
        var currentPath = _viewModel.ActiveTab?.CurrentPath;
        if (string.IsNullOrEmpty(currentPath))
            return;

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var home = userProfile;
        var desktop = UserDirectoryPaths.Current["Desktop"];
        var documents = UserDirectoryPaths.Current["Documents"];
        var downloads = UserDirectoryPaths.Current["Downloads"];
        var pictures = UserDirectoryPaths.Current["Pictures"];
        var videos = UserDirectoryPaths.Current["Videos"];
        var music = UserDirectoryPaths.Current["Music"];
        var trash = UserTrashPaths.Files;

        var sidebarButtons = this.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("SettingsNavItem"));
        foreach (var btn in sidebarButtons)
        {
            var tag = btn.Tag as string;
            var isActive = tag switch
            {
                "home" => string.Equals(currentPath, home, comparison),
                "desktop" => string.Equals(currentPath, desktop, comparison),
                "documents" => string.Equals(currentPath, documents, comparison),
                "downloads" => string.Equals(currentPath, downloads, comparison),
                "pictures" => string.Equals(currentPath, pictures, comparison),
                "videos" => string.Equals(currentPath, videos, comparison),
                "music" => string.Equals(currentPath, music, comparison),
                "trash" => string.Equals(currentPath, trash, StringComparison.Ordinal) || currentPath.StartsWith(trash + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                _ => !string.IsNullOrEmpty(tag) && string.Equals(currentPath, tag, comparison)
            };
            btn.Classes.Set("Active", isActive);
            if (btn.GetVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("PinnedLocationRow")) is { } pinnedRow)
                pinnedRow.Classes.Set("Active", isActive);
        }
    }

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateBreadcrumbLayout, DispatcherPriority.Loaded);
        FileVisualResolver.InvalidateAssociations();
        _viewModel.ActiveTab?.SetViewport([]);
        ScheduleVisuals();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Application.Current is App app) app.AppearanceChanged -= OnAppearanceChanged;
        if (_hookedTab != null)
        {
            _hookedTab.PropertyChanged -= OnActiveTabPropertyChanged;
            _hookedTab.RenameRequested -= StartRenameForEntry;
            _hookedTab.EntriesChanged -= ScheduleVisuals;
            _hookedTab.ProjectionChanging -= CaptureViewportAnchor;
            _hookedTab.ProjectionChanged -= RestoreViewportAnchor;
            _hookedTab.SetViewport([]);
            _hookedTab = null;
        }
        _typeAheadReset?.Cancel();
        _typeAheadReset?.Dispose();
        _tabDragActivationCancellation?.Cancel();
        _tabDragActivationCancellation?.Dispose();
        foreach (var tab in _viewModel.Tabs) tab.Dispose();
        _windowChrome.Dispose();
        base.OnClosed(e);
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source &&
            source is not TextBox &&
            source is not DaisyOS.Shell.Controls.SearchBar &&
            !source.GetVisualAncestors().Any(v => v is DaisyOS.Shell.Controls.SearchBar || v is TextBox))
        {
            FocusManager?.Focus(null);
        }
    }

    private void OnTabClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: FilesTabViewModel tab })
        {
            _viewModel.ActiveTab = tab;
        }
    }

    private void OnTabDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control { Tag: FilesTabViewModel tab } target ||
            !CanAcceptFileDrop(e, tab.CurrentPath))
        {
            return;
        }

        target.Classes.Set("TabDragOver", true);
        ScheduleTabActivation(tab);
        e.DragEffects = GetRequestedDropEffect(e);
        e.Handled = true;
    }

    private void OnTabDragLeave(object? sender, RoutedEventArgs e)
    {
        if (sender is Control target)
        {
            target.Classes.Set("TabDragOver", false);
        }

        CancelTabActivation();
    }

    private async void OnTabDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { Tag: FilesTabViewModel tab } target)
        {
            return;
        }

        target.Classes.Set("TabDragOver", false);
        CancelTabActivation();
        _viewModel.ActiveTab = tab;
        await CompleteFileDropAsync(e, tab.CurrentPath, tab);
    }

    private void ScheduleTabActivation(FilesTabViewModel tab)
    {
        if (ReferenceEquals(_tabDragActivationTarget, tab))
        {
            return;
        }

        CancelTabActivation();
        _tabDragActivationTarget = tab;
        _tabDragActivationCancellation = new CancellationTokenSource();
        _ = ActivateTabAfterDragHoverAsync(tab, _tabDragActivationCancellation.Token);
    }

    private async Task ActivateTabAfterDragHoverAsync(FilesTabViewModel tab, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450), cancellationToken);
            if (!cancellationToken.IsCancellationRequested && ReferenceEquals(_tabDragActivationTarget, tab))
            {
                _viewModel.ActiveTab = tab;
            }
        }
        catch (OperationCanceledException)
        {
            // The pointer left the tab before it became the intended destination.
        }
    }

    private void CancelTabActivation()
    {
        _tabDragActivationCancellation?.Cancel();
        _tabDragActivationCancellation?.Dispose();
        _tabDragActivationCancellation = null;
        _tabDragActivationTarget = null;
    }

    private void OnCloseTabClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: FilesTabViewModel tab })
        {
            _viewModel.CloseTab(tab);
        }
    }

    private void OnNewTabClicked(object? sender, RoutedEventArgs e) => _viewModel.NewTab();

    private void OnBackClicked(object? sender, RoutedEventArgs e) => _viewModel.ActiveTab?.GoBack();
    private void OnForwardClicked(object? sender, RoutedEventArgs e) => _viewModel.ActiveTab?.GoForward();
    private void OnUpClicked(object? sender, RoutedEventArgs e) => _viewModel.ActiveTab?.GoUp();
    private void OnRefreshClicked(object? sender, RoutedEventArgs e) => _viewModel.ActiveTab?.Refresh();

    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || _viewModel.ActiveTab is null) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
        {
            _ = CopyTextAsync(_viewModel.ActiveTab.CurrentPath, "Location copied.");
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            PathBox.IsVisible = false;
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;
        _ = _viewModel.ActiveTab.NavigateToAsync(textBox.Text ?? _viewModel.ActiveTab.CurrentPath);
        PathBox.IsVisible = false;
        e.Handled = true;
    }

    private void OnPathBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        PathBox.IsVisible = false;
    }

    private void OnBreadcrumbBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            PathBox.IsVisible = true;
            PathBox.Focus();
            PathBox.SelectAll();
        }
    }

    private void UpdateBreadcrumbLayout()
    {
        if (_viewModel.ActiveTab is not { } tab || tab.Breadcrumbs.Count == 0 || BreadcrumbItems.Bounds.Width <= 0) return;
        var available = BreadcrumbItems.Bounds.Width;
        var maximum = Math.Clamp(available, 24, 150);
        if (!Resources.TryGetValue("FilesBreadcrumbMaxWidth", out var current) || !Equals(current, maximum))
            Resources["FilesBreadcrumbMaxWidth"] = maximum;
        var family = this.FindResource("ShellFontFamily") as Avalonia.Media.FontFamily ?? Avalonia.Media.FontFamily.Default;
        var size = this.FindResource("TypeSizeBodySmall") is double configured ? configured : 13;
        double Width(BreadcrumbSegment segment)
        {
            using var text = new Avalonia.Media.TextFormatting.TextLayout(segment.Name, new Avalonia.Media.Typeface(family), size,
                fontFeatures: DaisyOS.Shell.Services.Theming.TypographyPolicy.ProportionalNumerals);
            return Math.Min(maximum, text.WidthIncludingTrailingWhitespace + 16);
        }
        var used = Width(tab.Breadcrumbs[^1]);
        var count = 1;
        for (var index = tab.Breadcrumbs.Count - 2; index >= 0; index--)
        {
            var proposed = used + Width(tab.Breadcrumbs[index]) + 16;
            if (proposed + (index > 0 ? 40 : 0) > available) break;
            used = proposed;
            count++;
        }
        tab.SetVisibleBreadcrumbs(count, count < tab.Breadcrumbs.Count && used + 40 <= available);
    }

    private void OnBreadcrumbClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: BreadcrumbSegment { IsOverflow: true } } overflow && _viewModel.ActiveTab is { } tab)
        {
            var visiblePaths = tab.VisibleBreadcrumbs.Where(segment => !segment.IsOverflow).Select(segment => segment.Path).ToHashSet(StringComparer.Ordinal);
            var menu = new ContextMenu();
            foreach (var segment in tab.Breadcrumbs.Where(segment => !visiblePaths.Contains(segment.Path)))
            {
                var item = new MenuItem { Header = segment.Name };
                item.Click += async (_, _) => await tab.NavigateToAsync(segment.Path);
                menu.Items.Add(item);
            }
            overflow.ContextMenu = menu;
            menu.Open(overflow);
            return;
        }
        if (sender is Button btn && btn.Tag is string path && _viewModel.ActiveTab != null)
        {
            _ = _viewModel.ActiveTab.NavigateToAsync(path);
        }
    }

    private void OnEntryDoubleTapped(object? sender, RoutedEventArgs e)
    {
        var entry = (e.Source as StyledElement)?.DataContext as FileEntryViewModel
                    ?? _viewModel.ActiveTab?.SelectedItems.FirstOrDefault();
        if (entry is null) return;
        OpenEntry(entry);
    }

    private void OnSidebarLocationClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string path })
            return;

        try
        {
            if (FileLocationPresentation.Resolve(path).Kind == FileLocationKind.Trash)
                Directory.CreateDirectory(path);
            if (Directory.Exists(path))
                _ = _viewModel.ActiveTab?.NavigateToAsync(path);
        }
        catch
        {
            (Application.Current as App)?.Feedback.Show(Loc.Get("That location isn’t available right now."));
        }
    }

    private void OnUnpinLocationClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string path } && _viewModel.RemovePinnedLocation(path))
        {
            (Application.Current as App)?.Feedback.Show(Loc.Get("Unpinned from sidebar."));
            e.Handled = true;
        }
    }

    private void OnSidebarLocationDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control control || !TryResolveSidebarDropPath(control, out var destination) ||
            !CanAcceptFileDrop(e, destination))
        {
            return;
        }

        control.Classes.Set("DragOver", true);
        e.DragEffects = GetRequestedDropEffect(e);
        e.Handled = true;
    }

    private void OnSidebarLocationDragLeave(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
        {
            control.Classes.Set("DragOver", false);
        }
    }

    private async void OnSidebarLocationDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control control || !TryResolveSidebarDropPath(control, out var destination))
        {
            return;
        }

        control.Classes.Set("DragOver", false);
        await CompleteFileDropAsync(e, destination);
    }

    private void OnSortNameClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab != null) _viewModel.ActiveTab.SortColumn = "Name";
    }

    private void OnSortDateClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab != null) _viewModel.ActiveTab.SortColumn = "Date";
    }

    private void OnSortTypeClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab != null) _viewModel.ActiveTab.SortColumn = "Type";
    }

    private void OnSortSizeClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab != null) _viewModel.ActiveTab.SortColumn = "Size";
    }

    private void OnToggleViewModeClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab != null)
        {
            _viewModel.ActiveTab.SetGridViewFromUser(!_viewModel.ActiveTab.IsGridView);
        }
    }

    private async void OnEmptyStateActionClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab is not { } tab) return;
        if (tab.HasLoadError) await tab.NavigateToAsync(tab.CurrentPath, addHistory: false);
        else if (tab.HasSearchQuery) OnClearSearchClicked(sender, e);
        else if (tab.HasHiddenEntriesOnly) tab.ShowHiddenFiles = true;
        else OnNewFolderClicked(sender, e);
    }

    private void OnTogglePreviewPaneClicked(object? sender, RoutedEventArgs e) => _viewModel.TogglePreviewPane();

    private void UpdatePreviewPaneLayout()
    {
        if (FileContentGrid.ColumnDefinitions.Count < 2)
        {
            return;
        }

        FileContentGrid.ColumnDefinitions[1].Width = _viewModel.IsPreviewPaneVisible
            ? new GridLength(260)
            : new GridLength(0);
        Dispatcher.UIThread.Post(UpdateDetailsLayout, DispatcherPriority.Render);
    }

    private void UpdateDetailsLayout()
    {
        var tab = _viewModel.ActiveTab;
        if (tab is null)
        {
            return;
        }

        // The sidebar and optional preview have already claimed their layout
        // space. Hide low-priority metadata before it can overlap the name.
        var availableWidth = FileContentGrid.Bounds.Width - (_viewModel.IsPreviewPaneVisible ? 260 : 0);
        tab.SetDetailsColumnVisibility(
            showDate: availableWidth >= 390,
            showType: availableWidth >= 560,
            showSize: availableWidth >= 650);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel.ActiveTab is null)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.T)
        {
            _viewModel.NewTab(_viewModel.ActiveTab.CurrentPath);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.W)
        {
            if (_viewModel.Tabs.Count == 1) Close();
            else _viewModel.CloseTab(_viewModel.ActiveTab);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab && (e.KeyModifiers == KeyModifiers.Control ||
                               e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)))
        {
            var direction = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
            var index = _viewModel.Tabs.IndexOf(_viewModel.ActiveTab);
            _viewModel.ActiveTab = _viewModel.Tabs[(index + direction + _viewModel.Tabs.Count) % _viewModel.Tabs.Count];
            e.Handled = true;
            return;
        }

        if ((e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F) ||
            (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F3))
        {
            FilesSearch.Open();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && FilesSearch.IsOpen)
        {
            FilesSearch.Close(clear: true);
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None) ||
            (e.Key == Key.R && e.KeyModifiers == KeyModifiers.Control))
        {
            _viewModel.ActiveTab.Refresh();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.L)
        {
            PathBox.IsVisible = true;
            PathBox.Text = _viewModel.ActiveTab.CurrentPath;
            PathBox.Focus();
            PathBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.N)
        {
            _ = _viewModel.ActiveTab.CreateNewFolderAsync();
            e.Handled = true;
            return;
        }

        if (e.Source is TextBox or DaisyOS.Shell.Controls.SearchBar ||
            e.Source is Visual source && source.GetVisualAncestors().Any(visual => visual is TextBox or DaisyOS.Shell.Controls.SearchBar))
        {
            return;
        }

        if (e.Key == Key.Escape && _viewModel.ActiveTab.HasSearchQuery)
        {
            ClearSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right && e.KeyModifiers == KeyModifiers.Alt)
        {
            if (_viewModel.ActiveTab.CanGoForward) _viewModel.ActiveTab.GoForward();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.C:
                    _ = _viewModel.ActiveTab.CopySelectedAsync();
                    e.Handled = true;
                    return;
                case Key.X:
                    _ = _viewModel.ActiveTab.CutSelectedAsync();
                    e.Handled = true;
                    return;
                case Key.V:
                    _ = _viewModel.ActiveTab.PasteAsync();
                    e.Handled = true;
                    return;
                case Key.A:
                    _viewModel.ActiveTab.SelectedItems.Clear();
                    foreach (var item in _viewModel.ActiveTab.FilteredEntries)
                    {
                        _viewModel.ActiveTab.SelectedItems.Add(item);
                    }
                    e.Handled = true;
                    return;
                case Key.H:
                    _viewModel.ActiveTab.ShowHiddenFiles = !_viewModel.ActiveTab.ShowHiddenFiles;
                    e.Handled = true;
                    return;
                case Key.Z:
                    _ = _viewModel.ActiveTab.UndoTrashAsync();
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.F2)
        {
            StartRenamingSelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            _ = _viewModel.ActiveTab.DeleteSelectedAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            if (_viewModel.ActiveTab.SelectedEntry is { } selected)
            {
                DaisyOS.Shell.Views.Components.QuickLook.QuickLookWindow.ShowOrToggle(selected.Path);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter)
        {
            if (_viewModel.ActiveTab.SelectedEntry is { } selected)
            {
                OpenEntry(selected);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Back || (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.Left))
        {
            if (_viewModel.ActiveTab.CanGoBack)
            {
                _viewModel.ActiveTab.GoBack();
                e.Handled = true;
                return;
            }
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.Up)
        {
            if (_viewModel.ActiveTab.CanGoUp)
            {
                _viewModel.ActiveTab.GoUp();
                e.Handled = true;
                return;
            }
        }

        if (e.KeyModifiers == KeyModifiers.None && !string.IsNullOrWhiteSpace(e.KeySymbol) && e.KeySymbol!.Length == 1 && !char.IsControl(e.KeySymbol[0]))
        {
            SelectByTypeAhead(e.KeySymbol);
            e.Handled = true;
        }
    }

    private void OnClearSearchClicked(object? sender, RoutedEventArgs e) => ClearSearch();

    private void ClearSearch()
    {
        FilesSearch.Close(clear: true, restoreFocus: false);
        CurrentList.Focus();
    }

    private async void OnNewFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab is not null)
            await _viewModel.ActiveTab.CreateNewFolderAsync();
    }

    private async void OnNewTextDocumentClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab is not null)
            await _viewModel.ActiveTab.CreateNewDocumentAsync();
    }

    private void OnOpenCurrentInTerminalClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab is not null)
            DaisyOS.System.Processes.TerminalLauncher.OpenTerminal(_viewModel.ActiveTab.CurrentPath);
    }

    private async void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab is not null)
            await _viewModel.ActiveTab.DeleteSelectedAsync();
    }

    private async void OnUndoTrashClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab is not null)
            await _viewModel.ActiveTab.UndoTrashAsync();
    }

    private async void OnCutClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab is not null)
            await _viewModel.ActiveTab.CutSelectedAsync();
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab is not null)
            await _viewModel.ActiveTab.CopySelectedAsync();
    }

    private async void OnPasteClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab is not null)
            await _viewModel.ActiveTab.PasteAsync();
    }

    private async void OnPasteReplaceClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab is not null)
            await _viewModel.ActiveTab.PasteAsync(ConflictResolution.Replace);
    }

    private void SelectByTypeAhead(string input)
    {
        var tab = _viewModel.ActiveTab;
        if (tab is null) return;

        _typeAheadBuffer += input;
        var match = tab.FilteredEntries.FirstOrDefault(entry => entry.Name.StartsWith(_typeAheadBuffer, StringComparison.CurrentCultureIgnoreCase));
        if (match is null && _typeAheadBuffer.Length > 1)
        {
            _typeAheadBuffer = input;
            match = tab.FilteredEntries.FirstOrDefault(entry => entry.Name.StartsWith(input, StringComparison.CurrentCultureIgnoreCase));
        }
        if (match is not null)
        {
            SelectEntry(match);
            EntriesList.ScrollIntoView(match);
            EntriesGrid.ScrollIntoView(match);
        }

        _typeAheadReset?.Cancel();
        _typeAheadReset?.Dispose();
        _typeAheadReset = new CancellationTokenSource();
        _ = ClearTypeAheadAsync(_typeAheadReset.Token);
    }

    private async Task ClearTypeAheadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            _typeAheadBuffer = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Another character extended the current type-ahead match.
        }
    }

    private void OnEntryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: FileEntryViewModel entry })
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            var tab = _viewModel.ActiveTab;
            if (tab is not null && !tab.SelectedItems.Contains(entry))
            {
                tab.SelectedItems.Clear();
                tab.SelectedItems.Add(entry);
            }
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            var tab = _viewModel.ActiveTab;
            if (tab is not null)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    if (tab.SelectedItems.Contains(entry)) tab.SelectedItems.Remove(entry);
                    else tab.SelectedItems.Add(entry);
                }
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && tab.SelectedEntry is not null)
                {
                    var startIndex = tab.FilteredEntries.IndexOf(tab.SelectedEntry);
                    var endIndex = tab.FilteredEntries.IndexOf(entry);
                    if (startIndex >= 0 && endIndex >= 0)
                    {
                        var min = Math.Min(startIndex, endIndex);
                        var max = Math.Max(startIndex, endIndex);
                        tab.SelectedItems.Clear();
                        for (var i = min; i <= max; i++) tab.SelectedItems.Add(tab.FilteredEntries[i]);
                    }
                }
                else if (!tab.SelectedItems.Contains(entry))
                {
                    tab.SelectedItems.Clear();
                    tab.SelectedItems.Add(entry);
                }
            }

            _fileDragPress = e;
            _fileDragSource = entry;
            _fileDragStart = e.GetPosition(this);
            (sender as Control)?.GetVisualAncestors().OfType<ListBox>().FirstOrDefault()?.Focus();
            e.Pointer.Capture(sender as Control);
            // Selection was handled above. Do not let ListBoxItem collapse the group again.
            e.Handled = true;
        }
    }

    private async void OnEntryPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_fileDragInProgress || _fileDragPress is null || _fileDragSource is null ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var pointerPosition = e.GetPosition(this);
        var deltaX = pointerPosition.X - _fileDragStart.X;
        var deltaY = pointerPosition.Y - _fileDragStart.Y;
        if (deltaX * deltaX + deltaY * deltaY < 36)
        {
            return;
        }

        var storageProvider = StorageProvider;
        if (storageProvider is null)
        {
            return;
        }

        _fileDragInProgress = true;
        try
        {
            var transfer = new DataTransfer();
            var draggedEntries = SelectedEntriesForDrag().ToArray();
            var draggedPaths = _draggedPaths = draggedEntries.Select(entry => entry.Path).ToArray();
            foreach (var entry in draggedEntries)
            {
                IStorageItem? storageItem = entry.IsDirectory
                    ? await storageProvider.TryGetFolderFromPathAsync(entry.Path)
                    : await storageProvider.TryGetFileFromPathAsync(entry.Path);

                if (storageItem is not null)
                {
                    var item = DataTransferItem.CreateFile(storageItem);
                    item.Set(FileDragPayloadFormat, new FileDragPayload(draggedPaths));
                    transfer.Add(item);
                }
            }

            if (transfer.Items.Count > 0 || draggedPaths.Length > 0)
            {
                using var preview = FileDragPreview.Create(draggedEntries.Select(entry => entry.DisplayName).ToArray(),
                    RenderScaling, draggedEntries[0].Thumbnail ?? draggedEntries[0].ApplicationIcon,
                    draggedEntries[0].Icon, FontFamily);
                var previewItem = new DataTransferItem();
                previewItem.Set(FileDragPreview.Format, preview);
                transfer.Add(previewItem);
                await DragDrop.DoDragDropAsync(_fileDragPress, transfer, DragDropEffects.Copy | DragDropEffects.Move);
            }
        }
        catch
        {
            (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t start that drag."));
        }
        finally
        {
            ResetFileDrag();
        }
    }

    private void OnEntryPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_fileDragPress is null && !_fileDragInProgress) return;
        // Keep the manually selected group intact on release as well.
        e.Handled = true;
        if (!_fileDragInProgress) ResetFileDrag();
    }

    private void OnFileAreaDragOver(object? sender, DragEventArgs e)
    {
        var currentPath = _viewModel.ActiveTab?.CurrentPath;
        if (string.IsNullOrEmpty(currentPath) || !CanAcceptFileDrop(e, currentPath))
        {
            return;
        }

        e.DragEffects = GetRequestedDropEffect(e);
        e.Handled = true;
    }

    private async void OnFileAreaDrop(object? sender, DragEventArgs e)
    {
        var currentPath = _viewModel.ActiveTab?.CurrentPath;
        if (string.IsNullOrEmpty(currentPath) || !CanAcceptFileDrop(e, currentPath))
        {
            return;
        }

        await CompleteFileDropAsync(e, currentPath);
    }

    private void OnFolderDragOver(object? sender, DragEventArgs e)
    {
        if (sender is Control { DataContext: FileEntryViewModel { IsDirectory: true } folder } target)
        {
            if (CanAcceptFileDrop(e, folder.Path))
            {
                target.Classes.Set("DragOver", true);
                e.DragEffects = GetRequestedDropEffect(e);
                e.Handled = true;
                return;
            }
        }

        var currentPath = _viewModel.ActiveTab?.CurrentPath;
        if (!string.IsNullOrEmpty(currentPath) && CanAcceptFileDrop(e, currentPath))
        {
            e.DragEffects = GetRequestedDropEffect(e);
            e.Handled = true;
        }
    }

    private void OnFolderDragLeave(object? sender, RoutedEventArgs e)
    {
        if (sender is Control target)
        {
            target.Classes.Set("DragOver", false);
        }
    }

    private async void OnFolderDrop(object? sender, DragEventArgs e)
    {
        if (sender is Control { DataContext: FileEntryViewModel { IsDirectory: true } folder } target)
        {
            target.Classes.Set("DragOver", false);
            if (CanAcceptFileDrop(e, folder.Path))
            {
                await CompleteFileDropAsync(e, folder.Path);
                return;
            }
        }

        var currentPath = _viewModel.ActiveTab?.CurrentPath;
        if (!string.IsNullOrEmpty(currentPath) && CanAcceptFileDrop(e, currentPath))
        {
            await CompleteFileDropAsync(e, currentPath);
        }
    }

    private async Task CompleteFileDropAsync(DragEventArgs e, string destinationDirectory, FilesTabViewModel? destinationTab = null)
    {
        var sourcePaths = GetLocalDropPaths(e).ToArray();
        if (sourcePaths.Length == 0)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        try
        {
            Directory.CreateDirectory(destinationDirectory);
            var move = GetRequestedDropEffect(e) == DragDropEffects.Move;
            var target = destinationTab ?? _viewModel.ActiveTab;
            var completed = target is null
                ? 0
                : await target.TransferFilesAsync(sourcePaths, destinationDirectory, move);
            e.DragEffects = completed > 0 ? (move ? DragDropEffects.Move : DragDropEffects.Copy) : DragDropEffects.None;
            e.Handled = true;

            if (completed > 0)
            {
                ReconcileTabsAffectedByTransfer(sourcePaths, destinationDirectory);
            }

            if (completed == 0)
            {
                (Application.Current as App)?.Feedback.Show(Loc.Get("That location already contains those items or can’t accept them."));
            }
        }
        catch
        {
            e.DragEffects = DragDropEffects.None;
            (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t transfer those items."));
        }
    }

    private void ReconcileTabsAffectedByTransfer(IEnumerable<string> sourcePaths, string destinationDirectory)
    {
        var sourceParents = sourcePaths
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var tab in _viewModel.Tabs)
        {
            if (sourceParents.Contains(tab.CurrentPath) ||
                string.Equals(tab.CurrentPath, destinationDirectory, StringComparison.Ordinal))
            {
                _ = tab.ReconcilePathsAsync(sourcePaths.Concat(sourcePaths.Select(path => Path.Combine(destinationDirectory, Path.GetFileName(path)))));
            }
        }
    }

    private IEnumerable<string> GetLocalDropPaths(DragEventArgs e)
    {
        if (_fileDragInProgress)
        {
            var selected = _draggedPaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
            if (selected.Length > 0)
            {
                return selected;
            }
        }

        if (e.DataTransfer.TryGetValue(FileDragPayloadFormat) is { Paths.Length: > 0 } payload)
        {
            return payload.Paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        }

        foreach (var item in e.DataTransfer.Items)
        {
            if (item.TryGetValue(FileDragPayloadFormat) is { Paths.Length: > 0 } itemPayload)
            {
                return itemPayload.Paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
            }
        }

        var files = e.DataTransfer.TryGetFiles()?
            .Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .ToArray();

        if (files is { Length: > 0 })
        {
            return files;
        }

        var text = e.DataTransfer.TryGetText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var uriPaths = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Select(line => line.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ? Uri.UnescapeDataString(line.Substring(7)) : line)
                .Where(p => File.Exists(p) || Directory.Exists(p))
                .ToArray();
            if (uriPaths.Length > 0) return uriPaths;
        }

        return [];
    }

    private bool CanAcceptFileDrop(DragEventArgs e, string destinationDirectory) =>
        (Directory.Exists(destinationDirectory) || Directory.Exists(Path.GetDirectoryName(destinationDirectory))) &&
        GetLocalDropPaths(e).Any(path => CanTransferTo(path, destinationDirectory));

    private static bool CanTransferTo(string sourcePath, string destinationDirectory)
    {
        var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
        if (string.Equals(Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(destinationPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
        {
            return false;
        }

        if (!Directory.Exists(sourcePath))
        {
            return true;
        }

        var source = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return !destination.StartsWith(source, StringComparison.Ordinal);
    }

    private static DragDropEffects GetRequestedDropEffect(DragEventArgs e) =>
        e.KeyModifiers.HasFlag(KeyModifiers.Control) || !e.DataTransfer.Contains(FileDragPayloadFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.Move;

    private static bool TryResolveSidebarDropPath(Control control, out string destination)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        destination = (control.Tag as string) switch
        {
            "home" => home,
            "desktop" => UserDirectoryPaths.Current["Desktop"],
            "documents" => UserDirectoryPaths.Current["Documents"],
            "downloads" => UserDirectoryPaths.Current["Downloads"],
            "pictures" => UserDirectoryPaths.Current["Pictures"],
            "videos" => UserDirectoryPaths.Current["Videos"],
            "music" => UserDirectoryPaths.Current["Music"],
            { } path when Path.IsPathRooted(path) => path,
            _ => string.Empty
        };
        return !string.IsNullOrWhiteSpace(destination);
    }

    private void OnEntryContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget.DataContext: FileEntryViewModel entry })
        {
            SelectEntry(entry);
        }
    }

    private void OnOpenEntryContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is { } entry)
        {
            OpenEntry(entry);
        }
    }

    private void OnOpenEntryInNewTabContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is { IsDirectory: true } entry)
        {
            _viewModel.NewTab(entry.Path);
        }
    }

    private void OnOpenEntryInTerminalContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is { IsDirectory: true } entry)
        {
            DaisyOS.System.Processes.TerminalLauncher.OpenTerminal(entry.Path);
        }
    }

    private void StartRenamingSelected()
    {
        var entry = _viewModel.ActiveTab?.SelectedEntry;
        if (entry is null) return;
        StartRenameForEntry(entry);
    }

    private void OnRenameEntryContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is not { } entry)
        {
            return;
        }

        StartRenameForEntry(entry);
    }

    private void StartRenameForEntry(FileEntryViewModel entry)
    {
        SelectEntry(entry);
        entry.IsRenaming = true;
        var tab = _viewModel.ActiveTab;
        var session = tab?.CurrentSession;
        var list = tab?.IsGridView == true ? EntriesGrid : EntriesList;
        list.ScrollIntoView(entry);
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_viewModel.ActiveTab, tab) || !ReferenceEquals(tab?.CurrentSession, session) || !entry.IsRenaming) return;
            var editor = this.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(textBox => ReferenceEquals(textBox.DataContext, entry) && textBox.Classes.Contains("RenameBox"));
            if (editor is not null)
            {
                editor.Focus();
                var name = entry.Name;
                var dotIndex = entry.IsDirectory ? -1 : name.LastIndexOf('.');
                if (dotIndex > 0)
                {
                    editor.SelectionStart = 0;
                    editor.SelectionEnd = dotIndex;
                }
                else
                {
                    editor.SelectAll();
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnAddToPinnedContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is { IsDirectory: true } entry)
        {
            _viewModel.AddPinnedLocation(entry.Path);
            (Application.Current as App)?.Feedback.Show(Loc.Get("Pinned to sidebar."));
        }
    }

    private void OnContinuitySendClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is not { } entry) return;
        if (entry.IsDirectory)
        {
            (Application.Current as App)?.Feedback.Show(Loc.Get("Archive the folder first, then send the archive with Continuity."));
            return;
        }
        var sibling = global::System.IO.Path.GetFullPath(global::System.IO.Path.Combine(AppContext.BaseDirectory, "..", "Settings", "DaisyOS.Settings"));
        var binary = File.Exists(sibling) ? sibling : "daisyos-settings";
        var result = new DaisyOS.System.Processes.SafeProcessLauncher().Launch(binary,
            ["--continuity-send=" + entry.Path, "--category=Continuity"]);
        if (!result.Succeeded) (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t open Continuity settings."));
    }

    private void OnShareEntryContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is not { } entry)
        {
            return;
        }

        var paths = GetLocalSendPaths(entry);
        if (paths.Count > 0) (Application.Current as App)?.ShowLocalSend(paths);
    }

    internal IReadOnlyList<string> GetLocalSendPaths(FileEntryViewModel contextEntry)
    {
        var selected = _viewModel.ActiveTab?.SelectedItems;
        IEnumerable<FileEntryViewModel> entries = selected?.Contains(contextEntry) == true
            ? selected : [contextEntry];
        return entries.Where(entry => !entry.IsDirectory).Select(entry => entry.Path)
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    private void OnCompressEntryContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is not { } entry)
        {
            return;
        }

        var parent = Path.GetDirectoryName(entry.Path);
        if (string.IsNullOrWhiteSpace(parent))
        {
            return;
        }

        var archivePath = Path.Combine(parent, $"{Path.GetFileName(entry.Path)}.tar.gz");
        var result = new SafeProcessLauncher().Launch("tar", ["-czf", archivePath, "--", entry.Path]);
        if (result.Succeeded)
        {
            (Application.Current as App)?.Feedback.Show(Loc.Get("Creating archive…"));

        }
        else
        {
            (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t create the archive."));
        }
    }

    private static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".zip" or ".tar" or ".gz" or ".xz" or ".7z" or ".bz2" or ".tgz" ||
               path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase);
    }

    private async void OnExtractHereContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is not { } entry) return;
        var parent = Path.GetDirectoryName(entry.Path);
        if (string.IsNullOrWhiteSpace(parent)) return;

        await ExtractArchiveAsync(entry.Path, parent);
    }

    private async void OnExtractToFolderContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is not { } entry) return;
        var parent = Path.GetDirectoryName(entry.Path);
        if (string.IsNullOrWhiteSpace(parent)) return;

        var folderName = Path.GetFileNameWithoutExtension(entry.Path);
        if (folderName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
        {
            folderName = Path.GetFileNameWithoutExtension(folderName);
        }
        var targetDir = Path.Combine(parent, folderName);
        await ExtractArchiveAsync(entry.Path, targetDir);
    }

    private async Task ExtractArchiveAsync(string archivePath, string destinationDir)
    {
        using var busyCursor = DaisyOS.System.Processes.BusyCursor.Begin();
        (Application.Current as App)?.Feedback.Show(Loc.Get("Extracting archive…"));
        try
        {
            var succeeded = await DaisyOS.Shell.Apps.Files.Services.ArchiveExtractionService.ExtractAsync(
                archivePath, destinationDir, new DaisyOS.System.Processes.SafeCommandRunner());
            (Application.Current as App)?.Feedback.Show(succeeded
                ? Loc.Get("Archive extracted successfully.")
                : Loc.Get("Couldn’t extract the archive. It may be damaged or its format may not be supported."));

        }
        catch (Exception ex)
        {
            (Application.Current as App)?.Feedback.Show(DaisyOS.Shell.Apps.Files.Services.FilesErrorMessages.Describe("Couldn’t extract the archive", ex));
        }
    }

    private async void OnVolumeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: VolumeInfo volume }) return;
        try
        {
            var path = await VolumeService.MountAsync(volume);
            _viewModel.RefreshVolumes();
            if (_viewModel.ActiveTab is { } tab) await tab.NavigateToAsync(path);
        }
        catch (Exception ex) { ReportFeedback(FilesErrorMessages.Describe("Couldn’t open drive", ex)); }
    }

    private async void OnEjectVolumeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: VolumeInfo volume })
        {
            var path = volume.Path;
            var result = await new DaisyOS.System.Processes.SafeCommandRunner().RunAsync("udisksctl", ["unmount", "--block-device", volume.DevicePath], TimeSpan.FromSeconds(5));
            if (!result.Succeeded)
            {
                result = await new DaisyOS.System.Processes.SafeCommandRunner().RunAsync("umount", [path], TimeSpan.FromSeconds(5));
            }

            (Application.Current as App)?.Feedback.Show(result.Succeeded ? Loc.Get("Drive safely removed.") : Loc.Get("Couldn’t unmount drive."));
            _viewModel.RefreshVolumes();
        }
    }

    private void OnShowPropertiesContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is { } entry)
        {
            SelectEntry(entry);
            _viewModel.IsPreviewPaneVisible = true;
        }
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: FileEntryViewModel entry }) return;
        if (e.Key == Key.Escape)
        {
            entry.Name = Path.GetFileName(entry.Path);
            entry.IsRenaming = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            _ = CommitRenameAsync(entry);
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: FileEntryViewModel entry } && entry.IsRenaming)
        {
            _ = CommitRenameAsync(entry);
        }
    }

    private async Task CommitRenameAsync(FileEntryViewModel entry)
    {
        if (!entry.IsRenaming)
        {
            return;
        }

        entry.IsRenaming = false;
        var originalPath = entry.Path;
        var newName = entry.Name.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == Path.GetFileName(originalPath))
        {
            entry.Name = Path.GetFileName(originalPath);
            return;
        }

        if (newName.IndexOf(Path.DirectorySeparatorChar) >= 0)
        {
            entry.Name = Path.GetFileName(originalPath);
            (Application.Current as App)?.Feedback.Show(Loc.Get("A name can’t contain a folder separator."));
            return;
        }

        var parent = Path.GetDirectoryName(originalPath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            entry.Name = Path.GetFileName(originalPath);
            return;
        }

        var tab = _viewModel.ActiveTab;
        try
        {
            if (tab is null || !await tab.RenameAsync(entry, newName))
            {
                entry.Name = Path.GetFileName(originalPath);
                (Application.Current as App)?.Feedback.Show(Loc.Get("That name is already in use or can’t be used."));
                return;
            }


        }
        catch (Exception ex)
        {
            entry.Name = Path.GetFileName(originalPath);
            (Application.Current as App)?.Feedback.Show(DaisyOS.Shell.Apps.Files.Services.FilesErrorMessages.Describe("Couldn’t rename that item", ex));
        }
    }

    private async void OnCutEntryContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is { } entry)
        {
            SelectEntry(entry);
            await _viewModel.ActiveTab!.CutSelectedAsync();
        }
    }

    private async void OnCopyEntryContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is { } entry)
        {
            SelectEntry(entry);
            await _viewModel.ActiveTab!.CopySelectedAsync();
        }
    }

    private async void OnCopyEntryPathContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is { } entry)
        {
            await CopyTextAsync(entry.Path, "Path copied.");
        }
    }

    private async void OnTrashEntryContextClicked(object? sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is { } entry)
        {
            SelectEntry(entry);
            await _viewModel.ActiveTab!.DeleteSelectedAsync();
        }
    }

    private async void OnCopyCurrentLocationClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab is not null)
        {
            await CopyTextAsync(_viewModel.ActiveTab.CurrentPath, "Location copied.");
        }
    }

    private void OpenEntry(FileEntryViewModel entry)
    {
        SelectEntry(entry);
        if (entry.IsDirectory)
        {
            _ = _viewModel.ActiveTab?.NavigateToAsync(entry.Path);
            return;
        }

        if (string.Equals(Path.GetExtension(entry.Path), ".AppImage", StringComparison.OrdinalIgnoreCase))
        {
            var appImageResult = DaisyOS.Shell.Services.PackagedApplicationLauncher.Launch("AppInstaller", [entry.Path]);
            if (!appImageResult.Succeeded)
            {
                (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t open that file."));
            }
            return;
        }

        var result = new SafeProcessLauncher().Launch("xdg-open", [entry.Path]);
        if (!result.Succeeded)
        {
            (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t open that file."));
        }
    }

    private FileEntryViewModel? GetContextEntry(object? sender)
    {
        if (sender is Control control)
        {
            if (control.DataContext is FileEntryViewModel entry)
            {
                return entry;
            }

            if (control.Parent is ContextMenu contextMenu && contextMenu.PlacementTarget?.DataContext is FileEntryViewModel targetEntry)
            {
                return targetEntry;
            }

            var ancestorContextMenu = control.GetVisualAncestors().OfType<ContextMenu>().FirstOrDefault();
            if (ancestorContextMenu?.PlacementTarget?.DataContext is FileEntryViewModel visualEntry)
            {
                return visualEntry;
            }
        }

        return _viewModel.ActiveTab?.SelectedEntry;
    }

    private void SelectEntry(FileEntryViewModel entry)
    {
        var tab = _viewModel.ActiveTab;
        if (tab is null || tab.SelectedItems.Contains(entry))
        {
            return;
        }

        tab.SelectedItems.Clear();
        tab.SelectedItems.Add(entry);
    }

    private IEnumerable<FileEntryViewModel> SelectedEntriesForDrag()
    {
        var tab = _viewModel.ActiveTab;
        if (tab is not null && tab.SelectedItems.Count > 0)
        {
            return tab.SelectedItems.ToArray();
        }

        return _fileDragSource is null ? [] : [_fileDragSource];
    }

    private async Task CopyTextAsync(string text, string confirmation)
    {
        var clipboard = Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(text);
        (Application.Current as App)?.Feedback.Show(confirmation);
    }

    private void ResetFileDrag()
    {
        _fileDragPress?.Pointer.Capture(null);
        _draggedPaths = [];
        _fileDragPress = null;
        _fileDragSource = null;
        _fileDragInProgress = false;
    }

    private void OnTabSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Button tab) return;
        foreach (var child in tab.GetVisualDescendants())
        {
            if (child is DaisyOS.Shell.Controls.DaisyIcon icon && icon.Classes.Contains("TabLocationIcon"))
                icon.IsVisible = e.NewSize.Width >= 88;
            else if (child is TextBlock label && label.Classes.Contains("TabLabel"))
                label.IsVisible = e.NewSize.Width >= 48;
        }
    }

    private void OnTabRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            (e.Source is Visual source && IsInteractiveControl(source)))
        {
            return;
        }

        SystemAppWindowChrome.HandleTitleBarPress(this, e);
    }

    private static bool IsInteractiveControl(Visual source)
    {
        return source is Button || source is TextBox ||
               source.GetVisualAncestors().Any(v => v is Button || v is TextBox);
    }

    private void OnMinimizeWindowClicked(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreWindowClicked(object? sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void OnCloseWindowClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateCaptionState()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeRestoreIcon.Kind = isMaximized ? DaisyOS.Shell.Controls.WindowCaptionKind.Restore : DaisyOS.Shell.Controls.WindowCaptionKind.Maximize;
        ToolTip.SetTip(MaximizeRestoreButton, isMaximized ? Loc.Get("Restore") : Loc.Get("Maximize"));
    }

    private void ToggleMaximizeRestore()
    {
        SystemAppWindowChrome.ToggleMaximizeRestore(this);
    }

    private sealed record FileDragPayload(string[] Paths);
}
