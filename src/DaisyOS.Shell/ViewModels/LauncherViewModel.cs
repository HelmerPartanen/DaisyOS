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
using DaisyOS.Shell.Services;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.ViewModels;

public class LauncherViewModel : INotifyPropertyChanged
{
    public const int MaxPageRows = 5;

    private readonly LinuxAppLauncherService _launcherService;
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
    public bool ShowNoResults => HasSearchQuery && FilteredApps.Count == 0;
    public string AllAppsCountText => Loc.Format("{0} items", AllApps.Count);
    public string SearchResultsCountText => Loc.Format("{0} items", FilteredApps.Count);
    public Task ApplicationsReady { get; }

    private int _searchGeneration;
    private int _appliedSearchGeneration;

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

            var generation = ++_searchGeneration;
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

    internal LauncherViewModel(ShellSessionState? sessionState, IReadOnlyList<AppEntry>? applications)
    {
        _externalDiscoveryEnabled = applications is null;
        _launcherService = new LinuxAppLauncherService();
        if (applications is null) DaisyLaunchClient.Shared.CatalogChanged += () => _ = ReloadApplicationsAsync();
        _sessionState = sessionState ?? new ShellSessionState();

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
            var entries = await Task.Run(BuildApplicationCatalog).ConfigureAwait(false);
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
        OnPropertyChanged(nameof(AllAppsCountText));
        ++_searchGeneration;
        UpdateFilteredApps();
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
            var query = SearchText.Trim().ToUpperInvariant();
            var tokens = query.Contains(' ', StringComparison.Ordinal)
                ? query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                : [query];
            var now = DateTimeOffset.UtcNow;
            var scored = new List<(LauncherItemViewModel Item, int Score, int OriginalIndex)>(AllApps.Count);

            for (var index = 0; index < AllApps.Count; index++)
            {
                var item = AllApps[index];
                var baseScore = ScoreMatch(item, query, tokens);
                if (baseScore <= 0) continue;

                var (launchCount, lastLaunched) = _sessionState.GetApplicationUsage(item.App.Id);
                var usageBoost = 0;
                if (launchCount > 0)
                {
                    usageBoost += Math.Min(45, launchCount * 5);
                    var ageHours = (now - lastLaunched).TotalHours;
                    if (ageHours <= 24) usageBoost += 20;
                    else if (ageHours <= 24 * 7) usageBoost += 10;
                }

                scored.Add((item, baseScore + usageBoost, index));
            }

            scored.Sort(static (left, right) =>
            {
                var byScore = right.Score.CompareTo(left.Score);
                return byScore != 0 ? byScore : left.OriginalIndex.CompareTo(right.OriginalIndex);
            });

            var result = new LauncherItemViewModel[scored.Count];
            for (var i = 0; i < scored.Count; i++) result[i] = scored[i].Item;
            matching = result;
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
            UpdateFilteredApps();
        }
        return TopSearchResult;
    }

    private static int ScoreMatch(LauncherItemViewModel item, string query, string[] tokens)
    {
        var upperName = item.UpperName;
        if (string.Equals(upperName, query, StringComparison.Ordinal))
        {
            return 1000;
        }

        if (upperName.StartsWith(query, StringComparison.Ordinal))
        {
            return 860 - Math.Min(40, upperName.Length - query.Length);
        }

        var words = item.NameWords;
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].StartsWith(query, StringComparison.Ordinal))
            {
                return 740 - Math.Min(30, i * 8);
            }
        }

        if (query.Length >= 2)
        {
            if (string.Equals(item.NameAcronym, query, StringComparison.Ordinal) ||
                string.Equals(item.CamelAcronym, query, StringComparison.Ordinal))
            {
                return 680;
            }
            if (item.NameAcronym.StartsWith(query, StringComparison.Ordinal) ||
                item.CamelAcronym.StartsWith(query, StringComparison.Ordinal))
            {
                return 640;
            }
        }

        if (tokens.Length > 1 && MatchesWordPrefixesInOrder(words, tokens))
        {
            return 700;
        }

        var nameHit = upperName.IndexOf(query, StringComparison.Ordinal);
        if (nameHit >= 0)
        {
            return 520 - Math.Min(60, nameHit * 2);
        }

        foreach (var term in item.UpperSearchTerms)
        {
            if (string.Equals(term, query, StringComparison.Ordinal)) return 460;
            if (term.StartsWith(query, StringComparison.Ordinal)) return 410;
        }

        if (item.SearchKey.Contains(query, StringComparison.Ordinal))
        {
            return 220;
        }

        if (tokens.Length > 1)
        {
            for (var i = 0; i < tokens.Length; i++)
            {
                if (!item.SearchKey.Contains(tokens[i], StringComparison.Ordinal))
                {
                    return 0;
                }
            }
            return 200;
        }

        return 0;
    }

    private static bool MatchesWordPrefixesInOrder(string[] words, string[] tokens)
    {
        var wordIndex = 0;
        for (var t = 0; t < tokens.Length; t++)
        {
            var matched = false;
            while (wordIndex < words.Length)
            {
                if (words[wordIndex++].StartsWith(tokens[t], StringComparison.Ordinal))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched) return false;
        }
        return true;
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
