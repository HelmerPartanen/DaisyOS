using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using DaisyOS.Shell.Apps.Files.Collections;
using DaisyOS.Shell.Services.Diagnostics;
using DaisyOS.Core.Models;
using DaisyOS.Core.Search;
using System.Threading;
using DaisyOS.Shell.Services;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.ViewModels;

public class LauncherViewModel : INotifyPropertyChanged
{
    public const int MaxPageRows = 5;

    private readonly LinuxAppLauncherService _launcherService;
    private readonly ApplicationSearchProvider _applicationSearch;
    private readonly DaisySearchService? _searchService;
    private readonly SearchSession? _searchSession;
    public FastObservableCollection<SettingSearchResultViewModel> SettingResults { get; } = new();
    public FastObservableCollection<UniversalSearchResultViewModel> UniversalResults { get; } = new();
    private UniversalSearchResultViewModel? _selectedUniversalResult;
    public UniversalSearchResultViewModel? SelectedUniversalResult
    { get => _selectedUniversalResult; set
        {
            if (ReferenceEquals(_selectedUniversalResult, value)) return;
            if (_selectedUniversalResult is not null)
                _selectedUniversalResult.IsKeyboardSelected = false;
            _selectedUniversalResult = value; OnPropertyChanged();
        } }
    public bool ShowUnifiedSearch => _searchSession is not null && HasSearchQuery;
    public bool ShowApplicationGrid => !ShowUnifiedSearch;
    public string ResultsHeading => Loc.Get("Results");
    public bool HasSearchFeedback => !string.IsNullOrEmpty(SearchFeedback);
    public string EmptySearchText => Loc.Get(ShowUnifiedSearch ? "No results found" : "No apps found");
    public string SearchFeedback
    {
        get
        {
            if (!ShowUnifiedSearch) return "";
            var status = _searchService?.Files?.Diagnostics.Status;
            if (status is "Initializing" or "Indexing") return Loc.Get("Files are being indexed…");
            if (status == "Paused") return Loc.Get("File indexing is paused.");
            if (status is "Unavailable" or "Degraded") return Loc.Get("The file index needs attention. Open Search in Settings.");
            if (SearchDiagnostics?.Providers.Any(p => p.Status is SearchProviderStatus.Failed or SearchProviderStatus.TimedOut) == true)
                return Loc.Get("Some search results could not be loaded.");
            return "";
        }
    }
    public Func<string, Task>? ClipboardWriter { get; set; }
    public bool HasSettingResults => SettingResults.Count > 0;
    private bool _isSearching;
    public bool IsSearching { get => _isSearching; private set { _isSearching = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowNoResults)); } }
    public Task SearchReady { get; private set; } = Task.CompletedTask;
    public SearchSnapshot? SearchDiagnostics { get; private set; }

    private readonly bool _externalDiscoveryEnabled;
    private readonly ShellSessionState _sessionState;
    private string _searchText = string.Empty;

    public FastObservableCollection<LauncherItemViewModel> AllApps { get; } = new();
    public FastObservableCollection<LauncherItemViewModel> FilteredApps { get; } = new();
    public FastObservableCollection<LauncherItemViewModel> PageApps { get; } = new();

    private readonly Dictionary<int, LauncherItemViewModel[]> _pages = [];
    private int _pageSize = 24;
    private int _pageIndex;
    private int _pageColumns = 8;
    private int _pageRows = 3;
    private int _selectedFilteredIndex = -1;
    private LauncherItemViewModel? _selectedItem;

    public int PageIndex => _pageIndex;
    public int PageCount => Math.Max(1, (FilteredApps.Count + _pageSize - 1) / _pageSize);
    public int PageColumns => _pageColumns;
    public int PageRows => _pageRows;
    public int PageSize => _pageSize;
    public double PageGridWidth => _pageColumns * 136;
    public double PageGridHeight => _pageRows * 128;

    public void SetPageLayout(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Clamp(rows, 1, MaxPageRows);
        var size = columns * rows;
        if (size == _pageSize && columns == _pageColumns && rows == _pageRows) return;
        var anchor = _selectedFilteredIndex >= 0 ? _selectedFilteredIndex : _pageIndex * _pageSize;
        _pages.Clear();
        _pageSize = size;
        _pageIndex = anchor / size;
        _pageColumns = columns;
        _pageRows = rows;
        OnPropertyChanged(nameof(PageGridWidth));
        OnPropertyChanged(nameof(PageGridHeight));
        RefreshPage();
    }

    internal LauncherItemViewModel[] GetPageApps(int index)
    {
        if (index < 0 || index >= PageCount) return [];
        if (_pages.TryGetValue(index, out var cached)) return cached;
        var start = index * _pageSize;
        var page = new LauncherItemViewModel[Math.Min(_pageSize, FilteredApps.Count - start)];
        for (var i = 0; i < page.Length; i++) page[i] = FilteredApps[start + i];
        return _pages[index] = page;
    }

    public void GoToPage(int index)
    {
        var next = Math.Clamp(index, 0, PageCount - 1);
        if (_pageIndex == next) return;
        _pageIndex = next;
        RefreshPage();
    }

    private void RefreshPage()
    {
        _pageIndex = Math.Clamp(_pageIndex, 0, PageCount - 1);
        var page = GetPageApps(_pageIndex);
        if (!PageApps.SequenceEqual(page)) PageApps.ReplaceAll(page);
        OnPropertyChanged(nameof(PageIndex));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(HasMultiplePages));
    }

    public bool HasMultiplePages => PageCount > 1;

    public LauncherItemViewModel? TopSearchResult { get; private set; }
    public LauncherItemViewModel? SelectedItem => _selectedItem;

    public string UserName { get; } = Environment.UserName is { Length: > 0 } name
        ? char.ToUpperInvariant(name[0]) + name[1..]
        : "User";
    public string UserInitial => UserName.Length > 0 ? UserName[..1] : "U";

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchText);
    public bool ShowNoResults => HasSearchQuery && FilteredApps.Count == 0 && UniversalResults.Count == 0 && !HasSettingResults && !IsSearching;
    public string AllAppsCountText => Loc.Format("{0} items", AllApps.Count);
    public string SearchResultsCountText => Loc.Format("{0} items", ShowUnifiedSearch ? UniversalResults.Count : FilteredApps.Count + SettingResults.Count);
    public Task ApplicationsReady { get; }

    private int _searchGeneration;
    private int _appliedSearchGeneration;
    private CancellationTokenSource? _searchRequest;

    public string SearchText
    {
        get => _searchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_searchText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSearchQuery));
            OnPropertyChanged(nameof(ShowUnifiedSearch));
            OnPropertyChanged(nameof(ShowApplicationGrid));
            OnPropertyChanged(nameof(EmptySearchText));
            OnPropertyChanged(nameof(SearchFeedback));
            OnPropertyChanged(nameof(HasSearchFeedback));

            var generation = ++_searchGeneration;
            if (_searchSession is not null)
            {
                SearchReady = UpdateSearchAsync(generation);
                return;
            }
            if (AllApps.Count <= 2500 && (Dispatcher.UIThread.CheckAccess() || Avalonia.Application.Current is null))
            {
                UpdateFilteredApps();
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (generation == _searchGeneration && generation != _appliedSearchGeneration) UpdateFilteredApps();
                }, DispatcherPriority.Input);
            }
        }
    }

    public LauncherViewModel(ShellSessionState? sessionState = null) : this(sessionState, null) { }

    internal LauncherViewModel(ShellSessionState? sessionState, IReadOnlyList<AppEntry>? applications, DaisySearchService? searchService = null)
    {
        _externalDiscoveryEnabled = applications is null;
        _searchService = searchService ?? (applications is null && Avalonia.Application.Current is App app ? app.SearchService : null);
        _applicationSearch = _searchService?.Applications ?? new ApplicationSearchProvider();
        _searchSession = _searchService?.Coordinator.CreateSession();
        if (_searchService is not null)
        {
            _searchService.Settings.ValueChanged += OnSearchSettingChanged;
            _searchService.ResultsChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(SearchFeedback));
                OnPropertyChanged(nameof(HasSearchFeedback));
                if (HasSearchQuery && !IsSearching) SearchReady = UpdateSearchAsync(++_searchGeneration, clear: false);
            };
        }
        _launcherService = new LinuxAppLauncherService();
        if (_searchService is not null)
            _searchService.CatalogChanged += (_, _) => ApplyApplicationCatalog(_searchService.Catalog);
        else if (applications is null) DaisyLaunchClient.Shared.CatalogChanged += () => _ = ReloadApplicationsAsync();
        _sessionState = sessionState ?? new ShellSessionState();
        if (_searchService is null) _applicationSearch.UsageBoost = id =>
        {
            var (count, last) = _sessionState.GetApplicationUsage(id);
            if (count <= 0) return 0;
            var age = (DateTimeOffset.UtcNow - last).TotalHours;
            return Math.Min(45, count * 5d) + (age <= 24 ? 20 : age <= 168 ? 10 : 0);
        };

        if (applications is null) ApplicationsReady = LoadApplicationsAsync();
        else
        {
            ApplyApplicationCatalog(applications);
            ApplicationsReady = Task.CompletedTask;
        }
    }

    public async Task ReloadApplicationsAsync()
    {
        if (!_externalDiscoveryEnabled) return;
        if (_searchService is not null) { await _searchService.RefreshCatalogAsync(); return; }
        try
        {
            var entries = await Task.Run(() => { _launcherService.Refresh(); return BuildApplicationCatalog(); }).ConfigureAwait(false);
            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyApplicationCatalog(entries);
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() => ApplyApplicationCatalog(entries));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[daisy-launcher] Failed to reload launcher applications: {ex}");
        }
    }

    private async Task LoadApplicationsAsync()
    {
        try
        {
            IReadOnlyList<AppEntry> entries;
            if (_searchService is not null)
            {
                await _searchService.ApplicationsReady.ConfigureAwait(false);
                entries = _searchService.Catalog;
            }
            else entries = await Task.Run(BuildApplicationCatalog).ConfigureAwait(false);
            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyApplicationCatalog(entries);
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() => ApplyApplicationCatalog(entries));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[daisy-launcher] Failed to load launcher applications: {ex}");
        }
    }

    private IReadOnlyList<AppEntry> BuildApplicationCatalog()
    {
        DesktopItemLoader.WarmUp();
        return _launcherService.GetAvailableApps()
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private void ApplyApplicationCatalog(IReadOnlyList<AppEntry> apps)
    {
        using var trace = ResponsivenessTrace.Measure("launcher.catalog.apply");
        var old = AllApps.ToDictionary(item => item.App.Id, StringComparer.Ordinal);
        var items = apps.Select(app =>
        {
            if (old.TryGetValue(app.Id, out var existing)) { existing.UpdateApp(app); return existing; }
            return new LauncherItemViewModel(app);
        }).ToArray();
        AllApps.ReplaceAll(items);
        // Use localized catalog names in the UI and in search alike.
        if (_searchService is null) _applicationSearch.ReplaceCatalog(items.Select(item => item.App with { Name = item.Name, Description = item.Description }));
        OnPropertyChanged(nameof(AllAppsCountText));
        ++_searchGeneration;
        if (_searchSession is not null && HasSearchQuery) SearchReady = UpdateSearchAsync(_searchGeneration);
        else UpdateFilteredApps();
    }

    private void UpdateFilteredApps()
    {
        using var trace = ResponsivenessTrace.Measure("launcher.search.projection");
        _appliedSearchGeneration = _searchGeneration;

        IReadOnlyList<LauncherItemViewModel> matching;
        if (!HasSearchQuery)
        {
            matching = AllApps;
        }
        else
        {
            var byId = AllApps.ToDictionary(item => item.App.Id, StringComparer.Ordinal);
            matching = _applicationSearch.Search(SearchQuery.Parse(SearchText))
                .Select(result => byId[result.Id]).ToArray();
        }

        if (!FilteredApps.SequenceEqual(matching))
        {
            _pages.Clear();
            FilteredApps.ReplaceAll(matching);
        }

        _pageIndex = 0;
        RefreshPage();

        if (HasSearchQuery && FilteredApps.Count > 0)
        {
            ApplySelection(0);
        }
        else
        {
            ApplySelection(-1);
        }

        OnPropertyChanged(nameof(SearchResultsCountText));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    private async Task UpdateSearchAsync(int generation, bool clear = true)
    {
        _searchRequest?.Cancel();
        var request = _searchRequest = new CancellationTokenSource();
        var query = SearchText;
        if (!HasSearchQuery)
        {
            await foreach (var ignored in _searchSession!.SearchAsync("")) { }
            if (generation == _searchGeneration)
            {
                SettingResults.Clear();
                UniversalResults.Clear();
                SelectedUniversalResult = null;
                OnPropertyChanged(nameof(HasSettingResults));
                IsSearching = false;
                UpdateFilteredApps();
            }
            if (ReferenceEquals(_searchRequest, request)) _searchRequest = null;
            request.Dispose();
            return;
        }
        IsSearching = true;
        OnPropertyChanged(nameof(HasSettingResults));
        OnPropertyChanged(nameof(SearchResultsCountText));
        OnPropertyChanged(nameof(ShowNoResults));
        try
        {
            // Coalesce typing; retain the previous complete list while providers work.
            if (clear) await Task.Delay(75, request.Token);
            await foreach (var snapshot in _searchSession!.SearchAsync(query, request.Token))
            {
                // Publishing partial background snapshots removes and re-adds slower results.
                // Present one coherent update once all providers finish or reach their deadline.
                if (!snapshot.IsComplete) continue;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (generation != _searchGeneration || request.IsCancellationRequested) return;
                    SearchDiagnostics = snapshot;
                    OnPropertyChanged(nameof(SearchDiagnostics));
                    OnPropertyChanged(nameof(SearchFeedback));
                    OnPropertyChanged(nameof(HasSearchFeedback));
                    var byId = AllApps.ToDictionary(item => item.App.Id, StringComparer.Ordinal);
                    var apps = snapshot.Results.Where(r => r.Payload is ApplicationSearchPayload && byId.ContainsKey(r.Id))
                        .Select(r => byId[r.Id]).ToArray();
                    if (!FilteredApps.SequenceEqual(apps))
                    {
                        FilteredApps.ReplaceAll(apps);
                        _pages.Clear();
                        _pageIndex = 0;
                        RefreshPage();
                        ApplySelection(apps.Length > 0 ? 0 : -1);
                    }
                    var existing = SettingResults.ToDictionary(r => r.Id, StringComparer.Ordinal);
                    var settings = snapshot.Results.Where(r => r.Payload is SettingSearchPayload)
                        .Select(r => existing.TryGetValue(r.Id, out var item) ? item : new SettingSearchResultViewModel(r, _searchService!.Settings)).ToArray();
                    SynchronizeSearchRows(SettingResults, settings);
                    var selectedKey = SelectedUniversalResult is { } selected ? (selected.Result.ProviderId, selected.Result.Id) : default;
                    var priorRows = UniversalResults.ToDictionary(row => (row.Result.ProviderId, row.Result.Id));
                    var projected = snapshot.Results.Select(result => priorRows.TryGetValue((result.ProviderId, result.Id), out var row) && row.Result.Title == result.Title && row.Result.Subtitle == result.Subtitle &&
                        row.Result.Payload == result.Payload && row.Result.IsEnabled == result.IsEnabled && row.Result.Actions.SequenceEqual(result.Actions)
                        ? row : new UniversalSearchResultViewModel(result,
                            settings.FirstOrDefault(setting => setting.Id == result.Id && result.Payload is SettingSearchPayload),
                            ExecuteSearchActionAsync, result.Payload is ApplicationSearchPayload appPayload ? byId.GetValueOrDefault(appPayload.ApplicationId) : null)).ToArray();
                    for (var rowIndex = 0; rowIndex < projected.Length; rowIndex++) projected[rowIndex].ShowDivider = rowIndex < projected.Length - 1;
                    SynchronizeSearchRows(UniversalResults, projected);
                    SelectedUniversalResult = projected.FirstOrDefault(result => (result.Result.ProviderId, result.Result.Id) == selectedKey) ?? projected.FirstOrDefault();
                    foreach (var setting in settings.Where(setting => !existing.ContainsKey(setting.Id))) _ = setting.RefreshAsync();
                    _appliedSearchGeneration = generation;
                    IsSearching = !snapshot.IsComplete;
                    OnPropertyChanged(nameof(HasSettingResults));
                    OnPropertyChanged(nameof(SearchResultsCountText));
                    OnPropertyChanged(nameof(ShowNoResults));
                });
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_searchRequest, request))
            {
                _searchRequest = null;
                if (generation == _searchGeneration) IsSearching = false;
            }
            request.Dispose();
        }
    }

    // Preserve containers, focus, scroll position and selection for unchanged rows.
    private static void SynchronizeSearchRows<T>(FastObservableCollection<T> target, IReadOnlyList<T> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (index < target.Count && EqualityComparer<T>.Default.Equals(target[index], rows[index])) continue;
            var previous = target.IndexOf(rows[index]);
            if (previous >= 0) target.Move(previous, index);
            else target.Insert(index, rows[index]);
        }
        while (target.Count > rows.Count) target.RemoveAt(target.Count - 1);
    }

    public bool HasCurrentSearchResults => _appliedSearchGeneration == _searchGeneration;

    private async Task ExecuteSearchActionAsync(SearchAction action)
    {
        if (!HasCurrentSearchResults)
        {
            await SearchReady;
            if (!HasCurrentSearchResults) return;
            if (!UniversalResults.Any(row => row.Result.Actions.Contains(action))) return;
        }
        if (action.Id is "copy-result" or "copy-path")
        {
            var source = UniversalResults.Select(r => r.Result).FirstOrDefault(r => r.ProviderId == action.ProviderId && r.Actions.Contains(action));
            if (source is null || ClipboardWriter is null) throw new InvalidOperationException("Clipboard is unavailable.");
            var text = source.Payload switch
            {
                CalculatorSearchPayload calculation => calculation.Value,
                FileSearchPayload file => file.Path,
                _ => throw new InvalidOperationException("This item cannot be copied.")
            };
            await ClipboardWriter(text);
            return;
        }
        await _searchService!.Coordinator.ExecuteAsync(action);
    }
    public bool MoveUniversalSelection(int delta)
    {
        if (UniversalResults.Count == 0) return false;
        var index = SelectedUniversalResult is null ? 0 : UniversalResults.IndexOf(SelectedUniversalResult);
        SelectedUniversalResult = UniversalResults[Math.Clamp(index + delta, 0, UniversalResults.Count - 1)];
        SelectedUniversalResult.IsKeyboardSelected = true;
        return true;
    }

    private void OnSearchSettingChanged(object? sender, string id) => Dispatcher.UIThread.Post(() =>
    {
        foreach (var setting in SettingResults.Where(s => s.Id == id)) _ = setting.RefreshAsync();
    });

    private void ApplySelection(int filteredIndex)
    {
        if (filteredIndex < 0 || filteredIndex >= FilteredApps.Count)
        {
            _selectedFilteredIndex = -1;
            if (_selectedItem is not null)
            {
                _selectedItem.IsKeyboardSelected = false;
                _selectedItem = null;
            }
            if (TopSearchResult is not null)
            {
                TopSearchResult.IsTopSearchResult = false;
                TopSearchResult = null;
            }
            OnPropertyChanged(nameof(SelectedItem));
            OnPropertyChanged(nameof(TopSearchResult));
            return;
        }

        _selectedFilteredIndex = filteredIndex;
        var next = FilteredApps[filteredIndex];

        if (!ReferenceEquals(_selectedItem, next))
        {
            if (_selectedItem is not null) _selectedItem.IsKeyboardSelected = false;
            _selectedItem = next;
            _selectedItem.IsKeyboardSelected = true;
            OnPropertyChanged(nameof(SelectedItem));
        }
        else
        {
            _selectedItem.IsKeyboardSelected = true;
        }

        var nextSearchTarget = HasSearchQuery ? next : null;
        if (!ReferenceEquals(TopSearchResult, nextSearchTarget))
        {
            if (TopSearchResult is not null) TopSearchResult.IsTopSearchResult = false;
            TopSearchResult = nextSearchTarget;
            if (TopSearchResult is not null) TopSearchResult.IsTopSearchResult = true;
            OnPropertyChanged(nameof(TopSearchResult));
        }
        else if (TopSearchResult is not null)
        {
            TopSearchResult.IsTopSearchResult = true;
        }
    }

    public void ResetSelection()
    {
        if (HasSearchQuery && FilteredApps.Count > 0)
        {
            ApplySelection(0);
        }
        else
        {
            ApplySelection(-1);
        }
    }

    public bool MoveSelection(int deltaColumns, int deltaRows)
    {
        if (FilteredApps.Count == 0) return false;

        var pageStart = _pageIndex * _pageSize;
        var pageEnd = Math.Min(FilteredApps.Count, pageStart + _pageSize) - 1;
        if (pageEnd < pageStart) return false;

        // First directional press selects the natural anchor on the visible page.
        if (_selectedFilteredIndex < pageStart || _selectedFilteredIndex > pageEnd)
        {
            var initial = (deltaColumns < 0 || deltaRows < 0) ? pageEnd : pageStart;
            ApplySelection(initial);
            return true;
        }

        var current = _selectedFilteredIndex;
        int target = current;

        if (deltaColumns != 0)
        {
            target = Math.Clamp(current + deltaColumns, 0, FilteredApps.Count - 1);
        }
        else if (deltaRows != 0)
        {
            var localIndex = current - pageStart;
            var col = localIndex % _pageColumns;
            var row = localIndex / _pageColumns;
            var pageCount = pageEnd - pageStart + 1;
            var rowsOnPage = (pageCount + _pageColumns - 1) / _pageColumns;
            var nextRow = row + deltaRows;

            if (nextRow >= 0 && nextRow < rowsOnPage)
            {
                target = Math.Min(pageEnd, pageStart + nextRow * _pageColumns + col);
            }
            else if (rowsOnPage == 1)
            {
                target = Math.Clamp(current + deltaRows, pageStart, pageEnd);
            }
        }

        var targetPage = target / _pageSize;
        if (targetPage != _pageIndex)
        {
            GoToPage(targetPage);
        }

        ApplySelection(target);
        return true;
    }

    // Enter can arrive before a queued search projection on huge catalogs.
    // Resolve against the current query so a fast typist never launches a stale result.
    public LauncherItemViewModel? ResolveSearchResult()
    {
        if (_appliedSearchGeneration != _searchGeneration)
        {
            if (_searchSession is not null) return null;
            UpdateFilteredApps();
        }
        return TopSearchResult;
    }

    public async Task<AppLaunchResult?> LaunchAppAsync(LauncherItemViewModel? item, string activationToken = "")
    {
        if (item == null) return null;
        try
        {
            if (Avalonia.Application.Current is App appInstance && appInstance.TryLaunchSystemApp(item.App.Id))
            {
                return new AppLaunchResult(true, $"Launched {item.Name}");
            }

            if (Avalonia.Application.Current is App shell && await shell.TryActivateExternalApplicationAsync(item.App.Id))
                return new AppLaunchResult(true, $"Activated {item.Name}");
            using var trace = ResponsivenessTrace.Measure("external.launch.dispatch", item.App.Id);
            return await _launcherService.LaunchWithActivationAsync(item.App, activationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch app {item.Name}: {ex.Message}");
            return new AppLaunchResult(false, $"{item.Name} could not be opened.");
        }
    }

    public void RecordSuccessfulLaunch(LauncherItemViewModel item)
    {
        _sessionState.RecordApplicationLaunch(item.App.Id);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
