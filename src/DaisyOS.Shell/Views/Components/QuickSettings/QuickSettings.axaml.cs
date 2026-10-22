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
    private readonly ShellSessionState _sessionState = (Application.Current as App)?.SessionState ?? new ShellSessionState();
    private CancellationTokenSource? _volumeUpdateCancellation;
    private bool _isUpdatingVolumeFromService;
    private string? _demoConnectedWifi = "DaisyOS Guest";
    private Task? _statusRefreshTask;
    private bool _airPodsModePending;
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
        Loc.RefreshOnChange(this, () => { ApplyLocalSettings(); RefreshStatus(); });
        Loaded += (_, _) =>
        {
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
            _volumeUpdateCancellation?.Cancel();
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
            _isUpdatingVolumeFromService = true;
            try
            {
                volumeSlider.IsEnabled = true;
                volumeSlider.Value = _sessionState.LastKnownVolume ?? 50;
            }
            finally
            {
                _isUpdatingVolumeFromService = false;
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
            if (this.FindControl<QuickSettingsSlider>("VolumeSlider") is { } volumeSlider)
            {
                _isUpdatingVolumeFromService = true;
                try
                {
                    volumeSlider.IsEnabled = volume is not null;
                    volumeSlider.Value = volume ?? _sessionState.LastKnownVolume ?? 50;
                }
                finally
                {
                    _isUpdatingVolumeFromService = false;
                }
            }
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
        catch
        {
            NetworkStatusUnavailable?.Invoke();
            if (this.FindControl<QuickSettingsSlider>("VolumeSlider") is { } volumeSlider) volumeSlider.IsEnabled = false;
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
            Icon = device.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase)
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
        var mainPage = this.FindControl<Control>("QuickSettingsMainPage");
        var outputPage = this.FindControl<StackPanel>("OutputDevicesPage");
        var wifiPage = this.FindControl<StackPanel>("WifiNetworksPage");
        var bluetoothPage = this.FindControl<StackPanel>("BluetoothDevicesPage");

        if (mainPage is not null) mainPage.IsVisible = page == QuickSettingsPage.Main;
        if (outputPage is not null) outputPage.IsVisible = page == QuickSettingsPage.OutputDevices;
        if (wifiPage is not null) wifiPage.IsVisible = page == QuickSettingsPage.WifiNetworks;
        if (bluetoothPage is not null) bluetoothPage.IsVisible = page == QuickSettingsPage.BluetoothDevices;
        var active = page switch
        {
            QuickSettingsPage.Main => mainPage,
            QuickSettingsPage.OutputDevices => outputPage,
            QuickSettingsPage.WifiNetworks => wifiPage,
            _ => bluetoothPage
        };
        if (active is not null) _ = GuiMotion.RevealAsync(active, 4);
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

        host.Children.Clear();
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

        host.Children.Clear();
        if (!status.IsAvailable || status.Networks.Count == 0)
        {
            host.Children.Add(CreateStatusText(status.Detail));
            return;
        }

        foreach (var network in status.Networks)
        {
            host.Children.Add(CreateWifiNetworkButton(network));
        }
    }

    private Button CreateWifiNetworkButton(WirelessNetworkInfo network)
    {
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("18,10,*"),
            RowDefinitions = new RowDefinitions(network.IsActive ? "20,16" : "36"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var signalIcon = new WifiSignalIcon
        {
            SignalPercent = network.SignalPercent,
            Width = 18,
            Height = 18,
            ActiveBrush = this.FindResource("ContentSecondaryBrush") as IBrush ?? Brushes.White,
            InactiveBrush = this.FindResource("ContentTertiaryBrush") as IBrush ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(signalIcon);
        Grid.SetRowSpan(signalIcon, network.IsActive ? 2 : 1);

        var name = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = network.Ssid,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        }, "BodySmall");
        Grid.SetColumn(name, 2);
        content.Children.Add(name);

        if (network.IsActive)
        {
            var detail = Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
            {
                Text = "Connected",
                FontSize = 11,
                Foreground = this.FindResource("ContentSecondaryBrush") as IBrush ?? Brushes.Gray,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            }, "Caption"), TextBlock.TextProperty, "Connected");
            Grid.SetRow(detail, 1);
            Grid.SetColumn(detail, 2);
            content.Children.Add(detail);
        }

        var button = CreateQuickSettingsListButton(content, false);
        button.Classes.Set("ConnectedWifiNetwork", network.IsActive);
        if (network.IsActive)
        {
            button.Click += async (_, _) =>
            {
                if (IsDemoWifiEnabled())
                {
                    _demoConnectedWifi = null;
                }
                else
                {
                    await _wirelessNetworkService.DisconnectFromNetworkAsync();
                }
                await LoadWifiNetworksAsync();
            };
        }
        else if (network.Security.Equals("Open", StringComparison.OrdinalIgnoreCase))
        {
            button.Click += async (_, _) =>
            {
                if (IsDemoWifiEnabled())
                {
                    _demoConnectedWifi = network.Ssid;
                }
                else
                {
                    await _wirelessNetworkService.ConnectToNetworkAsync(network.Ssid);
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
                    null);

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
            new WirelessNetworkInfo("DaisyOS Guest", 92, "WPA2", _demoConnectedWifi == "DaisyOS Guest"),
            new WirelessNetworkInfo("Hemppu 5G", 68, "WPA2", _demoConnectedWifi == "Hemppu 5G"),
            new WirelessNetworkInfo("Coffee Shop Wi-Fi", 43, "Open", _demoConnectedWifi == "Coffee Shop Wi-Fi"),
            new WirelessNetworkInfo("Neighbourhood Wi-Fi", 16, "WPA2", _demoConnectedWifi == "Neighbourhood Wi-Fi"),
        };

        return new WirelessNetworkStatus(true, networks, "Demo Wi-Fi networks are enabled.");
    }

    private async Task LoadBluetoothDevicesAsync()
    {
        var host = this.FindControl<StackPanel>("BluetoothDevicesHost");
        if (host is null) return;

        host.Children.Clear();
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

        host.Children.Clear();
        if (!status.IsAvailable || !status.IsEnabled || status.Devices.Count == 0)
        {
            host.Children.Add(CreateStatusText(status.Detail));
            return;
        }

        foreach (var device in status.Devices.OrderByDescending(device => device.IsConnected).ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            host.Children.Add(CreateBluetoothDeviceButton(device));
        }
    }

    private Button CreateBluetoothDeviceButton(BluetoothDevice device)
    {
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("18,10,*"),
            RowDefinitions = new RowDefinitions(device.IsConnected ? "20,16" : "36"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var icon = new DaisyIcon
        {
            Icon = BluetoothIconFor(device),
            Size = 18,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(icon);
        Grid.SetRowSpan(icon, device.IsConnected ? 2 : 1);

        var name = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = device.Name,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        }, "BodySmall");
        Grid.SetColumn(name, 2);
        content.Children.Add(name);

        if (device.IsConnected)
        {
            var detail = Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
            {
                Text = "Connected",
                FontSize = 11,
                Foreground = this.FindResource("ContentSecondaryBrush") as IBrush ?? Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
            }, "Caption"), TextBlock.TextProperty, "Connected");
            Grid.SetRow(detail, 1);
            Grid.SetColumn(detail, 2);
            content.Children.Add(detail);
        }

        var button = CreateQuickSettingsListButton(content, false);
        button.Classes.Set("ConnectedBluetoothDevice", device.IsConnected);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            if (!(device.IsPaired
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
            ],
            Detail: "Demo Bluetooth devices are enabled.");

    private static string BluetoothIconFor(BluetoothDevice device)
    {
        var type = device.Type;
        var name = device.Name;
        if (Contains(type, "AirPods") || Contains(name, "AirPods")) return "AirPods";
        if (Contains(type, "audio") || Contains(type, "headphone") || Contains(type, "headset") || Contains(name, "bud")) return "headphones";
        if (Contains(type, "keyboard") || Contains(name, "keyboard") || Contains(name, "keychron")) return "keyboard";
        if (Contains(type, "mouse") || Contains(name, "mouse") || Contains(name, "master")) return "mouse";
        if (Contains(type, "phone") || Contains(name, "phone")) return "smartphone";
        if (Contains(type, "controller") || Contains(type, "gamepad") || Contains(name, "controller") || Contains(name, "dualsense")) return "game-controller";
        return "bluetooth";
    }

    private static string MapAudioIcon(string icon) => icon switch
    {
        "volume_up" or "volume_down" or "volume_mute" => "speaker-high",
        "headphones" => "headphones",
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
            detail.Text = !status.IsAvailable ? "AirPods connected. Listening controls are unavailable; check the LibrePods service."
                : status.SupportedModes.Count == 0 ? "Listening controls are not available for this AirPods model." : "";
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
