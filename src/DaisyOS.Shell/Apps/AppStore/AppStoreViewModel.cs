using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using DaisyOS.Core.Helpers;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.AppStore;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Apps.AppStore;

public sealed class AppCategoryItem : INotifyPropertyChanged
{
    private readonly string _name;
    public string Name => Loc.Get(_name);
    public AppCategory Category { get; }
    public string Icon { get; }
    private bool _selected;
    public bool IsSelected { get => _selected; internal set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RefreshLanguage() => PropertyChanged?.Invoke(this, new(nameof(Name)));
    public AppCategoryItem(string name, AppCategory category, string icon) { _name = name; Category = category; Icon = icon; }
}

public sealed class AppStoreCardViewModel : INotifyPropertyChanged
{
    private readonly IAppStoreService _service;
    private AppInstallState _state;
    private bool _busy;
    private string _error = "";
    private CancellationTokenSource? _operation;
    public AppStoreItem Item { get; private set; }
    public string Id => Item.Id;
    public string Name => Item.Name;
    public string Summary => Item.Summary == "Installed application" ? Loc.Get("Installed application") : Item.Summary;
    public string Description => Item.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string Publisher => string.IsNullOrWhiteSpace(Item.Publisher) ? Loc.Get("Publisher not provided") : Item.Publisher;
    public string Version => string.IsNullOrWhiteSpace(Item.Version) ? Loc.Get("Not provided") : Item.Version;
    public string IconGlyph => Item.IconGlyph;
    public string IconPath => Item.IconPath;
    public bool IsVerified => Item.IsVerified;
    public IReadOnlyList<string> Screenshots => Item.Screenshots;
    public bool HasScreenshots => Screenshots.Count > 0;
    public string License => string.IsNullOrWhiteSpace(Item.License) ? Loc.Get("Not provided") : Item.License.Split('=')[0];
    private AppPackageInfo? _packageInfo;
    private bool _infoLoading;
    private int _infoVersion;
    public bool HasPackageInfo => _packageInfo != null;
    public bool IsPackageInfoLoading => _infoLoading;
    public string DownloadSize => Size(_packageInfo?.DownloadBytes ?? 0);
    public string InstalledSize => Size(_packageInfo?.InstalledBytes ?? 0);
    public bool HasRuntimeSize => (_packageInfo?.RuntimeInstalledBytes ?? 0) > 0;
    public string RuntimeSizeNote => Loc.Format("Shared runtime: up to {0} of disk space. It may already be installed.", Size(_packageInfo?.RuntimeInstalledBytes ?? 0));
    public IReadOnlyList<string> Permissions => _packageInfo?.Permissions.Select(Loc.Get).ToArray() ?? [];
    public bool HasPermissions => Permissions.Count > 0;
    public bool RuntimeEndOfLife => _packageInfo?.RuntimeEndOfLife == true;
    private static string Size(long bytes) => bytes == 0 ? Loc.Get("Not provided") : bytes >= 1_000_000_000 ? $"{bytes / 1_000_000_000.0:F1} GB" : $"{bytes / 1_000_000.0:F1} MB";
    internal async Task LoadPackageInfoAsync(CancellationToken token)
    {
        if (_packageInfo != null) return;
        var version = ++_infoVersion;
        _infoLoading = true; PropertyChanged?.Invoke(this, new(nameof(IsPackageInfoLoading)));
        try { _packageInfo = await _service.GetPackageInfoAsync(Id, token); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or global::System.Text.Json.JsonException) { }
        finally { if (version == _infoVersion) { _infoLoading = false; PropertyChanged?.Invoke(this, new(null)); } }
    }
    public string ReleaseNotes => Item.ReleaseNotes;
    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);
    public string SourceLabel => Item.Origin.Equals("flathub", StringComparison.OrdinalIgnoreCase) ? "Flathub" : Item.Origin;
    public string InstallLocation => Item.InstallationScope == "user" ? Loc.Get("Your account") : Loc.Get("All users");
    public string AppUrl => "https://flathub.org/apps/" + Uri.EscapeDataString(Id);
    public bool HasHomepage => Uri.TryCreate(Item.Homepage, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
    public bool IsFeatured => Item.IsFeatured;
    public string StatusText => IsBusy ? ActionText : _state switch { AppInstallState.Installed => Loc.Get("Installed"), AppInstallState.UpdateAvailable => Loc.Get("Update available"), _ => IsVerified ? Loc.Get("Verified on Flathub") : "" };
    public string ActionText => IsBusy ? _operationName : _state switch
    { AppInstallState.Installed => Loc.Get("Open"), AppInstallState.UpdateAvailable => Loc.Get("Update"), _ => Loc.Get("Install") };
    private string _operationName = "";
    public string ActionAccessibilityLabel => ActionText + " " + Name;
    public bool CanOpen => !IsBusy && _state is AppInstallState.Installed or AppInstallState.UpdateAvailable;
    public bool CanInstall => !IsBusy && _state == AppInstallState.NotInstalled;
    public bool CanUninstall => Item.NativeDesktopFile.Length == 0 && !IsBusy && _state is AppInstallState.Installed or AppInstallState.UpdateAvailable;
    public bool CanUpdate => !IsBusy && _state == AppInstallState.UpdateAvailable;
    public AppInstallState InstallState { get => _state; set { _state = value; Publish(); } }
    public bool IsBusy => _busy;
    public bool HasError => !string.IsNullOrEmpty(_error);
    public string ErrorMessage => _error;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action<AppStoreCardViewModel>? StateChanged;
    public AppStoreCardViewModel(AppStoreItem item, IAppStoreService service) { Item = item; _service = service; _state = item.InstallState; }
    internal void UpdateItem(AppStoreItem item)
    {
        if (_busy) return;
        Item = item; _state = item.InstallState;
        PropertyChanged?.Invoke(this, new(null));
    }
    public Task InstallAsync() => RunAsync("install");
    public Task UninstallAsync() => RunAsync("remove");
    public Task UpdateAsync() => RunAsync("update");
    public async Task PrimaryActionAsync()
    {
        if (CanUpdate) await UpdateAsync();
        else if (CanOpen) Launch();
        else if (CanInstall) await InstallAsync();
    }
    private async Task RunAsync(string operation)
    {
        if (_busy) return;
        using var busyCursor = BusyCursor.Begin();
        _operation = new CancellationTokenSource();
        var request = _operation;
        _busy = true; _error = "";
        _operationName = Loc.Get(operation switch { "remove" => "Removing…", "update" => "Updating…", _ => "Installing…" });
        Publish(); StateChanged?.Invoke(this);
        try
        {
            var success = operation switch
            {
                "remove" => await _service.UninstallAppAsync(Item.PackageName, request.Token),
                "update" => await _service.UpdateAppAsync(Item.PackageName, cancellationToken: request.Token),
                _ => await _service.InstallAppAsync(Item.PackageName, cancellationToken: request.Token)
            };
            if (!success) throw new IOException("The operation could not finish. Try again.");
            _state = operation == "remove" ? AppInstallState.NotInstalled : AppInstallState.Installed;
            // Reconcile version/scope, including a second installation that remains after removal.
            try
            {
                var installed = await _service.GetInstalledAppsAsync(request.Token);
                if (installed.FirstOrDefault(a => a.Id == Id) is { } current)
                { Item = current; _state = current.InstallState; PropertyChanged?.Invoke(this, new(null)); }
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException) { }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { _error = Loc.Get("Operation cancelled. Refresh to check the application's current state."); }
        catch (Exception ex) { _error = Loc.Get(ex is AppStoreException ? ex.Message : "The operation could not finish. Try again."); }
        finally { _busy = false; _operation = null; request.Dispose(); Publish(); StateChanged?.Invoke(this); }
    }
    public void Cancel() => _operation?.Cancel();
    public void Launch()
    {
        if (!CanOpen) return;
        var result = Item.NativeDesktopFile.Length > 0
            ? new SafeProcessLauncher().Launch("gio", ["launch", Item.NativeDesktopFile])
            : new SafeProcessLauncher().Launch("flatpak", ["run", Item.InstallationScope is "user" or "system" ? "--" + Item.InstallationScope : "--installation=" + Item.InstallationScope, Id]);
        if (!result.Succeeded) { _error = Loc.Get("The application could not be opened. Try again."); Publish(); }
    }
    private void Publish()
    {
        foreach (var name in new[] { nameof(InstallState), nameof(ActionText), nameof(ActionAccessibilityLabel), nameof(StatusText), nameof(IsBusy), nameof(CanOpen), nameof(CanInstall), nameof(CanUninstall), nameof(CanUpdate), nameof(HasError), nameof(ErrorMessage) })
            PropertyChanged?.Invoke(this, new(name));
    }
}

public sealed class AppStoreViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAppStoreService _service;
    private readonly Dictionary<string, AppStoreCardViewModel> _cards = new(StringComparer.Ordinal);
    private IReadOnlyList<AppStoreItem> _results = [];
    private AppCategoryItem _selectedCategory;
    private string _searchQuery = "", _errorMessage = "";
    private bool _isLoading, _disposed, _verifiedOnly;
    private int _requestVersion, _visibleCount = 24, _sortIndex;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _detailsCts;
    private AppStoreCardViewModel? _selectedApp;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<AppCategoryItem> Categories { get; } = [];
    public ObservableCollection<AppCategoryItem> LibraryCategories { get; } = [];
    public ObservableCollection<AppStoreCardViewModel> Apps { get; } = [];
    public ObservableCollection<AppStoreCardViewModel> FeaturedApps { get; } = [];
    public IReadOnlyList<string> SortOptions => [Loc.Get("Recommended"), Loc.Get("Name A–Z")];
    public ICommand RetryCommand { get; }
    public ICommand RefreshCatalogCommand { get; }
    public ICommand LoadMoreCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand UpdateAllCommand { get; }
    internal Task PendingLoad { get; private set; } = Task.CompletedTask;
    public string ErrorMessage => _errorMessage;
    public bool HasError => !string.IsNullOrEmpty(_errorMessage) && _results.Count == 0;
    public bool HasCatalogWarning => !string.IsNullOrEmpty(_errorMessage) && _results.Count > 0;
    public bool ShowEmptyState => !IsLoading && !HasError && Apps.Count == 0;
    public bool HasResults => !IsLoading && !HasError && Apps.Count > 0;
    public bool HasMore => _visibleCount < FilteredResults().Count;
    public string EmptyStateTitle => !string.IsNullOrWhiteSpace(SearchQuery) ? Loc.Get("No apps found") : SelectedCategory.Category switch
    { AppCategory.Installed => Loc.Get("Your library starts here"), AppCategory.Updates => Loc.Get("You're up to date"), _ => Loc.Get("No apps to show") };
    public string EmptyStateDescription => SelectedCategory.Category switch
    { AppCategory.Installed => Loc.Get("Install an app from Discover and it will appear here."), AppCategory.Updates => Loc.Get("Check again later for new versions of your installed apps."), _ => Loc.Get("Try a different name or keyword, or turn off the verified filter.") };
    public string PageTitle => !string.IsNullOrWhiteSpace(SearchQuery) ? Loc.Get("Search results") : SelectedCategory.Name;
    public string PageDescription => !string.IsNullOrWhiteSpace(SearchQuery) ? Loc.Format("Apps matching “{0}”", SearchQuery.Trim()) : SelectedCategory.Category switch
    { AppCategory.Discover => Loc.Get("Find something great for your everyday."), AppCategory.Installed => Loc.Get("Your applications, all in one place."),
      AppCategory.Updates => Loc.Get("Keep your applications running at their best."), _ => Loc.Get("Explore applications from the Flathub community.") };
    public string ResultsLabel => Loc.Format("{0} applications", FilteredResults().Count);
    public string CollectionTitle => !string.IsNullOrWhiteSpace(SearchQuery) ? Loc.Get("Matching applications") : SelectedCategory.Category == AppCategory.Discover ? Loc.Get("Explore the catalog") : SelectedCategory.Name;
    public bool IsDiscoverMode => !IsLoading && !HasError && FeaturedApps.Count > 0 && SelectedCategory.Category == AppCategory.Discover && string.IsNullOrWhiteSpace(SearchQuery) && !VerifiedOnly;
    public bool IsLibrary => SelectedCategory.Category is AppCategory.Installed or AppCategory.Updates;
    public bool ShowUpdateAll => SelectedCategory.Category == AppCategory.Updates && HasResults;
    public bool HasActiveOperations => _cards.Values.Any(c => c.IsBusy);
    public string OperationStatus => _cards.Values.FirstOrDefault(c => c.IsBusy) is { } c ? c.ActionText + " " + c.Name : Loc.Get("Applications from Flathub");
    public bool IsDetailMode => SelectedApp != null;
    public AppStoreCardViewModel? SelectedApp { get => _selectedApp; private set { _selectedApp = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDetailMode)); } }
    public bool VerifiedOnly { get => _verifiedOnly; set { if (_verifiedOnly == value) return; _verifiedOnly = value; OnPropertyChanged(); ApplyResults(); } }
    public int SortIndex { get => _sortIndex; set { if (_sortIndex == value) return; _sortIndex = value; OnPropertyChanged(); ApplyResults(); } }
    public AppCategoryItem SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value == null || _selectedCategory == value) return;
            _selectedCategory.IsSelected = false; _selectedCategory = value; value.IsSelected = true;
            SelectedApp = null; _searchQuery = ""; _verifiedOnly = false;
            OnPropertyChanged(); OnPropertyChanged(nameof(SearchQuery)); OnPropertyChanged(nameof(VerifiedOnly));
            PendingLoad = LoadAppsAsync(value.Category, "", false);
        }
    }
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            value ??= ""; if (_searchQuery == value) return;
            _searchQuery = value; SelectedApp = null; OnPropertyChanged();
            PendingLoad = LoadAppsAsync(SelectedCategory.Category, value.Trim(), true);
        }
    }
    public bool IsLoading { get => _isLoading; private set { _isLoading = value; OnPropertyChanged(); PublishContentState(); } }
    public AppStoreViewModel(IAppStoreService? service = null)
    {
        Loc.Changed += OnLanguageChanged;
        _service = service ?? new LinuxAppStoreService(new SafeCommandRunner());
        RetryCommand = new AsyncRelayCommand(RefreshAsync);
        RefreshCatalogCommand = new AsyncRelayCommand(RefreshCatalogAsync);
        LoadMoreCommand = new RelayCommand(() => { _visibleCount += 24; ApplyResults(resetPage: false); });
        BackCommand = new RelayCommand(() => { _detailsCts?.Cancel(); SelectedApp = null; });
        UpdateAllCommand = new AsyncRelayCommand(async () => { foreach (var card in _results.Select(Card).Where(c => c.CanUpdate).ToArray()) { if (_disposed) break; await card.UpdateAsync(); } });
        foreach (var (name, category, icon) in new[] { ("Discover", AppCategory.Discover, "grid-four"), ("Productivity", AppCategory.Productivity, "briefcase"),
            ("Internet", AppCategory.Internet, "globe"), ("Creative", AppCategory.Creative, "paint-brush"), ("Development", AppCategory.Development, "code"),
            ("Gaming", AppCategory.Gaming, "game-controller"), ("Audio", AppCategory.Audio, "headphones"), ("Video", AppCategory.Video, "film-strip"),
            ("Education", AppCategory.Education, "book-open"), ("Utilities", AppCategory.Utilities, "package") }) Categories.Add(new(name, category, icon));
        LibraryCategories.Add(new("Installed", AppCategory.Installed, "check-circle")); LibraryCategories.Add(new("Updates", AppCategory.Updates, "arrows-clockwise"));
        _selectedCategory = Categories[0]; _selectedCategory.IsSelected = true;
        PendingLoad = LoadCategoryAppsAsync(AppCategory.Discover);
    }
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var category in Categories.Concat(LibraryCategories)) category.RefreshLanguage();
        foreach (var card in _cards.Values) card.UpdateItem(card.Item with { InstallState = card.InstallState });
        OnPropertyChanged(nameof(SortOptions)); OnPropertyChanged(nameof(OperationStatus)); PublishContentState();
    }
    public void ShowDetails(AppStoreCardViewModel app)
    {
        SelectedApp = app;
        _detailsCts?.Cancel(); _detailsCts = new CancellationTokenSource();
        _ = LoadDetailsAsync(app, _detailsCts);
    }
    private async Task LoadDetailsAsync(AppStoreCardViewModel app, CancellationTokenSource request)
    {
        try { await app.LoadPackageInfoAsync(request.Token); }
        finally { if (ReferenceEquals(_detailsCts, request)) _detailsCts = null; request.Dispose(); }
    }
    public Task LoadCategoryAppsAsync(AppCategory category) => LoadAppsAsync(category, "", false);
    public Task RefreshAsync() => PendingLoad = LoadAppsAsync(SelectedCategory.Category, SearchQuery.Trim(), false);
    private async Task RefreshCatalogAsync()
    {
        if (_disposed) return;
        _searchCts?.Cancel();
        var request = new CancellationTokenSource(); _searchCts = request;
        var version = ++_requestVersion;
        IsLoading = true; _errorMessage = ""; PublishContentState();
        try
        {
            await _service.RefreshCatalogAsync(request.Token);
            if (!_disposed && version == _requestVersion) await RefreshAsync();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_disposed && version == _requestVersion)
                _errorMessage = Loc.Get("The catalog could not be refreshed. You can still browse the last loaded applications.");
        }
        finally
        {
            if (!_disposed && version == _requestVersion) { _searchCts = null; IsLoading = false; }
            request.Dispose();
        }
    }
    private AppStoreCardViewModel Card(AppStoreItem item)
    {
        if (_cards.TryGetValue(item.Id, out var existing)) { existing.UpdateItem(item); return existing; }
        var card = new AppStoreCardViewModel(item, _service);
        card.StateChanged += OnCardStateChanged; _cards[item.Id] = card;
        return card;
    }
    private void OnCardStateChanged(AppStoreCardViewModel card)
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(HasActiveOperations)); OnPropertyChanged(nameof(OperationStatus));
        if (!card.IsBusy && !card.HasError)
        {
            _results = _results.Select(a => a.Id == card.Id ? card.Item with { InstallState = card.InstallState } : a).ToArray();
            if (SelectedCategory.Category == AppCategory.Installed && card.CanOpen && !_results.Any(a => a.Id == card.Id)
                && (string.IsNullOrWhiteSpace(SearchQuery) || $"{card.Name} {card.Summary} {card.Id}".Contains(SearchQuery.Trim(), StringComparison.OrdinalIgnoreCase)))
                _results = _results.Append(card.Item with { InstallState = card.InstallState }).ToArray();
            if (SelectedCategory.Category == AppCategory.Updates) _results = _results.Where(a => a.InstallState == AppInstallState.UpdateAvailable).ToArray();
            if (SelectedCategory.Category == AppCategory.Installed) _results = _results.Where(a => a.InstallState != AppInstallState.NotInstalled).ToArray();
            ApplyResults(resetPage: false);
        }
    }
    private async Task LoadAppsAsync(AppCategory category, string query, bool debounce)
    {
        if (_disposed) return;
        _searchCts?.Cancel(); var request = new CancellationTokenSource(); _searchCts = request;
        var version = ++_requestVersion; var token = request.Token;
        _errorMessage = ""; Apps.Clear(); FeaturedApps.Clear(); _results = []; IsLoading = true;
        try
        {
            if (debounce && query.Length > 0) await Task.Delay(250, token);
            IReadOnlyList<AppStoreItem> results;
            if (category is AppCategory.Installed or AppCategory.Updates)
            {
                results = category == AppCategory.Updates ? await _service.GetUpdatesAsync(token) : await _service.GetInstalledAppsAsync(token);
                if (query.Length > 0) results = results.Where(a => $"{a.Name} {a.Summary} {a.Id}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            }
            else results = query.Length > 0 ? await _service.SearchAppsAsync(query, token) : await _service.GetAppsByCategoryAsync(category, token);
            IReadOnlyList<AppStoreItem> featured = [];
            if (category == AppCategory.Discover && query.Length == 0) featured = await _service.GetFeaturedAppsAsync(token);
            if (_disposed || version != _requestVersion || token.IsCancellationRequested) return;
            _results = results;
            foreach (var item in results) if (_cards.TryGetValue(item.Id, out var card)) card.UpdateItem(item);
            foreach (var item in featured.Take(4)) FeaturedApps.Add(Card(item));
            ApplyResults();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        { if (!_disposed && version == _requestVersion) _errorMessage = Loc.Get(ex is AppStoreException ? ex.Message : "Apps could not be loaded. Check your connection and try again."); }
        finally
        {
            if (!_disposed && version == _requestVersion) { _searchCts = null; IsLoading = false; }
            request.Dispose();
        }
    }
    private IReadOnlyList<AppStoreItem> FilteredResults()
    {
        var results = _results.Where(a => !VerifiedOnly || a.IsVerified);
        if (SortIndex == 1) results = results.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
        return results.ToArray();
    }
    private void ApplyResults(bool resetPage = true)
    {
        if (resetPage) _visibleCount = 24;
        Apps.Clear();
        foreach (var item in FilteredResults().Take(_visibleCount))
        {
            if (!_cards.TryGetValue(item.Id, out var card)) card = Card(item);
            Apps.Add(card);
        }
        PublishContentState();
    }
    private void PublishContentState()
    {
        foreach (var name in new[] { nameof(ErrorMessage), nameof(HasError), nameof(HasCatalogWarning), nameof(ShowEmptyState), nameof(EmptyStateTitle), nameof(EmptyStateDescription), nameof(IsDiscoverMode),
            nameof(PageTitle), nameof(PageDescription), nameof(ResultsLabel), nameof(CollectionTitle), nameof(HasMore), nameof(HasResults), nameof(IsLibrary), nameof(ShowUpdateAll) }) OnPropertyChanged(name);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Loc.Changed -= OnLanguageChanged; ++_requestVersion; _searchCts?.Cancel(); _searchCts = null; _detailsCts?.Cancel();
        foreach (var command in new[] { RetryCommand, RefreshCatalogCommand, UpdateAllCommand }) (command as IDisposable)?.Dispose();
        foreach (var card in _cards.Values) card.StateChanged -= OnCardStateChanged;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
