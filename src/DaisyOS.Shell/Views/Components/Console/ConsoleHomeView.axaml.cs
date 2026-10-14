using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Core.Models.Gaming;
using DaisyOS.Core.Services.Gaming;
using DaisyOS.Shell.ViewModels;
using DaisyOS.System.Gaming;

namespace DaisyOS.Shell.Views.Components.Console;

public partial class ConsoleHomeView : UserControl
{
    public static readonly StyledProperty<string> ControllerNameProperty =
        AvaloniaProperty.Register<ConsoleHomeView, string>(nameof(ControllerName), "Game controller");

    private readonly IGameDiscoveryService _discoveryService;
    private readonly IGameArtworkResolver _artworkResolver;
    private readonly ObservableCollection<ConsoleGameItemViewModel> _recentGames = [];
    private readonly ObservableCollection<ConsoleGameItemViewModel> _allLibraryGames = [];
    private readonly DispatcherTimer _carouselTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly TranslateTransform _carouselTranslation = new();

    private int _selectedIndex;
    private int _librarySelectedIndex;
    private double _carouselOffset;
    private double _targetCarouselOffset;
    private CancellationTokenSource? _discoveryCts;
    private bool _discoveryActive;
    private int _activeBgLayer = 1;
    private string _activeTab = "Recents";
    private bool _isHeaderFocused;
    private int _headerFocusIndex;

    public ObservableCollection<ConsoleGameItemViewModel> RecentGames => _recentGames;
    public ObservableCollection<ConsoleGameItemViewModel> AllLibraryGames => _allLibraryGames;

    public ConsoleHomeView()
        : this(new LinuxGameDiscoveryService(), new GameArtworkResolver())
    {
    }

    public ConsoleHomeView(IGameDiscoveryService discoveryService, IGameArtworkResolver artworkResolver)
    {
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _artworkResolver = artworkResolver ?? throw new ArgumentNullException(nameof(artworkResolver));

        InitializeComponent();
        Loc.RefreshOnChange(this, () =>
        {
            if (_selectedIndex >= 0 && _selectedIndex < _recentGames.Count)
                UpdateSelectedGameSpotlight(_recentGames[_selectedIndex]);
        });
        DataContext = this;
        var carouselTrack = this.FindControl<Control>("RecentGamesList")?.FindDescendantOfType<StackPanel>();
        if (carouselTrack != null)
        {
            carouselTrack.RenderTransform = _carouselTranslation;
        }

        _ = new DaisyOS.Shell.Services.Diagnostics.VisualActivityGate(this, active =>
        {
            if (!active) _carouselTimer.Stop();
            SetDiscoveryActive(active);
        });
        _carouselTimer.Tick += (_, _) => AdvanceCarousel();

        _artworkResolver.ArtworkUpdated += OnArtworkUpdated;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public event EventHandler<string>? DestinationRequested;
    public event EventHandler<ConsoleGameItemViewModel>? GameLaunchRequested;
    public event EventHandler<double>? SelectionChanged;
    public event EventHandler? SettingsRequested;

    public void SetControllerLayout(string? name)
    {
        var type = ControllerConnectionStatus.DetectType(name);
        ControllerName = string.IsNullOrWhiteSpace(name)
            ? (type == ControllerType.PlayStation ? "PlayStation Controller" : type == ControllerType.Xbox ? "Xbox Controller" : "Game controller")
            : name;

        ActionBar?.SetControllerLayout(type);
        HeaderBar?.UpdateControllerInfo(type, ControllerName);
    }

    public void SetControllerLayout(ControllerConnectionStatus status)
    {
        SetControllerLayout(status?.Name);
    }

    public string ControllerName
    {
        get => GetValue(ControllerNameProperty);
        set
        {
            SetValue(ControllerNameProperty, value);
            var type = ControllerConnectionStatus.DetectType(value);
            HeaderBar?.UpdateControllerInfo(type, value);
        }
    }

    public double ParallaxPosition => _recentGames.Count <= 1
        ? 0
        : _selectedIndex / (double)(_recentGames.Count - 1) * 2 - 1;

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        HeaderBar?.UpdateClock();
    }

    private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SetDiscoveryActive(false);

    private void SetDiscoveryActive(bool active)
    {
        if (_discoveryActive == active) return;
        _discoveryActive = active;
        _discoveryCts?.Cancel();
        _discoveryCts?.Dispose();
        _discoveryCts = null;
        if (!active) return;
        _discoveryCts = new CancellationTokenSource();
        _ = LoadGamesAsync(_discoveryCts.Token);
    }

    private async Task LoadGamesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var discovered = await _discoveryService.DiscoverGamesAsync(cancellationToken).ConfigureAwait(true);
            if (discovered.Count == 0)
            {
                discovered = [
                    new GameIdentity("Cyberpunk 2077", "Cyberpunk 2077", GameStoreSource.Steam, "1091500", Categories: ["Game"]),
                    new GameIdentity("The Witcher 3: Wild Hunt", "The Witcher 3: Wild Hunt", GameStoreSource.Steam, "292030", Categories: ["Game"]),
                    new GameIdentity("Hades", "Hades", GameStoreSource.Steam, "1145360", Categories: ["Game"]),
                    new GameIdentity("Portal 2", "Portal 2", GameStoreSource.Steam, "620", Categories: ["Game"])
                ];
            }

            cancellationToken.ThrowIfCancellationRequested();
            _recentGames.Clear();
            _allLibraryGames.Clear();

            // Populate Recents (First 6 items)
            foreach (var game in discovered.Take(6))
            {
                var vm = new ConsoleGameItemViewModel(game);
                _recentGames.Add(vm);
            }

            // Populate All Library Games
            foreach (var game in discovered)
            {
                var vm = new ConsoleGameItemViewModel(game);
                _allLibraryGames.Add(vm);
            }

            LibraryCountText.Text = Loc.Format("{0} Games", discovered.Count);

            if (_recentGames.Count > 0)
            {
                _selectedIndex = 0;
                ApplySelection();
            }

            // Hook up the transform now that the ItemsControl has likely created its panel
            Dispatcher.UIThread.Post(() =>
            {
                var carouselTrack = this.FindControl<ItemsControl>("RecentGamesList")?.FindDescendantOfType<StackPanel>();
                if (carouselTrack != null)
                {
                    carouselTrack.RenderTransform = _carouselTranslation;
                }
            }, DispatcherPriority.Loaded);

            // Request Artwork
            foreach (var vm in _recentGames.Concat(_allLibraryGames))
            {
                _ = RequestArtworkAsync(vm, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to discover games: {ex.Message}");
        }
    }

    private async Task RequestArtworkAsync(ConsoleGameItemViewModel vm, CancellationToken cancellationToken)
    {
        try
        {
            var assets = await _artworkResolver.GetArtworkAsync(vm.Game, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            vm.ApplyArtworkAssets(assets);

            if (_recentGames.Count > 0 && _recentGames[_selectedIndex] == vm)
            {
                UpdateSelectedGameSpotlight(vm);
            }
        }
        catch
        {
            // Best effort
        }
    }

    private void OnArtworkUpdated(object? sender, ArtworkChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var matchRecent = _recentGames.FirstOrDefault(g => g.Game.Title == e.Game.Title && g.Game.Source == e.Game.Source);
            if (matchRecent != null)
            {
                matchRecent.ApplyArtworkAssets(e.Assets);
                if (_recentGames.Count > 0 && _recentGames[_selectedIndex] == matchRecent)
                {
                    UpdateSelectedGameSpotlight(matchRecent);
                }
            }

            var matchLib = _allLibraryGames.FirstOrDefault(g => g.Game.Title == e.Game.Title && g.Game.Source == e.Game.Source);
            matchLib?.ApplyArtworkAssets(e.Assets);
        });
    }

    private void OnGameTileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ConsoleGameItemViewModel vm)
        {
            _isHeaderFocused = false;
            
            var index = _recentGames.IndexOf(vm);
            if (index >= 0)
            {
                _selectedIndex = index;
                ApplySelection();
            }
            else
            {
                _librarySelectedIndex = _allLibraryGames.IndexOf(vm);
            }
            if (vm != null)
            {
                GameLaunchRequested?.Invoke(this, vm);
                DestinationRequested?.Invoke(this, vm.Title);
            }
        }
    }

    public void Navigate(ControllerNavigationAction action)
    {
        if (action == ControllerNavigationAction.PreviousSection)
        {
            var tabs = new[] { "Recents", "Library" };
            int idx = Array.IndexOf(tabs, _activeTab);
            if (idx < 0) idx = 0;
            int nextIdx = (idx - 1 + tabs.Length) % tabs.Length;
            OnHeaderTabSelected(this, tabs[nextIdx]);
            _headerFocusIndex = nextIdx;
            if (_isHeaderFocused)
            {
                FocusHeaderButton(_headerFocusIndex);
            }
            else
            {
                FocusActiveTabContent();
            }
            return;
        }
        else if (action == ControllerNavigationAction.NextSection)
        {
            var tabs = new[] { "Recents", "Library" };
            int idx = Array.IndexOf(tabs, _activeTab);
            if (idx < 0) idx = 0;
            int nextIdx = (idx + 1) % tabs.Length;
            OnHeaderTabSelected(this, tabs[nextIdx]);
            _headerFocusIndex = nextIdx;
            if (_isHeaderFocused)
            {
                FocusHeaderButton(_headerFocusIndex);
            }
            else
            {
                FocusActiveTabContent();
            }
            return;
        }

        if (_isHeaderFocused)
        {
            switch (action)
            {
                case ControllerNavigationAction.Left:
                    _headerFocusIndex = Math.Max(0, _headerFocusIndex - 1);
                    FocusHeaderButton(_headerFocusIndex);
                    break;

                case ControllerNavigationAction.Right:
                    _headerFocusIndex = Math.Min(5, _headerFocusIndex + 1);
                    FocusHeaderButton(_headerFocusIndex);
                    break;

                case ControllerNavigationAction.Down:
                    _isHeaderFocused = false;
                    FocusActiveTabContent();
                    break;

                case ControllerNavigationAction.Confirm:
                    if (_headerFocusIndex == 0) HeaderBar?.MediaPrevButton?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    else if (_headerFocusIndex == 1) HeaderBar?.MediaPlayButton?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    else if (_headerFocusIndex == 2) HeaderBar?.MediaNextButton?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    else if (_headerFocusIndex == 3) OnHeaderTabSelected(this, "Recents");
                    else if (_headerFocusIndex == 4) OnHeaderTabSelected(this, "Library");
                    else if (_headerFocusIndex == 5) SettingsRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
            return;
        }

        if (_activeTab == "Recents")
        {
            if (_recentGames.Count == 0) return;

            switch (action)
            {
                case ControllerNavigationAction.Left:
                    _selectedIndex = Math.Max(0, _selectedIndex - 1);
                    ApplySelection();
                    break;

                case ControllerNavigationAction.Right:
                    _selectedIndex = Math.Min(_recentGames.Count - 1, _selectedIndex + 1);
                    ApplySelection();
                    break;

                case ControllerNavigationAction.Up:
                    _isHeaderFocused = true;
                    _headerFocusIndex = 3;
                    FocusHeaderButton(_headerFocusIndex);
                    break;

                case ControllerNavigationAction.Confirm:
                    if (_selectedIndex < _recentGames.Count)
                    {
                        var vm = _recentGames[_selectedIndex];
                        GameLaunchRequested?.Invoke(this, vm);
                        DestinationRequested?.Invoke(this, vm.Title);
                    }
                    break;
            }
        }
        else if (_activeTab == "Library")
        {
            if (_allLibraryGames.Count == 0) return;

            int columns = 5;
            switch (action)
            {
                case ControllerNavigationAction.Left:
                    _librarySelectedIndex = Math.Max(0, _librarySelectedIndex - 1);
                    FocusLibraryCard(_librarySelectedIndex);
                    break;

                case ControllerNavigationAction.Right:
                    _librarySelectedIndex = Math.Min(_allLibraryGames.Count - 1, _librarySelectedIndex + 1);
                    FocusLibraryCard(_librarySelectedIndex);
                    break;

                case ControllerNavigationAction.Down:
                    _librarySelectedIndex = Math.Min(_allLibraryGames.Count - 1, _librarySelectedIndex + columns);
                    FocusLibraryCard(_librarySelectedIndex);
                    break;

                case ControllerNavigationAction.Up:
                    if (_librarySelectedIndex < columns)
                    {
                        // Moving up from top row focuses Header
                        _isHeaderFocused = true;
                        _headerFocusIndex = 4;
                        FocusHeaderButton(_headerFocusIndex);
                    }
                    else
                    {
                        _librarySelectedIndex = Math.Max(0, _librarySelectedIndex - columns);
                        FocusLibraryCard(_librarySelectedIndex);
                    }
                    break;

                case ControllerNavigationAction.Confirm:
                    if (_librarySelectedIndex < _allLibraryGames.Count)
                    {
                        var vm = _allLibraryGames[_librarySelectedIndex];
                        GameLaunchRequested?.Invoke(this, vm);
                        DestinationRequested?.Invoke(this, vm.Title);
                    }
                    break;
            }
        }
    }

    private void FocusHeaderButton(int index)
    {
        if (HeaderBar == null) return;
        switch (index)
        {
            case 0:
                HeaderBar.MediaPrevButton?.Focus();
                break;
            case 1:
                HeaderBar.MediaPlayButton?.Focus();
                break;
            case 2:
                HeaderBar.MediaNextButton?.Focus();
                break;
            case 3:
                HeaderBar.RecentButton?.Focus();
                break;
            case 4:
                HeaderBar.LibraryButton?.Focus();
                break;
            case 5:
                HeaderBar.SettingsButton?.Focus();
                break;
        }
    }

    private void FocusActiveTabContent()
    {
        if (_activeTab == "Library")
        {
            FocusLibraryCard(_librarySelectedIndex);
        }
        else
        {
            _activeTab = "Recents";
            var list = this.FindControl<ItemsControl>("RecentGamesList");
            if (list != null && _recentGames.Count > 0)
            {
                var container = list.ContainerFromIndex(Math.Min(_selectedIndex, _recentGames.Count - 1));
                container?.FindDescendantOfType<Button>()?.Focus();
            }
        }
    }

    private void FocusLibraryCard(int index)
    {
        if (index >= 0 && index < _allLibraryGames.Count)
        {
            var list = this.FindControl<ItemsControl>("LibraryGrid");
            if (list != null)
            {
                var container = list.ContainerFromIndex(index);
                container?.FindDescendantOfType<Button>()?.Focus();
            }
        }
    }

    public void FocusInitialDestination()
    {
        _isHeaderFocused = false;
        FocusActiveTabContent();
    }

    private void OnHeaderTabSelected(object? sender, string tabName)
    {
        SetActiveTab(tabName);
        if (!_isHeaderFocused)
        {
            FocusActiveTabContent();
        }
    }

    private void OnHeaderSettingsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetActiveTab(string tabName)
    {
        _activeTab = tabName == "Library" ? "Library" : "Recents";

        if (RecentsTabContent != null) RecentsTabContent.Classes.Set("ActiveTab", _activeTab == "Recents");
        if (LibraryTabContent != null) LibraryTabContent.Classes.Set("ActiveTab", _activeTab == "Library");

        HeaderBar?.SetActiveTab(_activeTab);
    }

    private void ApplySelection()
    {
        if (_recentGames.Count == 0 || _selectedIndex < 0 || _selectedIndex >= _recentGames.Count) return;

        _targetCarouselOffset = -_selectedIndex * 238;
        if (!_carouselTimer.IsEnabled)
        {
            _carouselTimer.Start();
        }

        if (!_isHeaderFocused && _activeTab == "Recents")
        {
            var list = this.FindControl<ItemsControl>("RecentGamesList");
            if (list != null)
            {
                var container = list.ContainerFromIndex(_selectedIndex);
                container?.FindDescendantOfType<Button>()?.Focus();
            }
        }

        var activeGame = _recentGames[_selectedIndex];
        UpdateSelectedGameSpotlight(activeGame);

        SelectionChanged?.Invoke(this, ParallaxPosition);
    }

    public void UpdateSelectedGameSpotlight(ConsoleGameItemViewModel vm)
    {
        SelectedGameTitleText.Text = vm.Title;
        SelectedGameSourceText.Text = vm.SourceText.ToUpperInvariant();
        
        string prefix = _activeTab == "Recents" ? Loc.Get("Recently Played • ") : Loc.Get("Library • ");
        
        if (vm.IsRunning)
        {
            SelectedGameSubtext.Text = prefix + Loc.Get("Running");
            SelectedGameSubtext.Foreground = Avalonia.Media.Brushes.LimeGreen;
        }
        else
        {
            SelectedGameSubtext.Text = prefix + Loc.Get("Ready to play");
            SelectedGameSubtext.Foreground = Avalonia.Media.Brushes.White;
        }

        var bgImage = vm.HeroImage ?? vm.CoverImage ?? vm.LogoImage;
        
        var currentLayer = _activeBgLayer == 1 ? HeroBackgroundImage1 : HeroBackgroundImage2;
        var nextLayer = _activeBgLayer == 1 ? HeroBackgroundImage2 : HeroBackgroundImage1;

        if (bgImage != null)
        {
            nextLayer.Source = bgImage;
            nextLayer.Opacity = 0.45;
            currentLayer.Opacity = 0.0;
            _activeBgLayer = _activeBgLayer == 1 ? 2 : 1;
        }
        else
        {
            currentLayer.Opacity = 0.0;
            nextLayer.Opacity = 0.0;
        }
    }

    private void AdvanceCarousel()
    {
        _carouselOffset += (_targetCarouselOffset - _carouselOffset) * 0.24;
        _carouselTranslation.X = _carouselOffset;
        if (Math.Abs(_targetCarouselOffset - _carouselOffset) < 0.05)
        {
            _carouselOffset = _targetCarouselOffset;
            _carouselTranslation.X = _carouselOffset;
            _carouselTimer.Stop();
        }
    }
}
