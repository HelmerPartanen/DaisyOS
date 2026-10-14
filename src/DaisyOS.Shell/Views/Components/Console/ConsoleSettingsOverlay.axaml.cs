using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Audio;
using DaisyOS.System.Controllers;
using DaisyOS.System.Networking;
using DaisyOS.System.Power;
using DaisyOS.System.Processes;
using DaisyOS.Shell.Controls;

namespace DaisyOS.Shell.Views.Components.Console;

public partial class ConsoleSettingsOverlay : UserControl
{
    private readonly IAudioService _audioService;
    private readonly INetworkStatusService _networkStatusService;
    private readonly IWirelessNetworkService _wirelessNetworkService;
    private readonly IPowerService? _powerService;
    private readonly IUserSessionService? _userSessionService;
    private readonly IBluetoothService? _bluetoothService;
    private readonly IDisplayStatusService? _displayStatusService;
    private readonly IClockService? _clockService;
    private IControllerInputService _controllerInputService;

    private double _micVolume = 75;
    private CancellationTokenSource? _loadCts;
    private ControllerKeybindingsConfig _keybindingsConfig = new();
    private string? _rebindingAction;
    private string? _activeControllerName;
    private DispatcherTimer? _clockTimer;

    public event EventHandler? Closed;
    public event EventHandler? ReturnToDesktopRequested;

    public ConsoleSettingsOverlay()
        : this(
            new LinuxAudioService(new SafeCommandRunner()),
            new LinuxNetworkStatusService(new SafeCommandRunner()),
            new LinuxWirelessNetworkService(new SafeCommandRunner()),
            new LinuxControllerInputService(),
            new LinuxPowerService(new SafeCommandRunner()),
            new DaisyOS.System.Session.LinuxUserSessionService(),
            new DaisyOS.System.Bluetooth.LinuxBluetoothService(new SafeCommandRunner()),
            new DaisyOS.System.Displays.LinuxDisplayStatusService(new SafeCommandRunner()),
            null) // ClockService may be injected differently, use null for now
    {
    }

    public ConsoleSettingsOverlay(
        IAudioService audioService,
        INetworkStatusService networkStatusService,
        IWirelessNetworkService wirelessNetworkService,
        IControllerInputService controllerInputService,
        IPowerService? powerService = null,
        IUserSessionService? userSessionService = null,
        IBluetoothService? bluetoothService = null,
        IDisplayStatusService? displayStatusService = null,
        IClockService? clockService = null)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _networkStatusService = networkStatusService ?? throw new ArgumentNullException(nameof(networkStatusService));
        _wirelessNetworkService = wirelessNetworkService ?? throw new ArgumentNullException(nameof(wirelessNetworkService));
        _controllerInputService = controllerInputService ?? throw new ArgumentNullException(nameof(controllerInputService));
        _powerService = powerService;
        _userSessionService = userSessionService;
        _bluetoothService = bluetoothService;
        _displayStatusService = displayStatusService;
        _clockService = clockService;

        InitializeComponent();
        Loc.RefreshOnChange(this, () => SelectTab(_tabNames[_activeTabIndex]));
        _controllerInputService.RawInputReceived += OnRawControllerInputReceived;

        _ = new DaisyOS.Shell.Services.Diagnostics.VisualActivityGate(this, active =>
        {
            if (active) _clockTimer?.Start();
            else _clockTimer?.Stop();
        });
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void SetInputService(IControllerInputService controllerInputService)
    {
        if (_controllerInputService != null)
        {
            _controllerInputService.RawInputReceived -= OnRawControllerInputReceived;
        }

        _controllerInputService = controllerInputService ?? throw new ArgumentNullException(nameof(controllerInputService));
        _controllerInputService.RawInputReceived += OnRawControllerInputReceived;
    }

    private List<Control> GetCurrentPanelFocusableControls()
    {
        var list = new List<Control>();

        if (AudioPanel.Classes.Contains("ActiveTab"))
        {
            if (VolumeRowBtn != null) list.Add(VolumeRowBtn);
            if (MicRowBtn != null) list.Add(MicRowBtn);
            if (OutputDeviceRowBtn != null) list.Add(OutputDeviceRowBtn);
            if (InputDeviceRowBtn != null) list.Add(InputDeviceRowBtn);
        }
        else if (NetworkPanel.Classes.Contains("ActiveTab"))
        {
            if (WifiRowBtn != null) list.Add(WifiRowBtn);
        }
        else if (BluetoothPanel.Classes.Contains("ActiveTab"))
        {
            if (BluetoothRowBtn != null) list.Add(BluetoothRowBtn);
        }
        else if (DisplayPanel.Classes.Contains("ActiveTab"))
        {
            if (BrightnessRowBtn != null) list.Add(BrightnessRowBtn);
            if (ResolutionRowBtn != null) list.Add(ResolutionRowBtn);
        }
        else if (ControlsPanel.Classes.Contains("ActiveTab"))
        {
            if (RemapInfoRowBtn != null) list.Add(RemapInfoRowBtn);
            foreach (var child in KeybindingsListStack.Children)
            {
                if (child is Button b) list.Add(b);
            }
            if (ResetKeybindingsRowBtn != null) list.Add(ResetKeybindingsRowBtn);
        }
        else if (SystemPanel.Classes.Contains("ActiveTab"))
        {
            if (ReturnDesktopRowBtn != null) list.Add(ReturnDesktopRowBtn);
            if (RestartRowBtn != null) list.Add(RestartRowBtn);
            if (ShutdownRowBtn != null) list.Add(ShutdownRowBtn);
        }

        return list;
    }

    private int _itemIndex = 0;
    private bool _isSidebarFocused = false;

    public void ShowOverlay(string initialTab = "Audio")
    {
        IsVisible = true;
        Opacity = 1;
        IsHitTestVisible = true;
        
        SelectTab(initialTab);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        _ = LoadSettingsDataAsync(_loadCts.Token);

        Dispatcher.UIThread.Post(() =>
        {
            FocusFirstControlInActivePanel();
        }, DispatcherPriority.Input);
    }

    public async void HideOverlay()
    {
        Opacity = 0;
        IsHitTestVisible = false;
        
        _rebindingAction = null;
        if (_controllerInputService != null) _controllerInputService.IsRebinding = false;
        _loadCts?.Cancel();
        Closed?.Invoke(this, EventArgs.Empty);
        
        await Task.Delay(250);
        if (Opacity == 0) IsVisible = false;
    }

    private int _activeTabIndex = 0;
    private readonly string[] _tabNames = new[] { "Audio", "Network", "Bluetooth", "Display", "Controls", "System" };

    public void Navigate(ControllerNavigationAction action)
    {
        if (_controllerInputService != null && _controllerInputService.IsRebinding)
        {
            return;
        }

        var controls = GetCurrentPanelFocusableControls();

        switch (action)
        {
            case ControllerNavigationAction.PreviousSection: // LB / L1
                _activeTabIndex = (_activeTabIndex - 1 + _tabNames.Length) % _tabNames.Length;
                SelectTab(_tabNames[_activeTabIndex]);
                FocusFirstControlInActivePanel();
                break;

            case ControllerNavigationAction.NextSection: // RB / R1
                _activeTabIndex = (_activeTabIndex + 1) % _tabNames.Length;
                SelectTab(_tabNames[_activeTabIndex]);
                FocusFirstControlInActivePanel();
                break;

            case ControllerNavigationAction.Up:
                var focusedU = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                if (focusedU == BackButton) break;
                if (_isSidebarFocused)
                {
                    if (_activeTabIndex == 0)
                    {
                        _isSidebarFocused = false;
                        BackButton?.Focus();
                    }
                    else
                    {
                        _activeTabIndex = _activeTabIndex - 1;
                        SelectTab(_tabNames[_activeTabIndex]);
                        FocusSidebarTab(_activeTabIndex);
                    }
                }
                else if (controls.Count > 0)
                {
                    _itemIndex = (_itemIndex - 1 + controls.Count) % controls.Count;
                    controls[_itemIndex].Focus();
                }
                break;

            case ControllerNavigationAction.Down:
                var focusedD = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                if (focusedD == BackButton)
                {
                    _isSidebarFocused = true;
                    _activeTabIndex = 0;
                    SelectTab(_tabNames[_activeTabIndex]);
                    FocusSidebarTab(0);
                    break;
                }
                if (_isSidebarFocused)
                {
                    if (_activeTabIndex < _tabNames.Length - 1)
                    {
                        _activeTabIndex = _activeTabIndex + 1;
                        SelectTab(_tabNames[_activeTabIndex]);
                        FocusSidebarTab(_activeTabIndex);
                    }
                }
                else if (controls.Count > 0)
                {
                    _itemIndex = (_itemIndex + 1) % controls.Count;
                    controls[_itemIndex].Focus();
                }
                break;

            case ControllerNavigationAction.Left:
                if (!_isSidebarFocused)
                {
                    var focusedL = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                    if (focusedL is ConsolePillSlider pillL)
                    {
                        pillL.Value = Math.Max(pillL.Minimum, pillL.Value - pillL.Step);
                    }
                    else
                    {
                        _isSidebarFocused = true;
                        FocusSidebarTab(_activeTabIndex);
                    }
                }
                break;

            case ControllerNavigationAction.Right:
                if (!_isSidebarFocused)
                {
                    var focusedR = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                    if (focusedR is ConsolePillSlider pillR)
                    {
                        pillR.Value = Math.Min(pillR.Maximum, pillR.Value + pillR.Step);
                    }
                }
                else
                {
                    _isSidebarFocused = false;
                    FocusFirstControlInActivePanel();
                }
                break;

            case ControllerNavigationAction.Confirm:
                var focusedC = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                if (_isSidebarFocused)
                {
                    _isSidebarFocused = false;
                    FocusFirstControlInActivePanel();
                }
                else if (focusedC == VolumeRowBtn && MasterVolumePillSlider != null)
                {
                    MasterVolumePillSlider.Focus();
                }
                else if (focusedC == MicRowBtn && MicVolumePillSlider != null)
                {
                    MicVolumePillSlider.Focus();
                }
                else if (focusedC is Button btn)
                {
                    btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else if (focusedC is ToggleSwitch ts)
                {
                    ts.IsChecked = !ts.IsChecked;
                    ts.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                break;

            case ControllerNavigationAction.Back:
                var focusedB = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                if (focusedB is ConsolePillSlider)
                {
                    if (focusedB == MasterVolumePillSlider && VolumeRowBtn != null) VolumeRowBtn.Focus();
                    else if (focusedB == MicVolumePillSlider && MicRowBtn != null) MicRowBtn.Focus();
                }
                else
                {
                    HideOverlay();
                }
                break;

            case ControllerNavigationAction.QuickSettings:
                HideOverlay();
                break;
        }
    }

    private void FocusSidebarTab(int index)
    {
        _isSidebarFocused = true;
        switch (index)
        {
            case 0: TabAudioBtn.Focus(); break;
            case 1: TabNetworkBtn.Focus(); break;
            case 2: TabBluetoothBtn.Focus(); break;
            case 3: TabDisplayBtn.Focus(); break;
            case 4: TabControlsBtn.Focus(); break;
            case 5: TabSystemBtn.Focus(); break;
        }
    }

    private void FocusFirstControlInActivePanel()
    {
        _isSidebarFocused = false;
        _itemIndex = 0;
        var controls = GetCurrentPanelFocusableControls();
        if (controls.Count > 0)
        {
            controls[0].Focus();
        }
        else
        {
            FocusSidebarTab(_activeTabIndex);
        }
    }

    public void SetControllerLayout(string? name)
    {
        _activeControllerName = name;
        var type = ControllerConnectionStatus.DetectType(name);
        var isPlayStation = type == ControllerType.PlayStation;
        var prefix = isPlayStation
            ? "avares://DaisyOS.Shell/Assets/PlaystationController/"
            : "avares://DaisyOS.Shell/Assets/XboxController/";

        try
        {
            if (HintDpadImage != null)
                HintDpadImage.Source = new Bitmap(AssetLoader.Open(new Uri(prefix + (isPlayStation ? "plain-bottom.png" : "dpad.png"))));

            if (HintConfirmImage != null)
                HintConfirmImage.Source = new Bitmap(AssetLoader.Open(new Uri(prefix + (isPlayStation ? "outline-blue-cross.png" : "a-filled-green.png"))));

            if (HintDetailsImage != null)
                HintDetailsImage.Source = new Bitmap(AssetLoader.Open(new Uri(prefix + (isPlayStation ? "outline-green-triangle.png" : "y-filled-yellow.png"))));

            if (HintBackImage != null)
                HintBackImage.Source = new Bitmap(AssetLoader.Open(new Uri(prefix + (isPlayStation ? "outline-red-circle.png" : "b-filled red.png"))));
        }
        catch
        {
            // Graceful fallback
        }
    }

    public void SetControllerLayout(ControllerConnectionStatus status)
    {
        SetControllerLayout(status?.Name);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Settings data is loaded on ShowOverlay, not while hidden at login.
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) =>
        {
            if (ClockText != null)
            {
                var now = _clockService?.Now ?? DateTimeOffset.Now;
                ClockText.Text = now.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern, CultureInfo.CurrentCulture);
            }
        };
        if (IsEffectivelyVisible && Opacity > 0) _clockTimer.Start();
        if (ClockText != null)
        {
            var now = _clockService?.Now ?? DateTimeOffset.Now;
            ClockText.Text = now.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern, CultureInfo.CurrentCulture);
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _clockTimer?.Stop();
    }

    private async Task LoadSettingsDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 1. Audio Data
            var volume = await _audioService.GetVolumeAsync(cancellationToken).ConfigureAwait(true);
            if (volume.HasValue && MasterVolumePillSlider != null)
            {
                MasterVolumePillSlider.Value = (int)Math.Round(volume.Value);
            }

            var devices = await _audioService.GetAudioDevicesAsync(cancellationToken).ConfigureAwait(true);
            if (devices.Count > 0 && OutputDeviceText != null)
            {
                var defaultOutput = devices.FirstOrDefault(d => d.IsDefault) ?? devices[0];
                OutputDeviceText.Text = defaultOutput.Name;
            }

            // 2. Network Status
            var netStatus = await _networkStatusService.GetStatusAsync(cancellationToken).ConfigureAwait(true);
            if (netStatus.IsConnected)
            {
                NetworkStatusText.Text = Loc.Format("Connected to {0} ({1})", netStatus.DisplayName, netStatus.Detail);
            }
            else
            {
                NetworkStatusText.Text = Loc.Get("Not connected");
            }

            await RefreshWifiNetworksAsync(cancellationToken).ConfigureAwait(true);

            // 3. Bluetooth Status
            if (_bluetoothService != null)
            {
                var btStatus = await _bluetoothService.GetStatusAsync(cancellationToken).ConfigureAwait(true);
                Dispatcher.UIThread.Post(() =>
                {
                    if (BluetoothStatusText != null)
                    {
                        BluetoothStatusText.Text = btStatus.IsAvailable 
                            ? (btStatus.IsEnabled ? Loc.Get("Bluetooth is On") : Loc.Get("Bluetooth is Off"))
                            : Loc.Get("Bluetooth not available");
                    }
                    if (BluetoothToggleSwitch != null)
                    {
                        BluetoothToggleSwitch.IsChecked = btStatus.IsEnabled;
                    }
                });
                await RefreshBluetoothDevicesAsync(cancellationToken).ConfigureAwait(true);
            }

            // 4. Display Status
            if (_displayStatusService != null)
            {
                var displayStatus = await _displayStatusService.GetStatusAsync(cancellationToken).ConfigureAwait(true);
                Dispatcher.UIThread.Post(() =>
                {
                    if (ResolutionText != null)
                    {
                        ResolutionText.Text = string.IsNullOrEmpty(displayStatus.Resolution) ? Loc.Get("Unknown") : displayStatus.Resolution;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load console settings: {ex.Message}");
        }
    }

    private async Task RefreshWifiNetworksAsync(CancellationToken cancellationToken)
    {
        try
        {
            var wifiStatus = await _wirelessNetworkService.GetNetworksAsync(cancellationToken).ConfigureAwait(true);
            WifiToggleSwitch.IsChecked = wifiStatus.IsAvailable;

            WifiListStack.Children.Clear();
            if (wifiStatus.Networks.Count == 0)
            {
                WifiListStack.Children.Add(Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
                {
                    Text = "No Wi-Fi networks found",
                    FontSize = 13,
                    Foreground = GetThemeBrush("ContentSecondaryBrush", Brushes.Gray)
                }, "BodySmall"), TextBlock.TextProperty, "No Wi-Fi networks found"));
                return;
            }

            foreach (var net in wifiStatus.Networks.Take(5))
            {
                var border = new Border
                {
                    Padding = new Thickness(12, 10),
                    CornerRadius = new CornerRadius(10),
                    Background = GetThemeBrush("ActionSecondaryBrush", Brushes.DarkGray)
                };

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

                var infoStack = new StackPanel { Spacing = 2 };
                infoStack.Children.Add(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
                {
                    Text = net.Ssid,
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold
                }, "Body"));
                infoStack.Children.Add(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
                {
                    Text = net.IsActive ? Loc.Get("Connected") : Loc.Format("Signal: {0}%", net.SignalPercent),
                    FontSize = 12,
                    Foreground = GetThemeBrush("ContentSecondaryBrush", Brushes.Gray)
                }, "Caption"));

                var connectBtn = DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
                {
                    Content = Loc.Get(net.IsActive ? "Connected" : "Connect"),
                    IsEnabled = !net.IsActive,
                    Classes = { "ConsoleActionBtn" },
                    Height = 32,
                    Padding = new Thickness(12, 0)
                });

                grid.Children.Add(infoStack);
                grid.Children.Add(connectBtn);
                Grid.SetColumn(connectBtn, 1);

                border.Child = grid;
                WifiListStack.Children.Add(border);
            }
        }
        catch
        {
            // Best effort
        }
    }

    private void OnTabClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tabName)
        {
            SelectTab(tabName);
        }
    }

    private void SelectTab(string tabName)
    {
        _activeTabIndex = Array.IndexOf(_tabNames, tabName);
        if (_activeTabIndex < 0) _activeTabIndex = 0;

        AudioPanel.Classes.Set("ActiveTab", tabName == "Audio");
        NetworkPanel.Classes.Set("ActiveTab", tabName == "Network");
        BluetoothPanel.Classes.Set("ActiveTab", tabName == "Bluetooth");
        DisplayPanel.Classes.Set("ActiveTab", tabName == "Display");
        ControlsPanel.Classes.Set("ActiveTab", tabName == "Controls");
        SystemPanel.Classes.Set("ActiveTab", tabName == "System");

        TabAudioBtn.Classes.Set("Active", tabName == "Audio");
        TabNetworkBtn.Classes.Set("Active", tabName == "Network");
        TabBluetoothBtn.Classes.Set("Active", tabName == "Bluetooth");
        TabDisplayBtn.Classes.Set("Active", tabName == "Display");
        TabControlsBtn.Classes.Set("Active", tabName == "Controls");
        TabSystemBtn.Classes.Set("Active", tabName == "System");

        if (tabName == "Controls")
        {
            BuildKeybindingsListUI();
        }

        CategoryTitleText.Text = tabName switch
        {
            "Audio" => Loc.Get("Sound"),
            "Controls" => Loc.Get("Controller"),
            _ => Loc.Get(tabName)
        };
    }

    private void LoadKeybindingsConfig()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configFile = Path.Combine(home, ".config", "daisyos", "controller_keybindings.json");
            if (File.Exists(configFile))
            {
                var json = File.ReadAllText(configFile);
                var loaded = JsonSerializer.Deserialize(json, DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.ControllerKeybindingsConfig);
                if (loaded != null) _keybindingsConfig = loaded;
            }
        }
        catch
        {
            _keybindingsConfig = new ControllerKeybindingsConfig();
        }
    }

    private void SaveKeybindingsConfig()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configDir = Path.Combine(home, ".config", "daisyos");
            Directory.CreateDirectory(configDir);
            var configFile = Path.Combine(configDir, "controller_keybindings.json");
            var json = JsonSerializer.Serialize(_keybindingsConfig, DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.ControllerKeybindingsConfig);
            File.WriteAllText(configFile, json);

            _controllerInputService?.ReloadKeybindings();
        }
        catch
        {
            // Best effort
        }
    }

    public static string GetButtonLabel(byte buttonNumber, ControllerType type)
    {
        if (type == ControllerType.PlayStation)
        {
            return buttonNumber switch
            {
                0 => "Cross (✕)",
                1 => "Circle (○)",
                2 => "Triangle (△)",
                3 => "Square (▫)",
                4 => "L1",
                5 => "R1",
                6 => "L2",
                7 => "R2",
                8 => "Share",
                9 => "Options",
                10 => "PS Button",
                11 => "L3",
                12 => "R3",
                13 => "Touchpad",
                _ => $"Button {buttonNumber}"
            };
        }

        return buttonNumber switch
        {
            0 => "A",
            1 => "B",
            2 => "X",
            3 => "Y",
            4 => "LB",
            5 => "RB",
            6 => "View / Select",
            7 => "Menu / Start",
            8 => "Guide / Xbox",
            9 => "LS",
            10 => "RS",
            _ => $"Button {buttonNumber}"
        };
    }

    private static string ActionLabelFor(string action) => Loc.Get(action switch
    {
        "QuickSettings" => "Quick Settings",
        "PreviousSection" => "Previous section",
        "NextSection" => "Next section",
        "OpenConsole" => "Console mode",
        _ => action
    });

    private void BuildKeybindingsListUI()
    {
        LoadKeybindingsConfig();
        KeybindingsListStack.Children.Clear();

        var type = ControllerConnectionStatus.DetectType(_activeControllerName);

        var actions = new (string Action, string Label, string DefaultDesc)[]
        {
            ("Confirm", "Select / Launch Game", type == ControllerType.PlayStation ? "Cross (✕)" : "A"),
            ("Back", "Back / Cancel Overlay", type == ControllerType.PlayStation ? "Circle (○)" : "B"),
            ("QuickSettings", "Toggle Quick Settings", type == ControllerType.PlayStation ? "Square (▫)" : "X"),
            ("Details", "Show Details & Info", type == ControllerType.PlayStation ? "Triangle (△)" : "Y"),
            ("PreviousSection", "Switch Tab Left", type == ControllerType.PlayStation ? "L1" : "LB"),
            ("NextSection", "Switch Tab Right", type == ControllerType.PlayStation ? "R1" : "RB"),
            ("OpenConsole", "Toggle Console Mode", type == ControllerType.PlayStation ? "PS Button" : "Guide / Xbox")
        };

        foreach (var act in actions)
        {
            var border = new Border
            {
                Padding = new Thickness(12, 10),
                CornerRadius = new CornerRadius(10),
                Background = GetThemeBrush("ActionSecondaryBrush", Brushes.DarkGray)
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            var labelStack = new StackPanel { Spacing = 2 };
            labelStack.Children.Add(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock { Text = ActionLabelFor(act.Action), FontSize = 14, FontWeight = FontWeight.Bold }, "Body"));
            labelStack.Children.Add(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
            {
                Text = Loc.Get(act.Label),
                FontSize = 12,
                Foreground = GetThemeBrush("ContentSecondaryBrush", Brushes.Gray)
            }, "Caption"));

            var boundText = GetBoundButtonText(act.Action) ?? act.DefaultDesc;

            var rebindBtn = DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
            {
                Content = _rebindingAction == act.Action ? Loc.Get("PRESS BUTTON...") : Loc.Format("Remap ({0})", boundText),
                Classes = { "ConsoleActionBtn" },
                Height = 32,
                Padding = new Thickness(12, 0)
            });

            var actionName = act.Action;
            rebindBtn.Click += (s, e) =>
            {
                _rebindingAction = actionName;
                if (_controllerInputService != null) _controllerInputService.IsRebinding = true;
                rebindBtn.Content = Loc.Get("PRESS BUTTON...");
                LiveTestFeedText.Text = Loc.Format("Listening... Press any button on your controller to bind to '{0}'.", ActionLabelFor(actionName));
            };

            grid.Children.Add(labelStack);
            grid.Children.Add(rebindBtn);
            Grid.SetColumn(rebindBtn, 1);

            border.Child = grid;
            KeybindingsListStack.Children.Add(border);
        }
    }

    private string? GetBoundButtonText(string actionName)
    {
        var type = ControllerConnectionStatus.DetectType(_activeControllerName);
        var buttonMap = type == ControllerType.PlayStation ? _keybindingsConfig.PlayStationButtons : _keybindingsConfig.XboxButtons;

        foreach (var kvp in buttonMap)
        {
            if (string.Equals(kvp.Value, actionName, StringComparison.OrdinalIgnoreCase))
            {
                return GetButtonLabel((byte)kvp.Key, type);
            }
        }
        return null;
    }

    private void OnRawControllerInputReceived(object? sender, RawControllerInputEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.EventType == 1 && e.Value == 1) // Button Pressed
            {
                var type = ControllerConnectionStatus.DetectType(_activeControllerName);
                var buttonLabel = GetButtonLabel(e.Number, type);
                var mapped = GetActionForButton(e.Number, type);

                if (LiveTestFeedText != null)
                {
                    LiveTestFeedText.Text = Loc.Format("[LIVE FEED] {0} Pressed! -> Mapped Action: {1}", buttonLabel, ActionLabelFor(mapped));
                }

                if (!string.IsNullOrEmpty(_rebindingAction))
                {
                    var targetAction = _rebindingAction;
                    if (type == ControllerType.PlayStation)
                    {
                        _keybindingsConfig.PlayStationButtons[e.Number] = targetAction;
                    }
                    else
                    {
                        _keybindingsConfig.XboxButtons[e.Number] = targetAction;
                    }
                    _keybindingsConfig.EvdevKeys[e.EvdevCode != 0 ? e.EvdevCode : (ushort)(0x130 + e.Number)] = targetAction;

                    SaveKeybindingsConfig();
                    _rebindingAction = null;
                    if (_controllerInputService != null) _controllerInputService.IsRebinding = false;
                    BuildKeybindingsListUI();

                    if (LiveTestFeedText != null)
                    {
                        LiveTestFeedText.Text = Loc.Format("Successfully bound {0} to '{1}'!", buttonLabel, ActionLabelFor(targetAction));
                    }
                }
            }
        });
    }

    private string GetActionForButton(byte buttonNumber, ControllerType type)
    {
        var buttonMap = type == ControllerType.PlayStation ? _keybindingsConfig.PlayStationButtons : _keybindingsConfig.XboxButtons;
        if (buttonMap.TryGetValue(buttonNumber, out var act)) return act;
        return "Unmapped";
    }

    private void OnResetKeybindingsClicked(object? sender, RoutedEventArgs e)
    {
        _keybindingsConfig = new ControllerKeybindingsConfig();
        SaveKeybindingsConfig();
        BuildKeybindingsListUI();
        if (LiveTestFeedText != null)
        {
            LiveTestFeedText.Text = Loc.Get("Reset keybindings to factory defaults!");
        }
    }

    private async void OnMasterVolumePillChanged(object? sender, double value)
    {
        try
        {
            await _audioService.SetVolumeAsync(value).ConfigureAwait(true);
        }
        catch
        {
            // Best effort
        }
    }

    private async void OnMicVolumePillChanged(object? sender, double value)
    {
        _micVolume = Math.Round(value);
        if (_audioService != null)
        {
            try
            {
                await _audioService.SetInputVolumeAsync(value).ConfigureAwait(true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    private async void OnToggleOutputDeviceClicked(object? sender, RoutedEventArgs e)
    {
        if (_audioService == null) return;
        try
        {
            var devices = await _audioService.GetAudioDevicesAsync().ConfigureAwait(true);
            if (devices.Count <= 1) return; // Nothing to toggle to

            // Find current default
            var currentIndex = devices.ToList().FindIndex(d => d.IsDefault);
            if (currentIndex < 0) currentIndex = 0;

            var nextIndex = (currentIndex + 1) % devices.Count;
            var nextDevice = devices[nextIndex];

            var success = await _audioService.SetDefaultAudioDeviceAsync(nextDevice.Id).ConfigureAwait(true);
            if (success && OutputDeviceText != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    OutputDeviceText.Text = nextDevice.Name;
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to toggle output device: {ex.Message}");
        }
    }

    private void OnToggleInputDeviceClicked(object? sender, RoutedEventArgs e)
    {
        // Currently we don't have GetInputDevicesAsync in IAudioService
        // But for completeness we could add it. For now, leave it alone.
    }

    private async void OnWifiToggled(object? sender, RoutedEventArgs e)
    {
        if (WifiToggleSwitch.IsChecked.HasValue)
        {
            try
            {
                await _wirelessNetworkService.SetEnabledAsync(WifiToggleSwitch.IsChecked.Value).ConfigureAwait(true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    private void OnRefreshWifiClicked(object? sender, RoutedEventArgs e)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        _ = RefreshWifiNetworksAsync(_loadCts.Token);
    }

    private void OnRefreshBluetoothClicked(object? sender, RoutedEventArgs e)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        _ = RefreshBluetoothDevicesAsync(_loadCts.Token);
    }

    private void OnToggleResolutionClicked(object? sender, RoutedEventArgs e)
    {
        // Toggle resolution mock implementation or call a service if it exists
    }

    private async Task RefreshBluetoothDevicesAsync(CancellationToken cancellationToken)
    {
        if (_bluetoothService == null) return;
        try
        {
            var btStatus = await _bluetoothService.GetStatusAsync(cancellationToken).ConfigureAwait(true);
            Dispatcher.UIThread.Post(() =>
            {
                if (BluetoothListStack == null) return;
                BluetoothListStack.Children.Clear();
                
                if (btStatus.Devices.Count == 0)
                {
                    BluetoothListStack.Children.Add(Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
                    {
                        Text = "No Bluetooth devices found",
                        FontSize = 13,
                        Foreground = GetThemeBrush("ContentSecondaryBrush", Brushes.Gray)
                    }, "BodySmall"), TextBlock.TextProperty, "No Bluetooth devices found"));
                    return;
                }

                foreach (var device in btStatus.Devices.Take(5))
                {
                    var border = new Border
                    {
                        Padding = new Thickness(12, 10),
                        CornerRadius = new CornerRadius(10),
                        Background = GetThemeBrush("ActionSecondaryBrush", Brushes.DarkGray)
                    };

                    var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

                    var infoStack = new StackPanel { Spacing = 2 };
                    infoStack.Children.Add(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
                    {
                        Text = device.Name,
                        FontSize = 14,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = GetThemeBrush("ContentPrimaryBrush", Brushes.White)
                    }, "Body"));

                    infoStack.Children.Add(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
                    {
                        Text = Loc.Get(device.IsConnected ? "Connected" : (device.IsPaired ? "Paired" : "Available")),
                        FontSize = 12,
                        Foreground = GetThemeBrush("ContentSecondaryBrush", Brushes.Gray)
                    }, "Caption"));

                    Grid.SetColumn(infoStack, 0);
                    grid.Children.Add(infoStack);

                    if (device.IsConnected)
                    {
                        var icon = Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
                        {
                            Text = "check_circle",
                            Classes = { "SettingsIcon" },
                            FontSize = 20,
                            Foreground = GetThemeBrush("BrandPrimaryBrush", Brushes.DeepSkyBlue),
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        }, "Title"), TextBlock.TextProperty, "check_circle");
                        Grid.SetColumn(icon, 1);
                        grid.Children.Add(icon);
                    }

                    border.Child = grid;
                    BluetoothListStack.Children.Add(border);
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to refresh bluetooth: {ex.Message}");
        }
    }

    private void OnReturnToDesktopClicked(object? sender, RoutedEventArgs e)
    {
        HideOverlay();
        ReturnToDesktopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRestartClicked(object? sender, RoutedEventArgs e)
    {
        if (_powerService != null) _ = _powerService.RebootAsync();
    }

    private void OnShutdownClicked(object? sender, RoutedEventArgs e)
    {
        if (_powerService != null) _ = _powerService.ShutdownAsync();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        HideOverlay();
    }

    private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        HideOverlay();
    }

    private static IBrush GetThemeBrush(string key, IBrush fallback)
    {
        if (Application.Current?.TryFindResource(key, out var resource) == true && resource is IBrush brush)
        {
            return brush;
        }
        return fallback;
    }
}
