using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Avalonia.Threading;
using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.Apps.Files.Models;
using DaisyOS.Shell.Apps.Files.Collections;
using global::System.IO.Enumeration;
using Directory = global::System.IO.Directory;
using File = global::System.IO.File;
using Path = global::System.IO.Path;

namespace DaisyOS.Shell.Apps.Files;

public record BreadcrumbSegment(string Name, string Path, bool IsFirst, bool IsCurrent, bool IsOverflow = false);

public sealed class FilesTabViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly SemaphoreSlim SortGate = new(1, 1);
    private static readonly StringComparer NameComparer = StringComparer.CurrentCultureIgnoreCase;
    private string _currentPath = string.Empty;
    private string _title = "New Tab";
    private string _searchQuery = string.Empty;
    private string _sortColumn = "Name";
    private bool _sortAscending = true;
    private bool _isGridView;
    private bool _isActive;
    private bool _isLoading;
    private DaisyOS.System.Processes.BusyCursor? _loadingCursor;
    private bool _showHiddenFiles;
    private string _statusText = string.Empty;
    private string? _pendingSelectionPath;
    private FileEntryViewModel? _selectedEntry;
    private bool _isOperationInProgress;
    private string _operationStatus = string.Empty;
    private bool _hasUserSelectedViewMode;
    private List<TrashOperation> _lastTrashOperations = [];
    private bool _showDateColumn = true;
    private bool _showTypeColumn = true;
    private bool _showSizeColumn = true;

    private readonly DirectorySnapshotService _snapshots;
    private readonly IFileUiDispatcher _ui;
    private readonly bool _watchFiles;
    private readonly DirectoryMutationCoordinator _mutations;
    private readonly RecentDirectoryCache _recent = new();
    private long _generation;
    private long _viewGeneration;
    private CancellationTokenSource? _viewCts;
    private bool _disposed;
    public DirectorySession? CurrentSession { get; private set; }
    public Task NavigationTask { get; private set; } = Task.CompletedTask;
    public Task ViewUpdateTask { get; private set; } = Task.CompletedTask;
    public long EnumerationCount => _snapshots.EnumerationCount;
    public event Action<FileEntryViewModel>? RenameRequested;
    public event Action? ProjectionChanging;
    public event Action? ProjectionChanged;
    public event Action? EntriesChanged;


    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                _loadingCursor?.Dispose();
                _loadingCursor = value && !_disposed ? DaisyOS.System.Processes.BusyCursor.Begin() : null;
                NotifyEmptyState();
                UpdateStatusText();
            }
        }
    }

    private readonly Stack<string> _history = new();
    private readonly Stack<string> _forwardHistory = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? PreferencesChanged;
    internal Avalonia.Input.Platform.IClipboard? SystemClipboard { get; set; }
    internal Avalonia.Platform.Storage.IStorageProvider? StorageProvider { get; set; }

    public IReadOnlyList<FileEntryViewModel> Entries => CurrentSession?.Index.Entries ?? Array.Empty<FileEntryViewModel>();
    public FastObservableCollection<FileEntryViewModel> FilteredEntries { get; } = new();
    
    public ObservableCollection<FileEntryViewModel> SelectedItems { get; } = new();
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = new();
    private IReadOnlyList<BreadcrumbSegment> _visibleBreadcrumbs = Array.Empty<BreadcrumbSegment>();
    public IReadOnlyList<BreadcrumbSegment> VisibleBreadcrumbs
    {
        get => _visibleBreadcrumbs;
        private set { if (!_visibleBreadcrumbs.SequenceEqual(value)) SetProperty(ref _visibleBreadcrumbs, value); }
    }

    internal void SetVisibleBreadcrumbs(int suffixCount, bool includeOverflow)
    {
        var visible = Breadcrumbs.Skip(Math.Max(0, Breadcrumbs.Count - suffixCount)).ToList();
        if (includeOverflow) visible.Insert(0, new BreadcrumbSegment("…", "/", true, false, IsOverflow: true));
        VisibleBreadcrumbs = visible.Select((segment, index) => segment with { IsFirst = index == 0 }).ToArray();
    }

    public FileEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        private set => SetProperty(ref _selectedEntry, value);
    }

    public bool HasSelectedEntry => SelectedEntry is not null;

    public bool IsEmpty => !IsLoading && FilteredEntries.Count == 0;
    private string? _loadError;
    public string? LoadError
    {
        get => _loadError;
        private set { if (SetProperty(ref _loadError, value)) NotifyEmptyState(); }
    }
    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);
    public bool HasHiddenEntriesOnly => !HasSearchQuery && !ShowHiddenFiles && Entries.Count > 0 && FilteredEntries.Count == 0;
    private bool IsTrashLocation => FileLocationPresentation.Resolve(CurrentPath).Kind == FileLocationKind.Trash;
    public string EmptyTitle => HasLoadError ? Loc.Get("Couldn’t open this folder")
        : HasSearchQuery ? Loc.Get("No matching files")
        : HasHiddenEntriesOnly ? Loc.Get("No visible files") : IsTrashLocation ? Loc.Get("Trash is empty") : Loc.Get("This folder is empty");
    public string EmptyMessage => LoadError ?? (HasSearchQuery ? Loc.Get("Try a different search or clear the filter.")
        : HasHiddenEntriesOnly ? Loc.Get("Show hidden files to see the items in this folder.") : string.Empty);
    public bool ShowSearchClearInStatusBar => HasSearchQuery && !IsEmpty;
    public bool HasEmptyMessage => EmptyMessage.Length > 0;
    public bool HasEmptyAction => HasLoadError || HasSearchQuery || HasHiddenEntriesOnly || !IsTrashLocation;
    public string EmptyIcon => HasLoadError ? "warning-circle" : HasSearchQuery ? "magnifying-glass" : IsTrashLocation ? "trash" : "folder-open";
    public string EmptyActionLabel => HasLoadError ? Loc.Get("Try again") : HasSearchQuery ? Loc.Get("Clear search") : HasHiddenEntriesOnly ? Loc.Get("Show hidden files") : Loc.Get("New folder");
    public string EmptyActionIcon => HasLoadError ? "arrow-clockwise" : HasSearchQuery ? "x" : HasHiddenEntriesOnly ? "eye" : "folder-plus";

    private void NotifyEmptyState()
    {
        foreach (var name in new[] { nameof(IsEmpty), nameof(ShowSearchClearInStatusBar), nameof(HasLoadError), nameof(HasHiddenEntriesOnly), nameof(EmptyTitle), nameof(EmptyMessage),
            nameof(HasEmptyMessage), nameof(HasEmptyAction), nameof(EmptyIcon), nameof(EmptyActionLabel), nameof(EmptyActionIcon) })
            OnPropertyChanged(name);
    }

    public bool IsSortByName => string.Equals(SortColumn, "Name", StringComparison.OrdinalIgnoreCase);
    public bool IsSortByDate => string.Equals(SortColumn, "Date", StringComparison.OrdinalIgnoreCase);
    public bool IsSortByType => string.Equals(SortColumn, "Type", StringComparison.OrdinalIgnoreCase);
    public bool IsSortBySize => string.Equals(SortColumn, "Size", StringComparison.OrdinalIgnoreCase);

    public bool IsOperationInProgress
    {
        get => _isOperationInProgress;
        private set
        {
            if (SetProperty(ref _isOperationInProgress, value))
            {
                OnPropertyChanged(nameof(ShowIdleOperationStatus));
            }
        }
    }

    public string OperationStatus
    {
        get => _operationStatus;
        private set
        {
            if (SetProperty(ref _operationStatus, value))
            {
                OnPropertyChanged(nameof(HasOperationStatus));
                OnPropertyChanged(nameof(ShowIdleOperationStatus));
            }
        }
    }

    public bool HasOperationStatus => !string.IsNullOrWhiteSpace(OperationStatus);

    /// <summary>
    /// True only when there's a status message to show AND no operation is
    /// actively running. While an operation is in progress, the progress-bar
    /// row owns this text instead — showing it in both places at once made
    /// the status bar display the same message twice.
    /// </summary>
    public bool ShowIdleOperationStatus => HasOperationStatus && !IsOperationInProgress;

    public bool CanUndoTrash => _lastTrashOperations.Count > 0;

    public bool ShowDateColumn
    {
        get => _showDateColumn;
        private set
        {
            if (SetProperty(ref _showDateColumn, value))
            {
                PreferencesChanged?.Invoke();
            }
        }
    }

    public bool ShowTypeColumn
    {
        get => _showTypeColumn;
        private set
        {
            if (SetProperty(ref _showTypeColumn, value))
            {
                PreferencesChanged?.Invoke();
            }
        }
    }

    public bool ShowSizeColumn
    {
        get => _showSizeColumn;
        private set
        {
            if (SetProperty(ref _showSizeColumn, value))
            {
                PreferencesChanged?.Invoke();
            }
        }
    }

    public string CurrentPath
    {
        get => _currentPath;
        private set
        {
            if (SetProperty(ref _currentPath, value))
            {
                UpdateTitle();
                OnPropertyChanged(nameof(TabIcon));
                OnPropertyChanged(nameof(TabIconColorResourceKey));
                UpdateBreadcrumbs();
                OnPropertyChanged(nameof(CanGoUp));
            }
        }
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string TabIcon => FileLocationPresentation.Resolve(CurrentPath).Icon;
    public string TabIconColorResourceKey => FileLocationPresentation.Resolve(CurrentPath).ColorResourceKey;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                OnPropertyChanged(nameof(HasSearchQuery));
                NotifyEmptyState();
                ApplyFilterAndSort();
            }
        }
    }

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

    public string SortColumn
    {
        get => _sortColumn;
        set
        {
            if (_sortColumn == value)
            {
                SortAscending = !SortAscending;
            }
            else
            {
                _sortColumn = value;
                _sortAscending = true;
                OnPropertyChanged(nameof(SortColumn));
            }
            OnPropertyChanged(nameof(IsSortByName));
            OnPropertyChanged(nameof(IsSortByDate));
            OnPropertyChanged(nameof(IsSortByType));
            OnPropertyChanged(nameof(IsSortBySize));
            OnPropertyChanged(nameof(SortArrowIcon));
            ApplyFilterAndSort();
            PreferencesChanged?.Invoke();
        }
    }

    /// <summary>Direction glyph shown on the active column header.</summary>
    public string SortArrowIcon => _sortAscending ? "arrow-up" : "arrow-down";

    public bool SortAscending
    {
        get => _sortAscending;
        set
        {
            if (SetProperty(ref _sortAscending, value))
            {
                OnPropertyChanged(nameof(SortArrowIcon));
                ApplyFilterAndSort();
                PreferencesChanged?.Invoke();
            }
        }
    }

    public bool IsGridView
    {
        get => _isGridView;
        set
        {
            if (SetProperty(ref _isGridView, value))
            {
                PreferencesChanged?.Invoke();
            }
        }
    }

    public bool ShowHiddenFiles
    {
        get => _showHiddenFiles;
        set
        {
            if (SetProperty(ref _showHiddenFiles, value))
            {
                ApplyFilterAndSort();
                PreferencesChanged?.Invoke();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool CanGoBack => _history.Count > 0;
    public bool CanGoForward => _forwardHistory.Count > 0;
    public bool CanGoUp => !string.IsNullOrEmpty(CurrentPath) && Directory.GetParent(CurrentPath) != null;

    public FilesTabViewModel(string initialPath, FilesPreferences? initialPreferences = null, DirectorySnapshotService? snapshots = null, bool navigateImmediately = true, IFileUiDispatcher? uiDispatcher = null, bool watchFiles = true)
    {
        _snapshots = snapshots ?? new DirectorySnapshotService();
        _ui = uiDispatcher ?? new AvaloniaFileUiDispatcher();
        _watchFiles = watchFiles;
        Loc.Changed += OnLanguageChanged;
        _mutations = new DirectoryMutationCoordinator(_snapshots, () => CurrentSession, ApplyDelta, _ui);
        if (initialPreferences is not null)
        {
            _isGridView = initialPreferences.IsGridView;
            _sortColumn = string.IsNullOrWhiteSpace(initialPreferences.SortColumn) ? "Name" : initialPreferences.SortColumn;
            _sortAscending = initialPreferences.SortAscending;
            _showHiddenFiles = initialPreferences.ShowHiddenFiles;
            _showDateColumn = initialPreferences.ShowDateColumn;
            _showTypeColumn = initialPreferences.ShowTypeColumn;
            _showSizeColumn = initialPreferences.ShowSizeColumn;
            _hasUserSelectedViewMode = true;
        }

        SelectedItems.CollectionChanged += (s, e) =>
        {
            SelectedEntry = SelectedItems.FirstOrDefault();
            OnPropertyChanged(nameof(HasSelectedEntry));
            UpdateStatusText();
        };
        if (navigateImmediately) NavigationTask = NavigateToAsync(initialPath, addHistory: false);
    }

    private void UpdateBreadcrumbs()
    {
        Breadcrumbs.Clear();
        if (string.IsNullOrEmpty(CurrentPath)) return;

        var parts = CurrentPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        
        string currentBuiltPath = "/";
        var segments = new List<(string Name, string Path)> { ("Root", currentBuiltPath) };

        foreach (var part in parts)
        {
            currentBuiltPath = Path.Combine(currentBuiltPath, part);
            segments.Add((part, currentBuiltPath));
        }

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            Breadcrumbs.Add(new BreadcrumbSegment(
                segment.Name,
                segment.Path,
                IsFirst: index == 0,
                IsCurrent: index == segments.Count - 1));
        }
        SetVisibleBreadcrumbs(Breadcrumbs.Count, false);
    }

    public async Task NavigateToAsync(string path, bool addHistory = true, string? selectPath = null)
    {
        if (_disposed) return;
        DirectorySession? session = null;
        try
        {
            var fullPath = DirectoryIndex.Normalize(path);
            if (addHistory && fullPath != CurrentPath)
            {
                if (!string.IsNullOrEmpty(CurrentPath)) _history.Push(CurrentPath);
                _forwardHistory.Clear();
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoForward));
            }
            var previous = CurrentSession;
            if (previous is { IsReady: true }) _recent.Store(previous.Path, previous.Index);
            previous?.Dispose();
            _viewCts?.Cancel();
            CurrentPath = fullPath;
            if (!_hasUserSelectedViewMode) IsGridView = UsesMediaLayout(fullPath);
            session = new DirectorySession(fullPath, ++_generation,
                () => IsCurrent(session!), events => _ = HandleWatcherAsync(session!, events), _ui, _watchFiles);
            CurrentSession = session;
            _pendingSelectionPath = selectPath;
            SelectedItems.Clear();
            IsLoading = true;
            LoadError = null;
            if (FilteredEntries.Count > 0) FilteredEntries.ReplaceAll([]);
            NotifyEmptyState();
            var cached = _recent.Take(fullPath);
            if (cached != null)
            {
                var cachedOptions = GetViewOptions();
                var cachedEntries = cached.Entries.ToArray();
                var visible = await Task.Run(() => SortedVisible(cachedEntries, cachedOptions, session.Performance, session.Token), session.Token);
                await _ui.InvokeAsync(() =>
                {
                    if (!IsCurrent(session)) return;
                    session.Index = cached;
                    session.IsReady = true;
                    if (cachedOptions == GetViewOptions()) ReplaceProjection(visible);
                    else ApplyFilterAndSort();
                    IsLoading = !session.ProjectionReady;
                    SelectPendingEntry();
                    foreach (var mutation in session.BufferedMutations.ToArray()) ApplyDelta(session, mutation);
                    session.BufferedMutations.Clear();
                    CompletePendingRename(session);
                    OnPropertyChanged(nameof(Entries));
                    EntriesChanged?.Invoke();
                });
                await ReconcileAsync(session);
                if (IsCurrent(session)) session.Watcher.Activate();
                return;
            }
            var options = GetViewOptions();
            IReadOnlyList<FileEntrySnapshot> snapshot;
            using (session.Performance.Measure("Enumeration"))
                snapshot = await _snapshots.EnumerateAsync(fullPath, session.Token);
            if (Environment.GetEnvironmentVariable("DAISYOS_FILES_PERF") == "1")
                session.Performance.Record("Snapshot projection (within enumeration)", _snapshots.LastSnapshotProjectionMilliseconds);
            session.Performance.Count("Entries processed", snapshot.Count);
            var prepared = await Task.Run(() =>
            {
                var index = new DirectoryIndex();
                using (session.Performance.Measure("VM creation"))
                    foreach (var entry in snapshot)
                    {
                        session.Token.ThrowIfCancellationRequested();
                        index.Upsert(entry);
                    }
                FileEntryViewModel[] visible;
                using (session.Performance.Measure("Projection"))
                    visible = SortedVisible(index.Entries.ToArray(), options, session.Performance, session.Token);
                return (Index: index, Visible: visible);
            }, session.Token);
            await _ui.InvokeAsync(() =>
            {
                if (!IsCurrent(session)) return;
                session.Index = prepared.Index;
                session.IsReady = true;
                if (options == GetViewOptions()) ReplaceProjection(prepared.Visible);
                else ApplyFilterAndSort();
                IsLoading = !session.ProjectionReady;
                SelectPendingEntry();
                foreach (var mutation in session.BufferedMutations.ToArray()) ApplyDelta(session, mutation);
                session.BufferedMutations.Clear();
                CompletePendingRename(session);
                OnPropertyChanged(nameof(Entries));
                EntriesChanged?.Invoke();
                session.Watcher.Activate();
                session.Performance.Emit();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await _ui.InvokeAsync(() =>
            {
                if (session != null && IsCurrent(session))
                {
                    LoadError = FilesErrorMessages.Describe("Couldn’t open this folder", ex);
                    IsLoading = false;
                    session.Dispose();
                }
            });
        }
    }

    private bool IsCurrent(DirectorySession session) => !_disposed && !session.IsDisposed && ReferenceEquals(CurrentSession, session);

    public async Task RefreshAsync()
    {
        if (HasLoadError && !IsLoading)
            await NavigateToAsync(CurrentPath, addHistory: false);
        else if (CurrentSession is { } session)
            await ReconcileAsync(session);
    }

    private async Task ReconcileAsync(DirectorySession session)
    {
        if (!IsCurrent(session) || !session.IsReady) return;
        if (session.ReconciliationRunning) { session.ReconciliationRequested = true; return; }
        session.ReconciliationRunning = true;
        try
        {
            do
            {
                session.ReconciliationRequested = false;
                var revision = session.Index.Revision;
                var old = session.Index.Entries.Select(e => e.Snapshot).ToArray();
                IReadOnlyList<FileEntrySnapshot> fresh;
                using (session.Performance.Measure("Reconciliation enumeration"))
                    fresh = await _snapshots.EnumerateAsync(session.Path, session.Token);
                var delta = await Task.Run(() => DirectoryReconciler.Diff(old, fresh), session.Token);
                await _ui.InvokeAsync(() =>
                {
                    if (!IsCurrent(session)) return;
                    if (session.Index.Revision != revision) { session.ReconciliationRequested = true; return; }
                    ApplyDelta(session, delta);
                });
            } while (IsCurrent(session) && session.ReconciliationRequested);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await _ui.InvokeAsync(() => { if (IsCurrent(session)) OperationStatus = FilesErrorMessages.Describe("Couldn’t update this folder", ex); });
        }
        finally { session.ReconciliationRunning = false; }
    }

    private async Task HandleWatcherAsync(DirectorySession session, IReadOnlyList<DirectoryEvent> events)
    {
        if (!IsCurrent(session)) return;
        var token = session.Token;
        try
        {
            await session.WatcherGate.WaitAsync(token);
            try
            {
                if (!IsCurrent(session)) return;
                if (events.Any(e => e.Kind == DirectoryEventKind.Failure))
                {
                    // Restart before the snapshot so changes during recovery are buffered/live.
                    session.Watcher.Restart();
                    await _ui.InvokeAsync(() => { if (IsCurrent(session)) _ = ReconcileAsync(session); });
                    return;
                }
                using (session.Performance.Measure("Watcher application"))
                {
                    var delta = await FilesIoScheduler.ReadMetadataAsync(() =>
                    {
                        List<FileEntrySnapshot> changed = [];
                        List<string> removed = [];
                        List<FileRenameDelta> renames = [];
                        foreach (var e in events)
                        {
                            token.ThrowIfCancellationRequested();
                            if (e.Kind == DirectoryEventKind.Rename && e.OldPath != null)
                            {
                                if (_snapshots.Read(e.Path) is { } renamed) renames.Add(new(e.OldPath, renamed));
                                else { removed.Add(e.OldPath); removed.Add(e.Path); }
                            }
                            else if (_snapshots.Read(e.Path) is { } entry) changed.Add(entry);
                            else removed.Add(e.Path);
                        }
                        return new DirectoryDelta([], removed, renames, changed);
                    }, token);
                    await _ui.InvokeAsync(() => { if (IsCurrent(session)) ApplyDelta(session, delta); });
                }
            }
            finally { session.WatcherGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await _ui.InvokeAsync(() => { if (IsCurrent(session)) { OperationStatus = FilesErrorMessages.Describe("Couldn’t follow changes in this folder", ex); _ = ReconcileAsync(session); } });
        }
    }

    public Task ReconcilePathsAsync(IEnumerable<string> paths) => _mutations.PublishPathsAsync(CurrentSession, paths);

    public void GoBack()
    {
        if (_history.TryPop(out var previousPath))
        {
            _forwardHistory.Push(CurrentPath);
            _ = NavigateToAsync(previousPath, addHistory: false);
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        }
    }

    public void GoForward()
    {
        if (_forwardHistory.TryPop(out var nextPath))
        {
            _history.Push(CurrentPath);
            _ = NavigateToAsync(nextPath, addHistory: false);
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        }
    }

    public void GoUp()
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent != null)
        {
            _ = NavigateToAsync(parent.FullName);
        }
    }

    public void Refresh() => _ = RefreshAsync();

    public void Reveal(string path, bool selectItem)
    {
        if (Directory.Exists(path))
        {
            _ = NavigateToAsync(path);
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            if (selectItem && Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal))
            {
                ShowHiddenFiles = true;
            }
            _ = NavigateToAsync(directory, selectPath: selectItem ? path : null);
        }
    }

    public async Task CreateNewFolderAsync()
    {
        var session = CurrentSession;
        if (session == null) return;
        using var writePermit = await FilesOperationLimiter.EnterAsync();
        using var timing = session.Performance.Measure("Create mutation");
        try
        {
            var path = await Task.Run(() =>
            {
                var name = "New Folder";
                int count = 1;
                while (Directory.Exists(Path.Combine(session.Path, name)) || File.Exists(Path.Combine(session.Path, name))) name = $"New Folder ({count++})";
                var result = Path.Combine(session.Path, name);
                Directory.CreateDirectory(result);
                return result;
            });
            await _mutations.PublishPathsAsync(session, [path]);
            RequestRename(session, path);
        }
        catch (Exception ex) { OperationStatus = FilesErrorMessages.Describe("Couldn’t create the folder", ex); }
    }
    public async Task CreateNewDocumentAsync()
    {
        var session = CurrentSession;
        if (session == null) return;
        using var writePermit = await FilesOperationLimiter.EnterAsync();
        using var timing = session.Performance.Measure("Create mutation");
        try
        {
            var path = await Task.Run(() =>
            {
                var name = "New Text Document.txt";
                int count = 1;
                while (File.Exists(Path.Combine(session.Path, name)) || Directory.Exists(Path.Combine(session.Path, name))) name = $"New Text Document ({count++}).txt";
                var result = Path.Combine(session.Path, name);
                using var file = new global::System.IO.FileStream(result, global::System.IO.FileMode.CreateNew);
                return result;
            });
            await _mutations.PublishPathsAsync(session, [path]);
            RequestRename(session, path);
        }
        catch (Exception ex) { OperationStatus = FilesErrorMessages.Describe("Couldn’t create the file", ex); }
    }
    private void CompletePendingRename(DirectorySession session)
    {
        if (session.PendingRenamePath is not { } path) return;
        session.PendingRenamePath = null;
        RequestRename(session, path);
    }
    private void RequestRename(DirectorySession session, string path)
    {
        if (IsCurrent(session) && (!session.IsReady || !session.ProjectionReady)) { session.PendingRenamePath = path; return; }
        if (!IsCurrent(session) || session.Index.Find(path) is not { } entry || !FilteredEntries.Contains(entry)) return;
        SelectedItems.Clear();
        SelectedItems.Add(entry);
        entry.IsRenaming = true;
        RenameRequested?.Invoke(entry);
    }
    public async Task<bool> RenameAsync(FileEntryViewModel entry, string newName)
    {
        var session = CurrentSession;
        if (session == null || !ReferenceEquals(session.Index.Find(entry.Path), entry) ||
            string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 || newName is "." or "..") return false;
        var oldPath = entry.Path;
        var newPath = Path.Combine(session.Path, newName);
        using var writePermit = await FilesOperationLimiter.EnterAsync();
        using var timing = session.Performance.Measure("Rename mutation");
        if (!await FileSystemService.RenameAsync(oldPath, newName)) return false;
        await _mutations.PublishPathsAsync(session, [], renamed: [(oldPath, newPath)]);
        return true;
    }
    public async Task DeleteSelectedAsync()
    {
        var session = CurrentSession;
        var items = SelectedItems.Where(e => ReferenceEquals(session?.Index.Find(e.Path), e)).Select(e => e.Path).ToArray();
        if (session == null || items.Length == 0) return;
        using var writePermit = await FilesOperationLimiter.EnterAsync();
        using var timing = session.Performance.Measure("Trash mutation");
        IsOperationInProgress = true;
        List<TrashOperation> trashed = [];
        List<string> removed = [];
        try
        {
            for (var index = 0; index < items.Length; index++)
            {
                if (index % 32 == 0) OperationStatus = Loc.Format("Moving {0} of {1} to Trash…", index + 1, items.Length);
                var operation = await FileSystemService.MoveToTrashWithUndoAsync(items[index]);
                if (operation == null) continue;
                trashed.Add(operation);
                removed.Add(operation.OriginalPath);
                if (removed.Count >= 64) { await _mutations.PublishPathsAsync(session, [], removed); removed.Clear(); }
            }
            OperationStatus = Loc.Format("Moved {0} of {1} items to Trash.", trashed.Count, items.Length);
        }
        catch (Exception ex) { OperationStatus = FilesErrorMessages.Describe("Couldn’t move some items to Trash", ex); }
        finally
        {
            await _mutations.PublishPathsAsync(session, [], removed);
            _lastTrashOperations = trashed;
            OnPropertyChanged(nameof(CanUndoTrash));
            IsOperationInProgress = false;
        }
    }
    public async Task DeletePermanentlySelectedAsync()
    {
        var session = CurrentSession;
        var paths = SelectedItems.Where(e => ReferenceEquals(session?.Index.Find(e.Path), e)).Select(e => e.Path).ToArray();
        if (session == null) return;
        using var writePermit = await FilesOperationLimiter.EnterAsync();
        using var timing = session.Performance.Measure("Delete mutation");
        List<string> attempted = [];
        foreach (var path in paths)
        {
            await FileSystemService.DeletePermanentlyAsync(path);
            attempted.Add(path);
            if (attempted.Count >= 64) { await _mutations.PublishPathsAsync(session, attempted); attempted.Clear(); }
        }
        // Query only attempted paths: failed deletes remain in the index.
        await _mutations.PublishPathsAsync(session, attempted);
    }
    public async Task UndoTrashAsync()
    {
        var session = CurrentSession;
        if (session == null || _lastTrashOperations.Count == 0) return;
        using var writePermit = await FilesOperationLimiter.EnterAsync();
        using var timing = session.Performance.Measure("Restore mutation");
        IsOperationInProgress = true;
        List<string> restored = [];
        int restoredCount = 0;
        List<TrashOperation> failed = [];
        try
        {
            foreach (var operation in _lastTrashOperations.ToArray())
            {
                if (await FileSystemService.RestoreFromTrashAsync(operation))
                {
                    restoredCount++;
                    restored.Add(operation.OriginalPath);
                    if (restored.Count >= 64) { await _mutations.PublishPathsAsync(session, restored); restored.Clear(); }
                }
                else failed.Add(operation);
            }
            await _mutations.PublishPathsAsync(session, restored);
            _lastTrashOperations = failed;
            OnPropertyChanged(nameof(CanUndoTrash));
            OperationStatus = Loc.Format("Restored {0} items.", restoredCount);
        }
        catch (Exception ex) { OperationStatus = FilesErrorMessages.Describe("Couldn’t restore some items", ex); }
        finally { IsOperationInProgress = false; }
    }

    public async Task CopySelectedAsync()
    {
        var paths = SelectedItems.Select(x => x.Path).ToList();
        if (!paths.Any())
        {
            return;
        }

        try
        {
            await ClipboardService.SetFilesAsync(paths, ClipboardOperation.Copy, SystemClipboard, StorageProvider);
            OperationStatus = paths.Count == 1 ? Loc.Get("Copied to clipboard.") : Loc.Format("Copied {0} items to clipboard.", paths.Count);
        }
        catch (Exception ex)
        {
            OperationStatus = FilesErrorMessages.Describe("Couldn’t copy to the clipboard", ex);
        }
    }

    public async Task CutSelectedAsync()
    {
        var paths = SelectedItems.Select(x => x.Path).ToList();
        if (!paths.Any())
        {
            return;
        }

        try
        {
            await ClipboardService.SetFilesAsync(paths, ClipboardOperation.Cut, SystemClipboard, StorageProvider);
            OperationStatus = paths.Count == 1 ? Loc.Get("Ready to move.") : Loc.Format("Ready to move {0} items.", paths.Count);
        }
        catch (Exception ex)
        {
            OperationStatus = FilesErrorMessages.Describe("Couldn’t cut to the clipboard", ex);
        }
    }

    public async Task PasteAsync(ConflictResolution conflictResolution = ConflictResolution.Skip)
    {
        var session = CurrentSession;
        if (session == null) return;
        using var writePermit = await FilesOperationLimiter.EnterAsync();
        using var timing = session.Performance.Measure("Paste mutation");
        List<string> added = [], removed = [];
        List<string> files;
        ClipboardOperation op;
        try
        {
            (files, op) = await ClipboardService.GetFilesAsync(SystemClipboard);
        }
        catch (Exception ex)
        {
            OperationStatus = FilesErrorMessages.Describe("Couldn’t read the clipboard", ex);
            return;
        }

        if (!files.Any() || string.IsNullOrEmpty(session.Path) ||
            !await FilesIoScheduler.ReadMetadataAsync(() => Directory.Exists(session.Path), CancellationToken.None)) return;

        var completed = 0;
        var remaining = new List<string>(files);
        IsOperationInProgress = true;
        try
        {
            for (var index = 0; index < files.Count; index++)
            {
                var file = files[index];
                if (index % 32 == 0) OperationStatus = op == ClipboardOperation.Cut
                    ? Loc.Format("Moving {0} of {1}…", index + 1, files.Count)
                    : Loc.Format("Copying {0} of {1}…", index + 1, files.Count);
                var destination = await FilesIoScheduler.ReadMetadataAsync(() =>
                {
                    if ((!File.Exists(file) && !Directory.Exists(file)) || IsSameOrChildPath(file, session.Path))
                        return (Ready: false, Path: string.Empty, Exists: false);
                    var name = Path.GetFileName(file);
                    var target = Path.Combine(session.Path, name);
                    if (op == ClipboardOperation.Copy && string.Equals(Path.GetDirectoryName(file), session.Path, StringComparison.OrdinalIgnoreCase))
                        target = GenerateUniqueDuplicatePath(session.Path, name);
                    return (Ready: true, Path: target, Exists: File.Exists(target) || Directory.Exists(target));
                }, CancellationToken.None);
                if (!destination.Ready) continue;
                var destPath = destination.Path;
                if (destination.Exists && conflictResolution != ConflictResolution.Replace)
                {
                    if (conflictResolution == ConflictResolution.Cancel) break;
                    continue;
                }
                bool transferred;
                if (op == ClipboardOperation.Cut)
                {
                    if (IsSamePath(file, destPath)) continue;
                    transferred = await FileSystemService.MoveAsync(file, destPath, _ => Task.FromResult(conflictResolution));
                }
                else
                {
                    transferred = await FileSystemService.CopyAsync(file, destPath, _ => Task.FromResult(conflictResolution));
                }
                if (transferred)
                {
                    completed++;
                    remaining.Remove(file);
                    added.Add(destPath);
                    if (op == ClipboardOperation.Cut) removed.Add(file);
                    if (added.Count >= 64)
                    {
                        await _mutations.PublishPathsAsync(session, added, removed);
                        added.Clear(); removed.Clear();
                    }
                }
            }

            OperationStatus = op == ClipboardOperation.Cut
                ? Loc.Format("Moved {0} of {1} items.", completed, files.Count)
                : Loc.Format("Copied {0} of {1} items.", completed, files.Count);
        }
        catch (Exception ex)
        {
            OperationStatus = op == ClipboardOperation.Cut
                ? FilesErrorMessages.Describe("Couldn’t finish moving the files", ex)
                : FilesErrorMessages.Describe("Couldn’t finish copying the files", ex);
        }
        finally
        {
            await _mutations.PublishPathsAsync(session, added, removed);
            IsOperationInProgress = false;
        }

        // Preserve skipped/failed cut items for a subsequent paste. Never re-offer moved paths.
        if (op == ClipboardOperation.Cut && completed > 0)
        {
            try
            {
                var validRemaining = await FilesIoScheduler.ReadMetadataAsync(() => remaining.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray(), CancellationToken.None);
                await ClipboardService.SetFilesAsync(validRemaining, ClipboardOperation.Cut, SystemClipboard, StorageProvider);
                if (remaining.Count == 0) ClipboardService.ClearInternal();
            }
            catch (Exception ex) { OperationStatus += " " + FilesErrorMessages.Describe("The clipboard couldn’t be updated", ex); }
        }

    }

    private static string GenerateUniqueDuplicatePath(string folder, string originalName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(originalName);
        var ext = Path.GetExtension(originalName);
        var candidate = Path.Combine(folder, $"{nameWithoutExt} (copy){ext}");
        var counter = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(folder, $"{nameWithoutExt} (copy {counter}){ext}");
            counter++;
        }
        return candidate;
    }

    public async Task<int> TransferFilesAsync(IEnumerable<string> sourcePaths, string destinationDirectory, bool move)
    {
        var session = CurrentSession;
        var requestedSources = sourcePaths.ToArray();
        using var writePermit = await FilesOperationLimiter.EnterAsync();
        using var timing = session?.Performance.Measure("Transfer mutation");
        List<string> changed = [], removed = [];
        var sources = await FilesIoScheduler.ReadMetadataAsync(() => requestedSources
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.Ordinal).ToArray(), CancellationToken.None);

        if (sources.Length == 0 || !await FilesIoScheduler.ReadMetadataAsync(() => Directory.Exists(destinationDirectory), CancellationToken.None))
        {
            return 0;
        }

        IsOperationInProgress = true;
        try
        {
            var completed = 0;
            for (var index = 0; index < sources.Length; index++)
            {
                var sourcePath = sources[index];
                var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
                if (IsSamePath(sourcePath, destinationPath))
                {
                    continue;
                }

                if (index % 32 == 0) OperationStatus = move
                    ? Loc.Format("Moving {0} of {1}…", index + 1, sources.Length)
                    : Loc.Format("Copying {0} of {1}…", index + 1, sources.Length);

                // Existing files are never silently replaced by a drag operation.
                if (await FilesIoScheduler.ReadMetadataAsync(() => IsSameOrChildPath(sourcePath, destinationDirectory) || File.Exists(destinationPath) || Directory.Exists(destinationPath), CancellationToken.None))
                {
                    continue;
                }

                var succeeded = move
                    ? await FileSystemService.MoveAsync(sourcePath, destinationPath, _ => Task.FromResult(ConflictResolution.Skip))
                    : await FileSystemService.CopyAsync(sourcePath, destinationPath, _ => Task.FromResult(ConflictResolution.Skip));
                if (succeeded)
                {
                    completed++;
                    changed.Add(destinationPath);
                    if (move) removed.Add(sourcePath);
                    if (changed.Count >= 64)
                    {
                        await _mutations.PublishPathsAsync(session, changed, removed);
                        changed.Clear(); removed.Clear();
                    }
                }
            }

            OperationStatus = completed == 0
                ? Loc.Get("Nothing was transferred.")
                : move
                    ? (completed == 1 ? Loc.Get("Moved 1 item.") : Loc.Format("Moved {0} items.", completed))
                    : (completed == 1 ? Loc.Get("Copied 1 item.") : Loc.Format("Copied {0} items.", completed));
            return completed;
        }
        catch (Exception ex)
        {
            OperationStatus = FilesErrorMessages.Describe("Couldn’t finish transferring the files", ex);
            return 0;
        }
        finally
        {
            IsOperationInProgress = false;
            await _mutations.PublishPathsAsync(session, changed, removed);
        }
    }

    private static bool IsSamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);

    private static bool IsSameOrChildPath(string sourcePath, string destinationDirectory)
    {
        if (!Directory.Exists(sourcePath))
        {
            return false;
        }

        var source = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return destination.StartsWith(source, StringComparison.Ordinal);
    }

    private readonly record struct ViewOptions(string Query, bool Hidden, string Sort, bool Ascending);
    private ViewOptions GetViewOptions() => new(SearchQuery.Trim(), ShowHiddenFiles, SortColumn, SortAscending);
    private static bool Matches(FileEntryViewModel entry, ViewOptions options) =>
        (options.Hidden || !entry.IsHidden) && (options.Query.Length == 0 ||
            (options.Query.Contains('*') || options.Query.Contains('?')
                ? FileSystemName.MatchesSimpleExpression(options.Query, entry.NameSortKey, ignoreCase: true)
                : entry.NameSortKey.Contains(options.Query, StringComparison.OrdinalIgnoreCase)));
    private static int Compare(FileEntryViewModel a, FileEntryViewModel b, ViewOptions options)
    {
        int result = b.IsDirectory.CompareTo(a.IsDirectory);
        if (result != 0) return result;
        result = options.Sort switch
        {
            "Date" => a.Snapshot.LastWriteUtcTicks.CompareTo(b.Snapshot.LastWriteUtcTicks),
            "Size" => a.Length.CompareTo(b.Length),
            "Type" => NameComparer.Compare(a.TypeSortKey, b.TypeSortKey),
            _ => NameComparer.Compare(a.NameSortKey, b.NameSortKey)
        };
        if (result == 0) result = NameComparer.Compare(a.NameSortKey, b.NameSortKey);
        if (result == 0) result = StringComparer.Ordinal.Compare(a.Path, b.Path);
        return options.Ascending ? result : -result;
    }
    private readonly record struct SortValue(FileEntryViewModel Entry, FileEntrySnapshot Snapshot, string Type);
    private static SortValue[] CaptureSortValues(IEnumerable<FileEntryViewModel> entries) => entries.Select(e => new SortValue(e, e.Snapshot, e.TypeSortKey)).ToArray();
    private static FileEntryViewModel[] SortedVisible(FileEntryViewModel[] entries, ViewOptions options, FilesPerformance? performance = null, CancellationToken token = default) =>
        SortCaptured(CaptureSortValues(entries), options, performance, token);
    private static FileEntryViewModel[] SortCaptured(SortValue[] entries, ViewOptions options, FilesPerformance? performance = null, CancellationToken token = default)
    {
        int[] visible;
        using (performance?.Measure("Filter")) visible = Enumerable.Range(0, entries.Length).Where(i =>
        {
            token.ThrowIfCancellationRequested();
            ref readonly var e = ref entries[i];
            return
            (options.Hidden || !(e.Snapshot.Name.StartsWith(".", StringComparison.Ordinal) || (e.Snapshot.Attributes & global::System.IO.FileAttributes.Hidden) != 0)) &&
            (options.Query.Length == 0 || (options.Query.Contains('*') || options.Query.Contains('?')
                ? FileSystemName.MatchesSimpleExpression(options.Query, e.Snapshot.Name, ignoreCase: true)
                : e.Snapshot.Name.Contains(options.Query, StringComparison.OrdinalIgnoreCase)));
        }).ToArray();
        FileEntryViewModel[] result;
        SortGate.Wait(token);
        try
        {
            using (performance?.Measure("Sort"))
            {
                // LINQ materializes each cached key once. Comparisons then use
                // the framework's key arrays instead of copying snapshots.
                var grouped = visible.Select(i => entries[i]).OrderByDescending(e => e.Snapshot.IsDirectory);
                IOrderedEnumerable<SortValue> ordered = options.Sort switch
                {
                    "Date" => options.Ascending ? grouped.ThenBy(e => e.Snapshot.LastWriteUtcTicks) : grouped.ThenByDescending(e => e.Snapshot.LastWriteUtcTicks),
                    "Size" => options.Ascending ? grouped.ThenBy(e => e.Snapshot.Length) : grouped.ThenByDescending(e => e.Snapshot.Length),
                    "Type" => options.Ascending ? grouped.ThenBy(e => e.Type, NameComparer) : grouped.ThenByDescending(e => e.Type, NameComparer),
                    _ => grouped
                };
                ordered = options.Ascending ? ordered.ThenBy(e => e.Snapshot.Name, NameComparer) : ordered.ThenByDescending(e => e.Snapshot.Name, NameComparer);
                ordered = options.Ascending ? ordered.ThenBy(e => e.Snapshot.FullPath, StringComparer.Ordinal) : ordered.ThenByDescending(e => e.Snapshot.FullPath, StringComparer.Ordinal);
                result = ordered.Select(e => e.Entry).ToArray();
            }
        }
        finally { SortGate.Release(); }
        token.ThrowIfCancellationRequested();
        return result;
    }
    public void ApplyFilterAndSort() => ViewUpdateTask = UpdateViewAsync();
    public async Task UpdateViewAsync()
    {
        if (CurrentSession is not { IsReady: true } session || !IsCurrent(session)) return;
        _viewCts?.Cancel();
        _viewCts?.Dispose();
        _viewCts = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        var token = _viewCts.Token;
        var generation = ++_viewGeneration;
        var revision = session.Index.Revision;
        var entries = CaptureSortValues(session.Index.Entries);
        var options = GetViewOptions();
        try
        {
            var sorted = await Task.Run(() => SortCaptured(entries, options, session.Performance, token), token);
            await _ui.InvokeAsync(() =>
            {
                if (!IsCurrent(session) || token.IsCancellationRequested || generation != _viewGeneration) return;
                if (revision != session.Index.Revision) { ApplyFilterAndSort(); return; }
                ReplaceProjection(sorted);
                IsLoading = false;
                SelectPendingEntry();
                CompletePendingRename(session);
                EntriesChanged?.Invoke();
            });
        }
        catch (OperationCanceledException) { }
    }

    private void ReplaceProjection(IEnumerable<FileEntryViewModel> entries)
    {
        var selection = SelectedItems.ToArray();
        ProjectionChanging?.Invoke();
        using (CurrentSession?.Performance.Measure("UI collection replacement")) FilteredEntries.ReplaceAll(entries);
        if (CurrentSession is { } session) session.ProjectionReady = true;
        foreach (var item in SelectedItems.ToArray())
            if (!FilteredEntries.Contains(item)) SelectedItems.Remove(item);
        foreach (var item in selection)
            if (FilteredEntries.Contains(item) && !SelectedItems.Contains(item)) SelectedItems.Add(item);
        ProjectionChanged?.Invoke();
        CurrentSession?.Performance.Count("UI collection notifications");
        NotifyEmptyState();
        UpdateStatusText();
    }

    public void ApplyDelta(DirectoryDelta delta)
    {
        if (CurrentSession is { } session) ApplyDelta(session, delta);
    }
    public void ApplyDelta(DirectorySession session, DirectoryDelta delta)
    {
        if (!IsCurrent(session) || delta.Count == 0) return;
        if (!session.IsReady) { session.BufferedMutations.Add(delta); return; }
        using var timing = session.Performance.Measure("Delta application");
        var index = session.Index;
        var options = GetViewOptions();
        var revision = index.Revision;
        var beforeNotifications = FilteredEntries.NotificationCount;
        bool batch = delta.Count > 32;
        HashSet<FileEntryViewModel>? affected = batch ? [] : null;
        void Mutate()
        {
            foreach (var path in delta.Removed)
            {
                if (index.Remove(path) is not { } entry) continue;
                session.Visuals.Invalidate(entry);
                SelectedItems.Remove(entry);
                if (batch) affected!.Add(entry);
                else FilteredEntries.Remove(entry);
            }
            foreach (var rename in delta.Renamed)
            {
                var old = index.Find(rename.OldPath);
                var replaced = index.Find(rename.Snapshot.FullPath);
                if ((old == null || ReferenceEquals(old, replaced)) && replaced?.Snapshot == rename.Snapshot) continue;
                if (replaced != null && !ReferenceEquals(old, replaced) && old != null)
                {
                    SelectedItems.Remove(replaced);
                    session.Visuals.Invalidate(replaced);
                    if (batch) affected!.Add(replaced);
                    else FilteredEntries.Remove(replaced);
                }
                var entry = index.Rename(rename);
                session.Visuals.Invalidate(entry);
                if (batch) affected!.Add(entry);
                else UpdateVisible(entry, options);
            }
            foreach (var snapshot in delta.Added.Concat(delta.Changed))
            {
                var previous = index.Find(snapshot.FullPath);
                var oldSnapshot = previous?.Snapshot;
                var entry = index.Upsert(snapshot);
                if (oldSnapshot == snapshot) continue;
                if (oldSnapshot == null || oldSnapshot.Value.Length != snapshot.Length || oldSnapshot.Value.LastWriteUtcTicks != snapshot.LastWriteUtcTicks || oldSnapshot.Value.Extension != snapshot.Extension)
                    session.Visuals.Invalidate(entry);
                if (batch) affected!.Add(entry);
                else UpdateVisible(entry, options, oldSnapshot == null ? -1 : null);
            }
        }
        Mutate();
        if (batch && index.Revision != revision)
        {
            var unchanged = FilteredEntries.Where(e => !affected!.Contains(e)).ToArray();
            var changed = affected!.Where(e => ReferenceEquals(index.Find(e.Path), e) && Matches(e, options)).ToArray();
            Array.Sort(changed, (a, b) => Compare(a, b, options));
            var merged = new List<FileEntryViewModel>(unchanged.Length + changed.Length);
            int i = 0, j = 0;
            while (i < unchanged.Length && j < changed.Length)
                merged.Add(Compare(unchanged[i], changed[j], options) <= 0 ? unchanged[i++] : changed[j++]);
            while (i < unchanged.Length) merged.Add(unchanged[i++]);
            while (j < changed.Length) merged.Add(changed[j++]);
            ReplaceProjection(merged);
        }
        if (index.Revision == revision) return;
        session.Performance.Count("Entries changed", delta.Count);
        if (!batch) session.Performance.Count("UI collection notifications", FilteredEntries.NotificationCount - beforeNotifications);
        OnPropertyChanged(nameof(Entries));
        NotifyEmptyState();
        UpdateStatusText();
        EntriesChanged?.Invoke();
    }
    private void UpdateVisible(FileEntryViewModel entry, ViewOptions options, int? knownIndex = null)
    {
        var existing = knownIndex ?? FilteredEntries.IndexOf(entry);
        if (!Matches(entry, options))
        {
            if (existing >= 0) FilteredEntries.RemoveAt(existing);
            SelectedItems.Remove(entry);
            return;
        }
        if (existing >= 0 && (existing == 0 || Compare(FilteredEntries[existing - 1], entry, options) <= 0) &&
            (existing == FilteredEntries.Count - 1 || Compare(entry, FilteredEntries[existing + 1], options) <= 0)) return;
        // Binary search the sorted sequence while logically excluding this item.
        var count = FilteredEntries.Count - (existing >= 0 ? 1 : 0);
        int low = 0, high = count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            var actual = existing >= 0 && mid >= existing ? mid + 1 : mid;
            if (Compare(FilteredEntries[actual], entry, options) <= 0) low = mid + 1;
            else high = mid;
        }
        if (existing >= 0) FilteredEntries.Move(existing, low);
        else FilteredEntries.Insert(low, entry);
    }
    public void SetViewport(IReadOnlyList<FileEntryViewModel> visible, IReadOnlyList<FileEntryViewModel>? nearby = null) => CurrentSession?.Visuals.SetViewport(visible, nearby);
    public void MarkFirstFrame()
    {
        if (CurrentSession is { IsReady: true, ProjectionReady: true } session && !IsLoading)
        {
            if (session.Performance.FirstFrame()) session.Performance.Emit();
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loadingCursor?.Dispose();
        _loadingCursor = null;
        Loc.Changed -= OnLanguageChanged;
        _viewCts?.Cancel();
        _viewCts?.Dispose();
        CurrentSession?.Dispose();
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        NotifyEmptyState();
        UpdateTitle();
        UpdateStatusText();
    }

    private void UpdateTitle()
    {
        if (FileLocationPresentation.Resolve(CurrentPath).Kind == FileLocationKind.Trash)
        {
            Title = Loc.Get("Trash");
            return;
        }

        var folderName = Path.GetFileName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar));
        Title = string.IsNullOrEmpty(folderName) ? CurrentPath : folderName;
    }

    internal void ReportFeedback(string message) => OperationStatus = message;

    private void UpdateStatusText()
    {
        if (HasLoadError && !IsLoading)
        {
            StatusText = LoadError!;
            return;
        }
        if (IsLoading)
        {
            StatusText = Loc.Get("Loading…");
            return;
        }

        int count = FilteredEntries.Count;
        string itemsText = count == 1 ? Loc.Get("1 item") : Loc.Format("{0} items", count);

        if (SelectedItems.Count > 0)
        {
            if (SelectedItems.Count == 1)
            {
                var sel = SelectedItems[0];
                string detail = sel.IsDirectory ? Loc.Get("Folder") : sel.DisplaySize;
                StatusText = Loc.Format("{0}  |  Selected: {1} ({2})", itemsText, sel.Name, detail);
            }
            else
            {
                long totalSize = SelectedItems.Where(i => !i.IsDirectory).Sum(i => i.Length);
                string sizeStr = FormatFileSize(totalSize);
                StatusText = Loc.Format("{0} items selected • {1}", SelectedItems.Count, sizeStr);
            }
        }
        else
        {
            StatusText = itemsText;
        }
    }

    private void SelectPendingEntry()
    {
        if (string.IsNullOrWhiteSpace(_pendingSelectionPath))
        {
            return;
        }

        var selected = CurrentSession?.Index.Find(_pendingSelectionPath);
        if (selected is null)
        {
            return;
        }

        SelectedItems.Clear();
        SelectedItems.Add(selected);
        _pendingSelectionPath = null;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }

    public void SetGridViewFromUser(bool isGridView)
    {
        _hasUserSelectedViewMode = true;
        IsGridView = isGridView;
    }

    public void SetDetailsColumnVisibility(bool showDate, bool showType, bool showSize)
    {
        ShowDateColumn = showDate;
        ShowTypeColumn = showType;
        ShowSizeColumn = showSize;
    }

    private static bool UsesMediaLayout(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        return name.Equals("Pictures", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Downloads", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Videos", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Music", StringComparison.OrdinalIgnoreCase);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
