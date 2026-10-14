using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    private readonly LinuxAppLauncherService _launcherService;
    private readonly bool _externalDiscoveryEnabled;
    private readonly ShellSessionState _sessionState;
    private string _searchText = string.Empty;

    public FastObservableCollection<LauncherItemViewModel> AllApps { get; } = new();
    public FastObservableCollection<LauncherItemViewModel> FilteredApps { get; } = new();
    public FastObservableCollection<LauncherItemViewModel> MostUsedApps { get; } = new();

    public LauncherItemViewModel? TopSearchResult { get; private set; }

    public string UserName { get; } = Environment.UserName is { Length: > 0 } name 
        ? char.ToUpperInvariant(name[0]) + name[1..] 
        : "User";
    public string UserInitial => UserName.Length > 0 ? UserName[..1] : "U";

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchText);
    public bool ShowNoResults => HasSearchQuery && FilteredApps.Count == 0;
    public bool HasMostUsedApps => MostUsedApps.Count > 0;
    public Task ApplicationsReady { get; }

    private int _searchGeneration;

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
            Dispatcher.UIThread.Post(() =>
            {
                if (generation == _searchGeneration) UpdateFilteredApps();
            }, DispatcherPriority.Input);
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
        UpdateMostUsedApps();
        UpdateFilteredApps();
    }

    private void UpdateFilteredApps()
    {
        using var trace = ResponsivenessTrace.Measure("launcher.search.projection");
        var matching = AllApps.Where(MatchesSearch).ToList();

        if (!FilteredApps.SequenceEqual(matching))
            FilteredApps.ReplaceAll(matching);

        if (TopSearchResult is not null) TopSearchResult.IsTopSearchResult = false;
        TopSearchResult = HasSearchQuery ? FilteredApps.FirstOrDefault() : null;
        if (TopSearchResult is not null)
        {
            TopSearchResult.IsTopSearchResult = true;
        }

        OnPropertyChanged(nameof(TopSearchResult));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    // Enter can arrive before the queued search projection. Resolve against
    // the current query so a fast typist never launches the previous result.
    public LauncherItemViewModel? ResolveSearchResult()
    {
        ++_searchGeneration;
        UpdateFilteredApps();
        return TopSearchResult;
    }

    private void UpdateMostUsedApps()
    {
        var mostUsed = new List<LauncherItemViewModel>();

        var appsById = AllApps
            .Where(app => !string.IsNullOrWhiteSpace(app.App.Id))
            .GroupBy(app => app.App.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var applicationId in _sessionState.GetMostUsedApplicationIds(maximumCount: 7))
        {
            if (appsById.TryGetValue(applicationId, out var app))
            {
                mostUsed.Add(app);
            }
        }

        foreach (var app in AllApps)
        {
            if (mostUsed.Count >= 7) break;
            if (!mostUsed.Contains(app))
            {
                mostUsed.Add(app);
            }
        }

        MostUsedApps.ReplaceAll(mostUsed);
        OnPropertyChanged(nameof(HasMostUsedApps));
    }

    private bool MatchesSearch(LauncherItemViewModel item)
    {
        if (!HasSearchQuery)
        {
            return true;
        }

        return item.SearchKey.Contains(SearchText.Trim().ToUpperInvariant(), StringComparison.Ordinal);
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
        UpdateMostUsedApps();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
