using DaisyOS.Shell.Services.Motion;
using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using DaisyOS.Shell;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Audio;
using DaisyOS.System.Bluetooth;
using DaisyOS.System.Networking;
using DaisyOS.System.Processes;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Shell;
using DaisyOS.Shell.Views.Dialogs;
using DaisyOS.System.Power;

namespace DaisyOS.Shell.Views.Components.QuickSettings;

public partial class QuickSettings : UserControl
{
    private readonly IAudioService _audioService = new LinuxAudioService(new SafeCommandRunner());
    private readonly IWirelessNetworkService _wirelessNetworkService = new LinuxWirelessNetworkService(new SafeCommandRunner());
    private readonly INetworkStatusService _networkStatusService = new LinuxNetworkStatusService(new SafeCommandRunner());
    private readonly IBluetoothService _bluetoothService = new LinuxBluetoothService(new SafeCommandRunner());
    private readonly IBatteryStatusService _batteryService = new LinuxBatteryStatusService();
    private LinuxAudioChangeMonitor? _audioChangeMonitor;
    private readonly ShellSessionState _sessionState = (Application.Current as App)?.SessionState ?? new ShellSessionState();
    private CancellationTokenSource? _volumeUpdateCancellation;
    private bool _isUpdatingVolumeFromService;
    private bool _hasLoadedVolume;
    private Task? _volumeRefreshTask;
    private bool _volumeRefreshPending;
    private string? _demoConnectedWifi = "DaisyOS Guest";
    private Task? _statusRefreshTask;
    private bool _airPodsModePending;
    private bool _bluetoothDiscoveryPending;
    private readonly DispatcherTimer _connectionListTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private QuickSettingsPage _currentPage;
    private CancellationTokenSource? _quickBluetoothDiscoveryLifetime;
    private Task? _automaticWifiRefresh;
    private Task? _automaticBluetoothRefresh;
    private readonly Dictionary<string, WirelessNetworkInfo> _wifiNetworkCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BluetoothDevice> _bluetoothDeviceCache = new(StringComparer.OrdinalIgnoreCase);
    private WirelessNetworkStatus? _lastWifiStatus;
    private BluetoothStatus? _lastBluetoothStatus;
    private Task? _wifiScanRefresh;
    private bool _bluetoothAvailable = true;
    private string _defaultAudioName = "";
    private bool HasConnectedAirPods => (Application.Current as App)?.AirPods?.Status.IsConnected == true;

    public event EventHandler? DismissRequested;
    public event EventHandler? ConsoleModeRequested;
    public event Action<NetworkStatus, WirelessRadioStatus, BluetoothStatus>? NetworkStatusChanged;
    public event Action? NetworkStatusUnavailable;
    public Border MaterialSurface => this.FindControl<Border>("MaterialFrame")!;

    public QuickSettings()
    {
        InitializeComponent();
        _connectionListTimer.Tick += (_, _) => RefreshVisibleConnectionList();
        Loc.RefreshOnChange(this, () => { ApplyLocalSettings(); RefreshStatus(); });
        Loaded += (_, _) =>
        {
            _audioChangeMonitor ??= new LinuxAudioChangeMonitor();
            _audioChangeMonitor.Changed -= OnAudioChanged;
            _audioChangeMonitor.Changed += OnAudioChanged;
            if (Application.Current is App app)
            {
                app.Notifications.DoNotDisturbChanged += OnDoNotDisturbChanged;
                app.LocalSendReceivingChanged += OnLocalSendReceivingChanged;
                if (app.AirPods is { } pods) pods.Changed += UpdateAirPodsPresentation;
            }
            ApplyLocalSettings();
            RefreshStatus();
        };
        Unloaded += (_, _) =>
        {
            _connectionListTimer.Stop();
            StopQuickBluetoothDiscovery();
            _volumeUpdateCancellation?.Cancel();
            if (_audioChangeMonitor is { } audioChangeMonitor)
            {
                audioChangeMonitor.Changed -= OnAudioChanged;
                audioChangeMonitor.Dispose();
                _audioChangeMonitor = null;
            }
            if (Application.Current is App app)
            {
                app.Notifications.DoNotDisturbChanged -= OnDoNotDisturbChanged;
                app.LocalSendReceivingChanged -= OnLocalSendReceivingChanged;
                if (app.AirPods is { } pods) pods.Changed -= UpdateAirPodsPresentation;
            }
        };
    }

    public void ShowPanel()
    {
        IsVisible = true;
        ApplyLocalSettings();
        SetQuickSettingsPage(QuickSettingsPage.Main);
        RefreshStatus();
        WarmConnectionLists();
        _ = RefreshContinuityQuickControlAsync();
    }

    private async Task RefreshContinuityQuickControlAsync()
    {
        var button = this.FindControl<Button>("ContinuityPauseButton");
        if (button is null) return;
        try
        {
            var state = await new DaisyOS.System.Continuity.ContinuityClient().ExecuteAsync(new("status"));
            button.IsVisible = state.Enabled;
            button.Content = state.Paused ? Loc.Get("Resume Continuity") : Loc.Get("Pause Continuity");
        }
        catch { button.IsVisible = false; }
    }

    private async void OnContinuityPauseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        button.IsEnabled = false;
        try
        {
            var client = new DaisyOS.System.Continuity.ContinuityClient();
            var state = await client.ExecuteAsync(new("status"));
            await client.ExecuteAsync(new("pause", Allowed: !state.Paused));
            await RefreshContinuityQuickControlAsync();
        }
        catch { (Application.Current as App)?.Feedback.Show(Loc.Get("Continuity service is unavailable.")); }
        finally { button.IsEnabled = true; }
    }

    private void WarmConnectionLists()
    {
        if (_automaticWifiRefresh is not { IsCompleted: false })
            _automaticWifiRefresh = LoadWifiNetworksAsync();
        if (_automaticBluetoothRefresh is not { IsCompleted: false })
            _automaticBluetoothRefresh = LoadBluetoothDevicesAsync();
    }

    private void ApplyLocalSettings()
    {
        if (this.FindControl<QuickSettingTile>("ThemeTile") is { } themeTile)
        {
            themeTile.IsChecked = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
            themeTile.ToolTipText = Loc.Get(themeTile.IsChecked ? "Use light theme" : "Use dark theme");
        }
        if (this.FindControl<QuickSettingTile>("GamingTile") is { } gamingTile)
        {
            gamingTile.IsChecked = _sessionState.GamingMode;
        }
        if (this.FindControl<QuickSettingTile>("LocalSendTile") is { } localSendTile)
        {
            localSendTile.IsChecked = (Application.Current as App)?.IsLocalSendReceivingEnabled == true;
        }
        if (this.FindControl<QuickSettingTile>("NightLightTile") is { } nightLightTile)
        {
            var isNightLight = (Application.Current as App)?.GetPreference("display.night-light", "false") == "true";
            nightLightTile.IsChecked = isNightLight;
            nightLightTile.ToolTipText = Loc.Get(isNightLight ? "Turn off night light" : "Turn on night light");
        }
        if (this.FindControl<QuickSettingTile>("DndTile") is { } dndTile)
        {
            var isDnd = (Application.Current as App)?.SessionState.DoNotDisturb == true;
            dndTile.IsChecked = isDnd;
            dndTile.ToolTipText = Loc.Get(isDnd ? "Turn off Do Not Disturb" : "Turn on Do Not Disturb");
        }
        if (this.FindControl<QuickSettingsSlider>("VolumeSlider") is { } volumeSlider)
        {
            // Do not overwrite a live hardware value whenever local settings are
            // reapplied (for example while opening the panel).
            if (!_hasLoadedVolume)
            {
                _isUpdatingVolumeFromService = true;
                try
                {
                    volumeSlider.IsEnabled = true;
                    volumeSlider.Value = _sessionState.LastKnownVolume ?? 0;
                }
                finally
                {
                    _isUpdatingVolumeFromService = false;
                }
            }
        }
        UpdateQuickSettingsTileLayout();
        UpdateAirPodsPresentation();
    }

    private void OnDoNotDisturbChanged(object? sender, bool isDnd)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<QuickSettingTile>("DndTile") is { } dndTile)
            {
                dndTile.IsChecked = isDnd;
                dndTile.ToolTipText = Loc.Get(isDnd ? "Turn off Do Not Disturb" : "Turn on Do Not Disturb");
            }
        });
    }

    private void OnLocalSendReceivingChanged(object? sender, bool isReceiving)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<QuickSettingTile>("LocalSendTile") is { } localSendTile)
            {
                localSendTile.IsChecked = isReceiving;
            }
        });
    }

    private void OnLocalSendTileToggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is QuickSettingTile tile && Application.Current is App app)
        {
            app.SetLocalSendReceivingEnabled(tile.IsChecked);
            // A service that is unavailable leaves the control off rather
            // than presenting an optimistic state that cannot receive files.
            tile.IsChecked = app.IsLocalSendReceivingEnabled;
        }
    }

    private async void OnThemeTileToggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not QuickSettingTile tile || Application.Current is not App app)
        {
            return;
        }

        await app.SetShellThemeAsync(tile.IsChecked ? ThemeVariant.Dark : ThemeVariant.Light);
    }

    private async void OnWifiTileToggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not QuickSettingTile tile) return;
        if (!await _wirelessNetworkService.SetEnabledAsync(tile.IsChecked))
        {
            tile.IsChecked = !tile.IsChecked;
            ReportFailure("Couldn’t change Wi-Fi. Check that NetworkManager is available.");
        }
        RefreshStatus();
    }

    private async void OnBluetoothTileToggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not QuickSettingTile tile) return;
        if (!await _bluetoothService.SetEnabledAsync(tile.IsChecked))
        {
            tile.IsChecked = !tile.IsChecked;
            ReportFailure(tile.IsChecked ? "Couldn’t turn off Bluetooth." : "Couldn’t turn on Bluetooth. Run 'sudo systemctl enable --now bluetooth' in terminal.");
        }
        RefreshStatus();
    }

    private void OnGamingTileToggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is QuickSettingTile tile) _sessionState.GamingMode = tile.IsChecked;
    }

    private void OnGamingDetailsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ConsoleModeRequested?.Invoke(this, EventArgs.Empty);

    private async void OnNightLightTileToggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not QuickSettingTile tile || Application.Current is not App app) return;
        var isEnabled = tile.IsChecked;
        app.SessionState.SetPreference("display.night-light", isEnabled ? "true" : "false");
        var tempStr = app.GetPreference("display.night-light-temp", "4000");
        uint.TryParse(tempStr, out var temp);
        if (temp == 0) temp = 4000;
        tile.ToolTipText = Loc.Get(isEnabled ? "Turn off night light" : "Turn on night light");
        try
        {
            await new DaisyOS.System.Display.LinuxNightLightService().SetNightLightAsync(isEnabled, temp);
        }
        catch
        {
            // Ignored if compositor does not support DBus NightLight in current session
        }
    }

    private void OnDndTileToggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not QuickSettingTile tile || Application.Current is not App app) return;
        app.SetDoNotDisturb(tile.IsChecked);
        tile.ToolTipText = Loc.Get(tile.IsChecked ? "Turn off Do Not Disturb" : "Turn on Do Not Disturb");
    }

    private async void OnVolumeChanged(object? sender, EventArgs e)
    {
        if (sender is not QuickSettingsSlider slider || !slider.IsLoaded || _isUpdatingVolumeFromService) return;
        _volumeUpdateCancellation?.Cancel();
        _volumeUpdateCancellation?.Dispose();
        _volumeUpdateCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(80, _volumeUpdateCancellation.Token);
            await _audioService.SetVolumeAsync(slider.Value, _volumeUpdateCancellation.Token);
            _sessionState.LastKnownVolume = slider.Value;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ReportFailure("Couldn’t change the volume.");
        }
    }

    private void OnLockClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DismissRequested?.Invoke(this, EventArgs.Empty);
        (Application.Current as App)?.LockSession();
    }

    private void OnSettingsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DismissRequested?.Invoke(this, EventArgs.Empty);
        (Application.Current as App)?.ShowSettings();
    }

    private void OnNetworkSettingsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DismissRequested?.Invoke(this, EventArgs.Empty);
        (Application.Current as App)?.ShowSettings();
    }

    private void OnEthernetTileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Ethernet is a connection status rather than a radio switch. Keep its active
        // presentation stable and send either half of the split control to Network Settings.
        if (sender is QuickSettingTile ethernetTile) ethernetTile.IsChecked = true;
        OnNetworkSettingsClicked(sender, e);
    }

    public void RefreshStatus()
    {
        if (_statusRefreshTask is { IsCompleted: false }) return;
        _statusRefreshTask = RefreshStatusAsync();
    }

    private void OnAudioChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(QueueVolumeRefresh, DispatcherPriority.Input);

    private void QueueVolumeRefresh()
    {
        if (_volumeRefreshTask is { IsCompleted: false })
        {
            _volumeRefreshPending = true;
            return;
        }
        _volumeRefreshTask = RefreshVolumeAsync();
    }

    private async Task RefreshVolumeAsync()
    {
        do
        {
            _volumeRefreshPending = false;
            try
            {
                ApplyVolume(await _audioService.GetVolumeAsync());
            }
            catch
            {
                // A transient audio-server restart should retain the last valid value.
            }
        } while (_volumeRefreshPending);
    }

    private void ApplyVolume(double? volume)
    {
        if (this.FindControl<QuickSettingsSlider>("VolumeSlider") is not { } volumeSlider) return;
        _isUpdatingVolumeFromService = true;
        try
        {
            volumeSlider.IsEnabled = volume is not null;
            if (volume is { } current)
            {
                volumeSlider.Value = current;
                _sessionState.LastKnownVolume = current;
                _hasLoadedVolume = true;
            }
        }
        finally
        {
            _isUpdatingVolumeFromService = false;
        }
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            // Status queries are bounded and do not trigger a Wi-Fi rescan, so the shell
            // can remain current without blocking interaction or waiting for a flyout.
            var networkTask = _networkStatusService.GetStatusAsync();
            var wifiTask = _wirelessNetworkService.GetRadioStatusAsync();
            var bluetoothTask = _bluetoothService.GetAdapterStatusAsync();
            var batteryTask = _batteryService.GetStatusAsync();
            var volumeTask = _audioService.GetVolumeAsync();
            var audioDevicesTask = _audioService.GetAudioDevicesAsync();
            await Task.WhenAll(networkTask, wifiTask, bluetoothTask, batteryTask, volumeTask, audioDevicesTask);

            var network = await networkTask;
            var wifi = await wifiTask;
            var bluetooth = await bluetoothTask;
            var battery = await batteryTask;
            var volume = await volumeTask;
            _defaultAudioName = (await audioDevicesTask).FirstOrDefault(device => device.IsDefault)?.Name ?? "";
            UpdateNetworkPresentation(network, wifi, bluetooth);
            if (this.FindControl<QuickSettingTile>("LocalSendTile") is { } localSendTile) localSendTile.IsChecked = (Application.Current as App)?.IsLocalSendReceivingEnabled == true;
            if (this.FindControl<QuickSettingTile>("GamingTile") is { } gamingTile) gamingTile.IsChecked = _sessionState.GamingMode;
            if (this.FindControl<QuickSettingTile>("NightLightTile") is { } nightLightTile)
            {
                var isNightLight = (Application.Current as App)?.GetPreference("display.night-light", "false") == "true";
                nightLightTile.IsChecked = isNightLight;
                nightLightTile.ToolTipText = Loc.Get(isNightLight ? "Turn off night light" : "Turn on night light");
            }
            if (this.FindControl<QuickSettingTile>("DndTile") is { } dndTile)
            {
                var isDnd = (Application.Current as App)?.SessionState.DoNotDisturb == true;
                dndTile.IsChecked = isDnd;
                dndTile.ToolTipText = Loc.Get(isDnd ? "Turn off Do Not Disturb" : "Turn on Do Not Disturb");
            }
            if (this.FindControl<QuickSettingTile>("ThemeTile") is { } themeTile)
            {
                themeTile.IsChecked = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
                themeTile.ToolTipText = Loc.Get(themeTile.IsChecked ? "Use light theme" : "Use dark theme");
            }
            ApplyVolume(volume);
            UpdateBatteryPresentation(battery);
        }
        catch
        {
            NetworkStatusUnavailable?.Invoke();
            if (this.FindControl<QuickSettingsSlider>("VolumeSlider") is { } volumeSlider) volumeSlider.IsEnabled = false;
        }
    }

    internal void UpdateBatteryPresentation(BatteryStatus battery)
    {
        if (this.FindControl<TextBlock>("BatteryText") is { } batteryText)
        {
            batteryText.Text = battery.DisplayText;
            ToolTip.SetTip(batteryText, battery.Detail);
        }
        if (this.FindControl<StackPanel>("BatteryStatusHost") is { } batteryStatusHost)
        {
            // A desktop on mains power has no battery status to communicate. Keeping the
            // footer quiet is clearer than presenting an unavailable hardware warning.
            batteryStatusHost.IsVisible = battery.IsPresent;
        }
    }

    private void UpdateQuickSettingsTileLayout()
    {
        var grid = this.FindControl<Grid>("QuickSettingsTiles");
        if (grid is null) return;

        var tiles = new[]
        {
            this.FindControl<QuickSettingTile>("WifiTile"),
            this.FindControl<QuickSettingTile>("EthernetTile"),
            this.FindControl<QuickSettingTile>("DndTile"),
            this.FindControl<QuickSettingTile>("LocalSendTile"),
            this.FindControl<QuickSettingTile>("NightLightTile"),
            this.FindControl<QuickSettingTile>("BluetoothTile"),
            this.FindControl<QuickSettingTile>("GamingTile")
        }.Where(tile => tile?.IsVisible == true).Cast<QuickSettingTile>().ToArray();

        grid.RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("Auto", Math.Max(1, (int)Math.Ceiling(tiles.Length / 3d)))));
        for (var index = 0; index < tiles.Length; index++)
        {
            Grid.SetRow(tiles[index], index / 3);
            Grid.SetColumn(tiles[index], index % 3);
        }
    }

    private async void OnOutputDevicesButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetQuickSettingsPage(QuickSettingsPage.OutputDevices);
        await LoadOutputDevicesAsync();
    }

    private void OnOutputDevicesBackClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SetQuickSettingsPage(QuickSettingsPage.Main);

    private async void OnWifiNetworksButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetQuickSettingsPage(QuickSettingsPage.WifiNetworks);
        await LoadWifiNetworksAsync();
        if (_wifiScanRefresh is not { IsCompleted: false })
            _wifiScanRefresh = ScanAndRefreshWifiAsync();
    }

    private void OnWifiNetworksBackClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SetQuickSettingsPage(QuickSettingsPage.Main);

    private async void OnBluetoothDevicesButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetQuickSettingsPage(QuickSettingsPage.BluetoothDevices);
        await LoadBluetoothDevicesAsync();
    }

    private void OnBluetoothDevicesBackClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SetQuickSettingsPage(QuickSettingsPage.Main);

    private async Task LoadOutputDevicesAsync()
    {
        var host = this.FindControl<StackPanel>("OutputDevicesHost");
        if (host is null)
        {
            return;
        }

        host.Children.Clear();
        host.Children.Add(Loc.Bind(new TextBlock
        {
            Text = "Loading devices…",
            Foreground = this.FindResource("ContentSecondaryBrush") as IBrush,
            Margin = new Avalonia.Thickness(10, 8)
        }, TextBlock.TextProperty, "Loading devices…"));

        IReadOnlyList<AudioDeviceInfo> devices;
        try
        {
            devices = await _audioService.GetAudioDevicesAsync();
        }
        catch
        {
            devices = Array.Empty<AudioDeviceInfo>();
        }

        host.Children.Clear();
        if (devices.Count == 0)
        {
            host.Children.Add(Loc.Bind(new TextBlock
            {
                Text = "No output devices available",
                Foreground = this.FindResource("ContentSecondaryBrush") as IBrush,
                Margin = new Avalonia.Thickness(10, 8)
            }, TextBlock.TextProperty, "No output devices available"));
            return;
        }

        foreach (var device in devices.OrderByDescending(device => device.IsDefault).ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            host.Children.Add(CreateOutputDeviceButton(device));
        }
    }

    private Button CreateOutputDeviceButton(AudioDeviceInfo device)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,20") };
        var outputIcon = new DaisyIcon
        {
            Icon = device.IconGlyph == "continuity" ? "continuity" : device.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase)
                || string.Equals(device.Name, (Application.Current as App)?.AirPods?.Status.Name, StringComparison.OrdinalIgnoreCase)
                ? "AirPods" : MapAudioIcon(device.IconGlyph),
            Size = 18,
            VerticalAlignment = VerticalAlignment.Center
        };
        outputIcon.Bind(DaisyIcon.ForegroundProperty, this.GetResourceObservable("ContentSecondaryBrush"));
        content.Children.Add(outputIcon);
        var name = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = FormatOutputDeviceName(device.Name),
            FontSize = 13,
            Margin = new Avalonia.Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis
        }, "BodySmall");
        name.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentPrimaryBrush"));
        Grid.SetColumn(name, 1);
        content.Children.Add(name);

        var button = new Button
        {
            Content = content
        };
        button.Classes.Add("ShellButton");
        button.Classes.Add("AudioOutputItem");
        ToolTip.SetTip(button, device.Name);
        if (device.IsDefault)
        {
            button.Classes.Add("SelectedOutputDevice");
            var selected = new DaisyIcon
            {
                Icon = "check", Size = 16,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
            };
            selected.Bind(DaisyIcon.ForegroundProperty, this.GetResourceObservable("ActionPrimaryBrush"));
            Grid.SetColumn(selected, 2);
            content.Children.Add(selected);
        }
        button.Click += async (_, _) =>
        {
            if (!await _audioService.SetDefaultAudioDeviceAsync(device.Id))
            {
                ToolTip.SetTip(button, Loc.Get("Couldn’t switch to this output."));
                ReportFailure("Couldn’t switch to that audio output.");
            }
            await RefreshOutputDevicesAsync();
        };
        return button;
    }

    private void SetQuickSettingsPage(QuickSettingsPage page)
    {
        _currentPage = page;
        if (page is QuickSettingsPage.WifiNetworks or QuickSettingsPage.BluetoothDevices)
            _connectionListTimer.Start();
        else
            _connectionListTimer.Stop();
        if (page == QuickSettingsPage.BluetoothDevices) EnsureQuickBluetoothDiscovery();
        else StopQuickBluetoothDiscovery();
        var mainPage = this.FindControl<Control>("QuickSettingsMainPage");
        var outputPage = this.FindControl<Grid>("OutputDevicesPage");
        var wifiPage = this.FindControl<Grid>("WifiNetworksPage");
        var bluetoothPage = this.FindControl<Grid>("BluetoothDevicesPage");
        if (this.FindControl<Grid>("MotionContent") is { } motionContent)
        {
            motionContent.Height = page == QuickSettingsPage.Main
                ? double.NaN
                : (double)this.FindResource("QuickSettingsPanelContentHeight")!;
        }

        if (mainPage is not null) mainPage.IsVisible = page == QuickSettingsPage.Main;
        if (outputPage is not null) outputPage.IsVisible = page == QuickSettingsPage.OutputDevices;
        if (wifiPage is not null) wifiPage.IsVisible = page == QuickSettingsPage.WifiNetworks;
        if (bluetoothPage is not null) bluetoothPage.IsVisible = page == QuickSettingsPage.BluetoothDevices;
        if (page == QuickSettingsPage.WifiNetworks && wifiPage is not null && _lastWifiStatus is not null &&
            this.FindControl<StackPanel>("WifiNetworksHost") is { } wifiHost)
            RenderWifiNetworks(wifiHost, _lastWifiStatus);
        if (page == QuickSettingsPage.BluetoothDevices && bluetoothPage is not null && _lastBluetoothStatus is not null &&
            this.FindControl<StackPanel>("BluetoothDevicesHost") is { } bluetoothHost)
            RenderBluetoothDevices(bluetoothHost, _lastBluetoothStatus);
        var active = page switch
        {
            QuickSettingsPage.Main => mainPage,
            QuickSettingsPage.OutputDevices => outputPage,
            QuickSettingsPage.WifiNetworks => wifiPage,
            _ => bluetoothPage
        };
        if (active is not null) _ = GuiMotion.RevealAsync(active, 4);
    }

    private void RefreshVisibleConnectionList()
    {
        if (_currentPage == QuickSettingsPage.WifiNetworks)
        {
            if (_automaticWifiRefresh is not { IsCompleted: false })
                _automaticWifiRefresh = LoadWifiNetworksAsync();
        }
        else if (_currentPage == QuickSettingsPage.BluetoothDevices)
        {
            if (_automaticBluetoothRefresh is not { IsCompleted: false })
                _automaticBluetoothRefresh = LoadBluetoothDevicesAsync();
        }
    }

    private static string FormatOutputDeviceName(string name)
    {
        var parts = name.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 &&
               parts[0].Equals("FL", StringComparison.OrdinalIgnoreCase) &&
               parts[1].Equals("Output", StringComparison.OrdinalIgnoreCase)
            ? string.Join(" - ", parts.Skip(2))
            : name;
    }

    private async Task RefreshOutputDevicesAsync()
    {
        await LoadOutputDevicesAsync();
        RefreshStatus();
    }

    private async Task LoadWifiNetworksAsync()
    {
        var host = this.FindControl<StackPanel>("WifiNetworksHost");
        if (host is null) return;

        if (_lastWifiStatus is null && host.Children.Count == 0)
            host.Children.Add(CreateStatusText("Loading networks…"));

        WirelessNetworkStatus status;
        try
        {
            status = IsDemoWifiEnabled()
                ? CreateDemoWifiStatus()
                : await _wirelessNetworkService.GetNetworksAsync();
        }
        catch
        {
            status = new WirelessNetworkStatus(false, Array.Empty<WirelessNetworkInfo>(), "Wi-Fi networks could not be loaded.");
        }

        status = StabilizeWifiStatus(status);
        var changed = !HasSameWifiRows(_lastWifiStatus, status);
        _lastWifiStatus = status;
        if (changed && _currentPage == QuickSettingsPage.WifiNetworks)
            RenderWifiNetworks(host, status);
    }

    private WirelessNetworkStatus StabilizeWifiStatus(WirelessNetworkStatus status)
    {
        if (status.IsAvailable)
        {
            foreach (var network in status.Networks)
                _wifiNetworkCache[network.Ssid] = network;
        }

        if (_wifiNetworkCache.Count == 0) return status;
        return status with
        {
            IsAvailable = true,
            Networks = _wifiNetworkCache.Values
                .OrderByDescending(network => network.IsActive)
                .ThenByDescending(network => network.IsKnown)
                .ThenByDescending(network => network.IsAvailable)
                .ThenByDescending(network => network.SignalPercent)
                .ThenBy(network => network.Ssid, StringComparer.CurrentCultureIgnoreCase)
                .ToArray()
        };
    }

    private void RenderWifiNetworks(StackPanel host, WirelessNetworkStatus status)
    {
        host.Children.Clear();
        if (!status.IsAvailable || status.Networks.Count == 0)
        {
            host.Children.Add(CreateStatusText(status.Detail));
            return;
        }

        var known = status.Networks.Where(network => network.IsKnown).ToArray();
        var other = status.Networks.Where(network => !network.IsKnown && network.IsAvailable).ToArray();

        host.Children.Add(CreateConnectionSectionHeader("Known networks", "Saved networks"));
        if (known.Length == 0) host.Children.Add(CreateStatusText("No known networks yet."));
        foreach (var network in known)
        {
            host.Children.Add(CreateWifiNetworkButton(network));
        }
        host.Children.Add(CreateConnectionSectionHeader("Other networks", "Searching automatically"));
        if (other.Length == 0) host.Children.Add(CreateStatusText("Searching for nearby networks…"));
        foreach (var network in other) host.Children.Add(CreateWifiNetworkButton(network));
    }

    private static bool HasSameWifiRows(WirelessNetworkStatus? previous, WirelessNetworkStatus current)
    {
        if (previous is null || previous.IsAvailable != current.IsAvailable || previous.Networks.Count != current.Networks.Count)
            return false;
        return previous.Networks.Zip(current.Networks).All(pair =>
            pair.First.Ssid == pair.Second.Ssid &&
            pair.First.Security == pair.Second.Security &&
            pair.First.IsActive == pair.Second.IsActive &&
            pair.First.IsKnown == pair.Second.IsKnown &&
            pair.First.IsAvailable == pair.Second.IsAvailable);
    }

    private async Task ScanAndRefreshWifiAsync()
    {
        try
        {
            await _wirelessNetworkService.RequestScanAsync();
            await LoadWifiNetworksAsync();
        }
        catch
        {
            // The current cached list remains useful if an active scan is not
            // supported or NetworkManager is temporarily busy.
        }
    }

    private Button CreateWifiNetworkButton(WirelessNetworkInfo network)
    {
        var idleDetail = network.IsActive ? "Connected"
            : !network.IsAvailable ? "Not in range"
            : network.IsKnown ? $"Known network · {network.SignalPercent}%"
            : $"{network.Security} · {network.SignalPercent}%";
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,20") };
        var signalIcon = new WifiSignalIcon
        {
            SignalPercent = network.SignalPercent,
            Width = 18,
            Height = 18,
            ActiveBrush = this.FindResource("ContentSecondaryBrush") as IBrush ?? Brushes.White,
            InactiveBrush = this.FindResource("ContentTertiaryBrush") as IBrush ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(signalIcon);

        var textPanel = new StackPanel
        {
            Spacing = 2,
            Margin = new Avalonia.Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var name = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = network.Ssid,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        }, "BodySmall");
        name.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentPrimaryBrush"));
        textPanel.Children.Add(name);

        var detail = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = idleDetail,
            FontSize = 11,
            Foreground = this.FindResource("ContentSecondaryBrush") as IBrush ?? Brushes.Gray,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        }, "Caption");
        textPanel.Children.Add(detail);

        if (network.IsActive)
        {
            Loc.Bind(detail, TextBlock.TextProperty, "Connected");
        }

        Grid.SetColumn(textPanel, 1);
        content.Children.Add(textPanel);

        var button = new Button
        {
            Content = content
        };
        button.Classes.Add("ShellButton");
        button.Classes.Add("AudioOutputItem");
        button.Classes.Add("OutputDeviceItem");
        ToolTip.SetTip(button, network.Ssid);
        button.IsEnabled = network.IsAvailable || network.IsActive;

        if (network.IsActive)
        {
            button.Classes.Add("SelectedOutputDevice");
            button.Classes.Add("ConnectedWifiNetwork");
            var selected = new DaisyIcon
            {
                Icon = "check",
                Size = 16,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            selected.Bind(DaisyIcon.ForegroundProperty, this.GetResourceObservable("ActionPrimaryBrush"));
            Grid.SetColumn(selected, 2);
            content.Children.Add(selected);
        }
        else if (!network.Security.Equals("Open", StringComparison.OrdinalIgnoreCase))
        {
            var lockIcon = new DaisyIcon
            {
                Icon = "lock",
                Size = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            lockIcon.Bind(DaisyIcon.ForegroundProperty, this.GetResourceObservable("ContentTertiaryBrush"));
            Grid.SetColumn(lockIcon, 2);
            content.Children.Add(lockIcon);
        }
        if (network.IsActive)
        {
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                button.Classes.Add("Loading");
                detail.Text = Loc.Get("Disconnecting…");
                if (IsDemoWifiEnabled())
                {
                    _demoConnectedWifi = null;
                }
                else
                {
                    if (!await _wirelessNetworkService.DisconnectFromNetworkAsync())
                    {
                        button.IsEnabled = true;
                        button.Classes.Remove("Loading");
                        detail.Text = idleDetail;
                        ReportFailure("Couldn’t disconnect from that Wi-Fi network.");
                        return;
                    }
                }
                await LoadWifiNetworksAsync();
            };
        }
        else if (network.Security.Equals("Open", StringComparison.OrdinalIgnoreCase))
        {
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                button.Classes.Add("Loading");
                detail.Text = Loc.Get("Connecting…");
                if (IsDemoWifiEnabled())
                {
                    _demoConnectedWifi = network.Ssid;
                }
                else
                {
                    if (!await _wirelessNetworkService.ConnectToNetworkAsync(network.Ssid))
                    {
                        button.IsEnabled = true;
                        button.Classes.Remove("Loading");
                        detail.Text = idleDetail;
                        ReportFailure("Couldn’t connect to that Wi-Fi network.");
                        return;
                    }
                }
                await LoadWifiNetworksAsync();
            };
        }
        else
        {
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                button.Classes.Add("Loading");
                detail.Text = Loc.Get("Connecting…");
                if (IsDemoWifiEnabled())
                {
                    _demoConnectedWifi = network.Ssid;
                    await LoadWifiNetworksAsync();
                    return;
                }

                // NetworkManager remembers approved profiles. Try that path first so
                // reconnecting to a known secured network stays a single action.
                if (await _wirelessNetworkService.ConnectToNetworkAsync(network.Ssid))
                {
                    await LoadWifiNetworksAsync();
                    return;
                }

                button.IsEnabled = true;
                button.Classes.Remove("Loading");
                var dialog = new WifiPasswordDialog(
                    _wirelessNetworkService,
                    network.Ssid,
                    network.Security,
                    signalPercent: network.SignalPercent);

                var parentWindow = VisualRoot as Window ?? TopLevel.GetTopLevel(this) as Window;
                if (parentWindow is not null)
                {
                    await dialog.ShowDialog(parentWindow);
                }
                else
                {
                    dialog.Show();
                }

                if (dialog.WasConnected)
                {
                    await LoadWifiNetworksAsync();
                }
                else
                {
                    button.IsEnabled = true;
                    button.Classes.Remove("Loading");
                    detail.Text = idleDetail;
                }
            };
        }

        return button;
    }

    private static bool IsDemoWifiEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("DAISYOS_MOCK_WIFI"), "1", StringComparison.Ordinal);

    private WirelessNetworkStatus CreateDemoWifiStatus()
    {
        var networks = new[]
        {
            new WirelessNetworkInfo("DaisyOS Guest", 92, "WPA2", _demoConnectedWifi == "DaisyOS Guest", IsKnown: true),
            new WirelessNetworkInfo("Hemppu 5G", 68, "WPA2", _demoConnectedWifi == "Hemppu 5G", IsKnown: true),
            new WirelessNetworkInfo("Coffee Shop Wi-Fi", 43, "Open", _demoConnectedWifi == "Coffee Shop Wi-Fi"),
            new WirelessNetworkInfo("Neighbourhood Wi-Fi", 16, "WPA2", _demoConnectedWifi == "Neighbourhood Wi-Fi"),
        };

        return new WirelessNetworkStatus(true, networks, "Demo Wi-Fi networks are enabled.");
    }

    private async Task LoadBluetoothDevicesAsync()
    {
        var host = this.FindControl<StackPanel>("BluetoothDevicesHost");
        if (host is null) return;

        if (_lastBluetoothStatus is null && host.Children.Count == 0)
            host.Children.Add(CreateStatusText("Loading devices…"));

        BluetoothStatus status;
        try
        {
            status = IsDemoBluetoothEnabled()
                ? CreateDemoBluetoothStatus()
                : await _bluetoothService.GetStatusAsync();
        }
        catch
        {
            status = new BluetoothStatus(false, false, false, Array.Empty<BluetoothDevice>(), "Bluetooth devices could not be loaded.");
        }

        status = StabilizeBluetoothStatus(status);
        var changed = !HasSameBluetoothRows(_lastBluetoothStatus, status);
        _lastBluetoothStatus = status;
        if (changed && _currentPage == QuickSettingsPage.BluetoothDevices)
            RenderBluetoothDevices(host, status);
    }

    private BluetoothStatus StabilizeBluetoothStatus(BluetoothStatus status)
    {
        if (status.IsAvailable)
        {
            foreach (var device in status.Devices.Where(device => !LinuxBluetoothService.IsAnonymousName(device.Name)))
                _bluetoothDeviceCache[device.Address] = device;
        }

        if (_bluetoothDeviceCache.Count == 0) return status;
        return status with
        {
            IsAvailable = true,
            Devices = _bluetoothDeviceCache.Values.ToArray()
        };
    }

    private void RenderBluetoothDevices(StackPanel host, BluetoothStatus status)
    {
        host.Children.Clear();
        if (!status.IsAvailable)
        {
            host.Children.Add(CreateStatusText(status.Detail));
            return;
        }

        if (!status.IsEnabled)
            host.Children.Add(CreateStatusText("Bluetooth is off. Paired devices are still available below."));
        var devices = status.Devices.Where(d => !LinuxBluetoothService.IsAnonymousName(d.Name)).ToArray();
        var mine = devices.Where(device => device.IsPaired)
            .OrderByDescending(device => device.IsConnected)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var nearby = devices.Where(device => !device.IsPaired)
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

        host.Children.Add(CreateConnectionSectionHeader("My devices", "Paired devices"));
        if (mine.Length == 0) host.Children.Add(CreateStatusText("No paired devices yet."));
        foreach (var device in mine) host.Children.Add(CreateBluetoothDeviceButton(device));

        if (status.IsEnabled)
        {
            host.Children.Add(CreateConnectionSectionHeader("Nearby devices", "Searching automatically"));
            if (nearby.Length == 0) host.Children.Add(CreateStatusText("Searching for nearby devices in pairing mode…"));
            foreach (var device in nearby) host.Children.Add(CreateBluetoothDeviceButton(device));
        }
    }

    private static bool HasSameBluetoothRows(BluetoothStatus? previous, BluetoothStatus current)
    {
        if (previous is null || previous.IsAvailable != current.IsAvailable || previous.IsEnabled != current.IsEnabled ||
            previous.Devices.Count != current.Devices.Count)
            return false;
        return previous.Devices.Zip(current.Devices).All(pair => pair.First == pair.Second);
    }

    private Button CreateBluetoothDeviceButton(BluetoothDevice device)
    {
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("18,10,*"),
            RowDefinitions = new RowDefinitions("20,16"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var icon = new DaisyIcon
        {
            Icon = BluetoothDevicePresentation.IconFor(device),
            Size = 18,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(icon);
        Grid.SetRowSpan(icon, 2);

        var name = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = device.Name,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        }, "BodySmall");
        Grid.SetColumn(name, 2);
        content.Children.Add(name);

        var detail = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = device.IsConnected ? Loc.Get("Connected") : Loc.Get(device.IsPaired ? "Paired" : "Ready to pair"),
            FontSize = 11,
            Foreground = this.FindResource("ContentSecondaryBrush") as IBrush ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
        }, "Caption");
        Grid.SetRow(detail, 1);
        Grid.SetColumn(detail, 2);
        content.Children.Add(detail);

        var button = CreateQuickSettingsListButton(content, false);
        button.Classes.Set("ConnectedBluetoothDevice", device.IsConnected);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            button.Classes.Add("Loading");
            var pendingText = Loc.Get(device.IsConnected ? "Disconnecting…" : device.IsPaired ? "Connecting…" : "Pairing…");
            detail.Text = pendingText;
            ToolTip.SetTip(button, pendingText);
            var status = await _bluetoothService.GetAdapterStatusAsync();
            var ready = status.IsEnabled || await _bluetoothService.SetEnabledAsync(true);
            if (!ready || !(device.IsPaired
                    ? await _bluetoothService.SetConnectedAsync(device.Address, !device.IsConnected)
                    : await _bluetoothService.PairDeviceAsync(device.Address)))
            {
                ToolTip.SetTip(button, Loc.Get("Couldn’t change this device connection."));
                ReportFailure("Couldn’t change that Bluetooth connection.");
            }
            await LoadBluetoothDevicesAsync();
        };
        return button;
    }

    private Control CreateConnectionSectionHeader(string title, string detail)
    {
        var panel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(10, 10, 10, 2)
        };
        var heading = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = Loc.Get(title), FontWeight = FontWeight.SemiBold
        }, "BodySmall");
        heading.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentPrimaryBrush"));
        panel.Children.Add(heading);
        var status = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = Loc.Get(detail), FontSize = 11
        }, "Caption");
        status.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentTertiaryBrush"));
        Grid.SetColumn(status, 1);
        panel.Children.Add(status);
        return panel;
    }

    private void EnsureQuickBluetoothDiscovery()
    {
        if (_quickBluetoothDiscoveryLifetime is not null || _bluetoothDiscoveryPending) return;
        var lifetime = new CancellationTokenSource();
        _quickBluetoothDiscoveryLifetime = lifetime;
        _ = RunQuickBluetoothDiscoveryAsync(lifetime);
    }

    private async Task RunQuickBluetoothDiscoveryAsync(CancellationTokenSource lifetime)
    {
        var cancellationToken = lifetime.Token;
        _bluetoothDiscoveryPending = true;
        var started = false;
        try
        {
            var adapter = await _bluetoothService.GetAdapterStatusAsync(cancellationToken);
            if (!adapter.IsEnabled) return;
            var host = this.FindControl<StackPanel>("BluetoothDevicesHost");
            host?.Children.Insert(0, CreateStatusText("Searching for nearby devices…"));
            if (!(started = await _bluetoothService.StartDiscoveryAsync(cancellationToken)))
            {
                await LoadBluetoothDevicesAsync();
                ReportFailure("Couldn’t search for nearby Bluetooth devices.");
                return;
            }
            await LoadBluetoothDevicesAsync();
            while (!cancellationToken.IsCancellationRequested && _currentPage == QuickSettingsPage.BluetoothDevices)
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            if (started) await _bluetoothService.StopDiscoveryAsync();
            _bluetoothDiscoveryPending = false;
            if (ReferenceEquals(_quickBluetoothDiscoveryLifetime, lifetime))
                _quickBluetoothDiscoveryLifetime = null;
            lifetime.Dispose();
        }
    }

    private void StopQuickBluetoothDiscovery()
    {
        _quickBluetoothDiscoveryLifetime?.Cancel();
        _quickBluetoothDiscoveryLifetime = null;
    }

    private static bool IsDemoBluetoothEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("DAISYOS_MOCK_BLUETOOTH"), "1", StringComparison.Ordinal);

    private static BluetoothStatus CreateDemoBluetoothStatus() =>
        new(
            IsAvailable: true,
            IsEnabled: true,
            IsDiscovering: false,
            Devices:
            [
                new BluetoothDevice("10:24:98:7A:3B:01", "DaisyBuds Pro", true, true, "Audio"),
                new BluetoothDevice("10:24:98:7A:3B:02", "MX Master 3S", false, true, "Mouse"),
                new BluetoothDevice("10:24:98:7A:3B:03", "Keychron K3", false, true, "Keyboard"),
                new BluetoothDevice("10:24:98:7A:3B:04", "DualSense Wireless Controller", false, true, "Controller"),
                new BluetoothDevice("10:24:98:7A:3B:05", "Nearby headphones", false, false, "Audio", IsNearby: true),
            ],
            Detail: "Demo Bluetooth devices are enabled.");

    private static string MapAudioIcon(string icon) => icon switch
    {
        "volume_up" or "volume_down" or "volume_mute" => "speaker-high",
        "headphones" => "headphones",
        "continuity" => "continuity",
        "mic" => "microphone",
        _ => "speaker-high"
    };

    private static bool Contains(string value, string fragment) =>
        value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static Button CreateQuickSettingsListButton(Control content, bool isSelected)
    {
        var button = new Button { Content = content };
        button.Classes.Add("ShellButton");
        button.Classes.Add("OutputDeviceItem");
        if (isSelected) button.Classes.Add("SelectedOutputDevice");
        return button;
    }

    private TextBlock CreateStatusText(string text) => new()
    {
        Text = text,
        Foreground = this.FindResource("ContentSecondaryBrush") as IBrush,
        Margin = new Avalonia.Thickness(10, 8),
        TextWrapping = TextWrapping.Wrap,
    };

    private static void ReportFailure(string message) => (Application.Current as App)?.Feedback.Show(message);

    private void UpdateNetworkPresentation(NetworkStatus network, WirelessRadioStatus wifi, BluetoothStatus bluetooth)
    {
        var hasInternet = network.IsConnected && network.Connectivity == NetworkConnectivity.Full;
        var wifiIsOnline = hasInternet && network.ConnectionKind == NetworkConnectionKind.WiFi;
        var ethernetIsOnline = hasInternet && network.ConnectionKind == NetworkConnectionKind.Ethernet;
        var ethernetIsActive = network.IsConnected && network.ConnectionKind == NetworkConnectionKind.Ethernet;
        var wifiReceiverMissing = !wifi.IsAvailable;
        var bluetoothReceiverMissing = !bluetooth.IsAvailable;
        _bluetoothAvailable = bluetooth.IsAvailable;

        if (this.FindControl<QuickSettingTile>("WifiTile") is { } wifiTile)
        {
            wifiTile.IsVisible = !wifiReceiverMissing && !ethernetIsActive;
            wifiTile.IsEnabled = wifi.IsAvailable;
            wifiTile.IsChecked = wifi.IsEnabled;
            wifiTile.WifiSignalPercent = network.ConnectionKind == NetworkConnectionKind.WiFi
                ? network.SignalPercent ?? 0
                : 0;
            wifiTile.ToolTipText = Loc.Get(wifi.IsEnabled ? "Turn off Wi-Fi" : "Turn on Wi-Fi");
        }
        if (this.FindControl<QuickSettingTile>("EthernetTile") is { } ethernetTile)
        {
            ethernetTile.IsVisible = ethernetIsActive;
            ethernetTile.IsEnabled = ethernetIsActive;
            ethernetTile.IsChecked = ethernetIsActive;
            ethernetTile.ToolTipText = ethernetIsActive ? network.DisplayName : Loc.Get("Ethernet is not connected.");
        }
        if (this.FindControl<QuickSettingTile>("BluetoothTile") is { } bluetoothTile)
        {
            bluetoothTile.IsVisible = true;
            bluetoothTile.IsEnabled = true;
            bluetoothTile.IsChecked = bluetooth.IsEnabled;
            bluetoothTile.ToolTipText = Loc.Get(bluetooth.IsEnabled ? "Turn off Bluetooth" : "Turn on Bluetooth");
        }
        if (this.FindControl<QuickSettingTile>("DndTile") is { } dndTile)
        {
            dndTile.IsVisible = true;
            var isDnd = (Application.Current as App)?.SessionState.DoNotDisturb == true;
            dndTile.IsChecked = isDnd;
            dndTile.ToolTipText = Loc.Get(isDnd ? "Turn off Do Not Disturb" : "Turn on Do Not Disturb");
        }
        UpdateQuickSettingsTileLayout();
        UpdateAirPodsPresentation();
        NetworkStatusChanged?.Invoke(network, wifi, bluetooth);

    }

    private void UpdateAirPodsPresentation() => ApplyAirPodsStatus((Application.Current as App)?.AirPods?.Status ?? AirPodsStatus.Unavailable);

    private void ApplyAirPodsStatus(AirPodsStatus status)
    {
        if (this.FindControl<StackPanel>("AirPodsControls") is { } controls)
        {
            controls.IsVisible = status.IsConnected;
            ToolTip.SetTip(controls, string.IsNullOrEmpty(status.Battery) ? status.Name : $"{status.Name} · {status.Battery}");
        }
        if (this.FindControl<QuickSettingTile>("BluetoothTile") is { } bluetooth)
            bluetooth.IsVisible = true;
        foreach (var mode in Enum.GetValues<AirPodsNoiseMode>())
        {
            if (this.FindControl<ToggleButton>("AirPods" + mode) is not { } button) continue;
            button.IsChecked = status.NoiseMode == mode;
            button.IsEnabled = !_airPodsModePending && status.IsConnected && status.SupportedModes.Contains(mode);
        }
        if (this.FindControl<TextBlock>("AirPodsDetail") is { } detail)
        {
            detail.Text = !status.IsAvailable ? Loc.Get("Listening controls are unavailable.")
                : status.SupportedModes.Count == 0 ? Loc.Get("Listening controls are not available for this AirPods model.") : "";
            detail.IsVisible = status.IsConnected && detail.Text.Length > 0;
        }
        if (this.FindControl<QuickSettingsSlider>("VolumeSlider") is { } volume)
        {
            var airPodsOutput = status.IsConnected && (string.Equals(_defaultAudioName, status.Name, StringComparison.OrdinalIgnoreCase)
                || _defaultAudioName.Contains("AirPods", StringComparison.OrdinalIgnoreCase));
            volume.Icon = airPodsOutput ? "AirPods" : "speaker-high";
            ToolTip.SetTip(volume, string.IsNullOrEmpty(_defaultAudioName) ? Loc.Get("Volume") : _defaultAudioName);
        }
        UpdateQuickSettingsTileLayout();
    }

    private async void OnAirPodsModeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || !Enum.TryParse<AirPodsNoiseMode>(button.Tag?.ToString(), out var mode)
            || (Application.Current as App)?.AirPods is not { } pods || _airPodsModePending) return;
        _airPodsModePending = true;
        UpdateAirPodsPresentation();
        try
        {
            if (!await pods.SetModeAsync(mode))
                ReportFailure("Your AirPods didn’t confirm that listening mode. Check that they’re in your ears and try again.");
        }
        finally { _airPodsModePending = false; UpdateAirPodsPresentation(); }
    }

    private enum QuickSettingsPage
    {
        Main,
        OutputDevices,
        WifiNetworks,
        BluetoothDevices,
    }

}
