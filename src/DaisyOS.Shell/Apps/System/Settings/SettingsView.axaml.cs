using System;
using System.Collections.Generic;
using System.Globalization;
using DaisyOS.Shell.Services.Idle;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.System.Audio;
using DaisyOS.System.Bluetooth;
using DaisyOS.System.Display;
using DaisyOS.System.Input;
using DaisyOS.System.Networking;
using DaisyOS.System.Power;
using DaisyOS.System.Processes;
using DaisyOS.System.Time;
using DaisyOS.System.Updates;
using DaisyOS.System.Appearance;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Motion;
using DaisyOS.Shell.Services.Wallpaper;
using DaisyOS.Shell.Services.Wallpaper.VideoWallpaper;
using DaisyOS.Shell.ViewModels;

namespace DaisyOS.Shell.Apps.System.Settings
{
    public partial class SettingsView : UserControl
    {
        private readonly DispatcherTimer _heicPreviewTimer = new() { Interval = TimeSpan.FromMinutes(1) };
        private Bitmap? _wallpaperPreview;
        private readonly List<Bitmap> _retiredWallpaperPreviews = [];
        private bool _isRefreshingAppearanceControls;
        private int _categoryBuildVersion;
        private int _selectedDisplayIndex = 0;
        private readonly DisplayChangeGuard _displayChangeGuard = new();
        private CancellationTokenSource _displayLifetime = new();
        private CancellationTokenSource? _storageScan;
        private StorageDetailState? _storageDetail;
        private sealed record StorageDetailState(DaisyOS.System.Storage.StorageUsageCategory Category, string? FolderPath = null, StorageDetailState? Parent = null);
        private Task<DisplayChangeResult>? _pendingDisplayChange;
        private Window? _displayOwner;
        private bool _closingForDisplayRevert;
        private bool _allowDisplayOwnerClose;
        private bool _isScanningBluetooth = false;
        private readonly LinuxWirelessNetworkService _settingsWirelessService = new(new SafeCommandRunner());
        private readonly LinuxBluetoothService _settingsBluetoothService = new(new SafeCommandRunner());
        private readonly DispatcherTimer _connectionsRefreshTimer = new() { Interval = TimeSpan.FromSeconds(8) };
        private Task? _automaticNetworkRefresh;
        private CancellationTokenSource? _bluetoothDiscoveryLifetime;
        private string? _previewWallpaperUri;
        private int _previewRequestVersion;
        private string? _pendingSection;
        private string? _pendingSettingTitle;
        private string _activeCategory = "System";
        private WallpaperSelection _wallpaperSelection = WallpaperSelection.Picture;
        private bool _personalizationListsLoaded;
        private readonly List<string> _availableFontFamilies = [];

        private enum WallpaperSelection
        {
            Picture,
            Video,
            Solid
        }

        private static readonly (string Title, string[] Categories)[] NavigationGroups =
        [
            ("SYSTEM", ["System", "Displays", "Sound"]),
            ("CONNECTIONS", ["Network", "Ethernet", "Bluetooth"]),
            ("PERSONALIZATION", ["Appearance", "Notifications", "Apps"]),
            ("MANAGEMENT", ["Storage", "Accessibility", "Input", "DateTime", "About"])
        ];

        private static readonly SettingsSearchEntry[] SearchIndex =
        [
            new("System", "POWER", "Power mode", "Battery saver, performance mode, energy", "power battery energy performance saver"),
            new("System", "DESKTOP", "System bar date", "Show the date beside the clock", "clock date taskbar system bar"),
            new("Displays", "DISPLAY", "Resolution", "Choose display resolution", "resolution display screen size monitor 1080p 1440p 4k"),
            new("Displays", "DISPLAY", "Display scale", "Text and app size", "scale zoom display text size"),
            new("Displays", "DISPLAY", "Refresh rate", "Choose a display refresh rate", "hertz hz 60 120 144 monitor"),
            new("Displays", "DISPLAY", "Night light", "Reduce blue light in the evening", "night light blue comfort warm"),
            new("Displays", "DISPLAY", "HDR", "Use high dynamic range when available", "hdr high dynamic range"),
            new("Sound", "OUTPUT", "Output volume", "System output volume", "volume speaker audio sound"),
            new("Sound", "INPUT", "Microphone", "Microphone volume and mute", "mic microphone input mute"),
            new("Network", "WI-FI", "Wi-Fi", "Wireless radio and available networks", "wifi wireless network internet connect password"),
            new("Ethernet", "ETHERNET", "Ethernet", "Wired connection status", "ethernet wired lan network cable internet"),
            new("Bluetooth", "BLUETOOTH", "Bluetooth", "Devices, pairing, and discoverability", "bluetooth headset controller devices"),
            new("Appearance", "", "Theme", "Light, dark, and automatic appearance", "dark light theme mode appearance"),
            new("Appearance", "", "Taskbar size", "Minimum, default or maximum", "taskbar dock size height scale"),
            new("Appearance", "", "Taskbar and systembar", "Always visible or auto-hide", "taskbar systembar system bar dock auto-hide maximize fullscreen"),
            new("Appearance", "", "Profile picture", "Shown in the launcher and on the lock screen.", "profile avatar account user photo picture"),
            new("Appearance", "", "Wallpaper", "Desktop picture or video wallpaper", "wallpaper background picture video live"),
            new("Appearance", "", "Accent color", "Control and focus colors", "accent colour color focus"),
            new("Input", "CURSOR", "Cursor", "Pointer theme and cursor size", "cursor pointer theme size mouse"),
            new("Appearance", "", "System font", "Font family and text size", "font typography text size add font"),
            new("Accessibility", "DISPLAY", "High contrast", "Increase contrast between text and surfaces", "high contrast accessibility visibility"),
            new("Accessibility", "VISION", "Reduce motion", "Reduce desktop movement animations", "motion animation accessibility"),
            new("Notifications", "FOCUS", "Do not disturb", "Pause notification interruptions", "do not disturb focus notifications quiet"),
            new("InstalledApps", "", "Installed apps", "Repair or uninstall applications", "installed apps applications uninstall repair remove modify"),
            new("Apps", "DEFAULT APPLICATIONS", "Default apps", "Web links and new-file defaults", "default app browser links files"),
            new("Input", "LANGUAGE", "System language", "Choose the desktop language", "language locale translation english finnish suomi kieli"),
            new("Input", "KEYBOARD LAYOUT", "Keyboard layout", "Choose the language and physical layout used while typing", "keyboard layout language typing input finnish us english"),
            new("Input", "MOUSE & TRACKPAD", "Mouse and trackpad", "Pointer speed, scroll speed, natural scrolling, tap to click", "mouse trackpad pointer scrolling scroll speed tap"),
            new("Input", "MOUSE & TRACKPAD", "Trackpad scroll speed", "Slowest", "trackpad touchpad scroll scrolling speed slider mouse"),
            new("DateTime", "TIME", "Automatic date and time", "System time, time zone, and clock format", "date time timezone time zone clock ntp automatic"),
            new("DateTime", "TIME", "Time format", "Choose between 24-hour and 12-hour time", "time format clock 12h 24h 12-hour 24-hour am pm"),
            new("About", "DAISYOS", "About this system", "Version and desktop-session information", "version about diagnostics support")
        ];

        public SettingsView()
        {
            InitializeComponent();
            OrganizeNavigation();
            ContentArea.SizeChanged += (_, _) => UpdateContentWidth();
            NavigationSearch.PropertyChanged += (_, args) =>
            {
                if (args.Property == SearchBar.TextProperty)
                {
                    FilterNavigation(args.GetNewValue<string>() ?? string.Empty);
                }
            };
            FontSearchBar.PropertyChanged += (_, args) =>
            {
                if (args.Property == SearchBar.TextProperty)
                    FilterFontPicker(FontSearchBar.Text);
            };
            FontSearchBar.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    SystemFontPicker.IsDropDownOpen = false;
                    SystemFontPicker.Focus();
                    args.Handled = true;
                }
                else if (args.Key is Key.Down or Key.Enter)
                {
                    var first = SystemFontPicker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.IsVisible);
                    if (first is not null)
                    {
                        if (args.Key == Key.Enter)
                        {
                            SystemFontPicker.SelectedItem = first;
                            SystemFontPicker.IsDropDownOpen = false;
                            SystemFontPicker.Focus();
                        }
                        else first.Focus();
                    }
                    args.Handled = true;
                }
            };
            SystemFontPicker.DropDownOpened += (_, _) =>
            {
                FontSearchBar.Text = string.Empty;
                Dispatcher.UIThread.Post(() => FontSearchBar.FocusInput());
            };
            _heicPreviewTimer.Tick += (_, _) =>
            {
                if (_activeCategory == "Appearance" && _previewWallpaperUri is { } uri && WallpaperFileTypes.IsHeic(uri))
                    RefreshWallpaperPreview(uri);
            };
            _connectionsRefreshTimer.Tick += (_, _) =>
            {
                if (string.Equals(_activeCategory, "Network", StringComparison.Ordinal) &&
                    _automaticNetworkRefresh is not { IsCompleted: false })
                {
                    var buildVersion = ++_categoryBuildVersion;
                    _automaticNetworkRefresh = BuildNetworkAvailabilityAsync(buildVersion, null);
                }
            };
            ActualThemeVariantChanged += (_, _) =>
            {
                if (_previewWallpaperUri is { } uri && WallpaperFileTypes.IsHeic(uri)) RefreshWallpaperPreview(uri);
            };
            AttachedToVisualTree += (_, _) =>
            {
                if (_displayLifetime.IsCancellationRequested)
                {
                    _displayLifetime.Dispose();
                    _displayLifetime = new CancellationTokenSource();
                }
                _heicPreviewTimer.Start();
                _allowDisplayOwnerClose = _closingForDisplayRevert = false;
                _displayOwner = TopLevel.GetTopLevel(this) as Window;
                if (_displayOwner is not null) _displayOwner.Closing += OnDisplayOwnerClosing;
                if (Application.Current is App app)
                {
                    app.AppearanceChanged -= OnAppearanceChanged;
                    app.AppearanceChanged += OnAppearanceChanged;
                }
                UpdateContentWidth();
                RefreshAppearanceControls();
                ShowCategory("System");
            };
            DetachedFromVisualTree += (_, _) =>
            {
                _heicPreviewTimer.Stop();
                _connectionsRefreshTimer.Stop();
                StopAutomaticBluetoothDiscovery();
                _displayLifetime.Cancel();
                _storageScan?.Cancel();
                if (_displayOwner is not null) _displayOwner.Closing -= OnDisplayOwnerClosing;
                _displayOwner = null;
                Interlocked.Increment(ref _previewRequestVersion);
                if (Application.Current is App app)
                {
                    app.AppearanceChanged -= OnAppearanceChanged;
                }
                _previewWallpaperUri = null;
                WallpaperPreviewImage.Source = null;
                _wallpaperPreview?.Dispose();
                _wallpaperPreview = null;
                foreach (var preview in _retiredWallpaperPreviews) preview.Dispose();
                _retiredWallpaperPreviews.Clear();
            };
        }

        private void OnNavCategoryClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button clickedBtn)
            {
                SelectCategory(clickedBtn.Tag as string ?? "System");
            }
        }

        private void OnSettingsKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                NavigationSearch.FocusInput();
                e.Handled = true;
            }
        }

        private void OnNavigationSearchKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Down or Key.Up)
            {
                var results = NavigationSearchResults.Children.OfType<Button>().ToArray();
                var result = e.Key == Key.Down ? results.FirstOrDefault() : results.LastOrDefault();
                if (result is not null)
                {
                    result.Focus(NavigationMethod.Directional);
                    e.Handled = true;
                }
                return;
            }
            if (e.Key == Key.Escape && !string.IsNullOrEmpty(NavigationSearch.Text))
            {
                NavigationSearch.Text = string.Empty;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && NavigationSearchResults.Children.OfType<Button>().FirstOrDefault() is { } firstResult)
            {
                OpenSearchResult(firstResult);
                e.Handled = true;
            }
        }

        private void OnSystemSettingItemClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string settingKey })
            {
                if (string.Equals(settingKey, "Performance", StringComparison.OrdinalIgnoreCase))
                {
                    (Application.Current as App)?.ShowTaskManager();
                    return;
                }
                if (string.Equals(settingKey, "Clipboard", StringComparison.OrdinalIgnoreCase))
                {
                    (Application.Current as App)?.ShowClipboardHistory();
                    return;
                }

                ShowCategory("System");
            }
        }

        private void OnBackToSystemListClicked(object? sender, RoutedEventArgs e)
        {
            SelectCategory("System");
        }

        private void OrganizeNavigation()
        {
            var buttonsByCategory = NavigationItems.Children
                .OfType<Button>()
                .Concat(GetNavigationButtons())
                .Where(button => button.Tag is string)
                .Distinct()
                .ToDictionary(button => (string)button.Tag!, StringComparer.Ordinal);

            foreach (var button in buttonsByCategory.Values)
            {
                if (button.Parent is Panel parent)
                {
                    parent.Children.Remove(button);
                }
            }

            NavigationItems.Children.Clear();

            foreach (var (title, categories) in NavigationGroups)
            {
                var group = new StackPanel { Spacing = 4 };
                group.Children.Add(new TextBlock { Text = SentenceCase(Loc.Get(title)), Classes = { "SettingsNavGroupHeader" } });
                foreach (var category in categories)
                {
                    if (buttonsByCategory.TryGetValue(category, out var button))
                    {
                        group.Children.Add(button);
                    }
                }

                NavigationItems.Children.Add(group);
            }
        }

        private IEnumerable<Button> GetNavigationButtons() =>
            NavigationItems.Children
                .OfType<StackPanel>()
                .SelectMany(group => group.Children.OfType<Button>());

        private Button CreateSearchResult(SettingsSearchEntry entry)
        {
            var button = new Button { Tag = entry };
            button.Classes.Add("SettingsSearchResult");
            button.Click += (_, _) => OpenSearchResult(button);
            button.KeyDown += (_, e) =>
            {
                if (e.KeyModifiers != KeyModifiers.None) return;
                if (e.Key == Key.Escape)
                {
                    NavigationSearch.Text = string.Empty;
                    NavigationSearch.FocusInput();
                    e.Handled = true;
                }
                else if (e.Key is Key.Down or Key.Up)
                {
                    var results = NavigationSearchResults.Children.OfType<Button>().ToArray();
                    var index = Array.IndexOf(results, button) + (e.Key == Key.Down ? 1 : -1);
                    if (index < 0) NavigationSearch.FocusInput();
                    else if (index < results.Length) results[index].Focus(NavigationMethod.Directional);
                    e.Handled = true;
                }
            };
            var content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };
            content.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock { Text = Loc.Get(entry.Title), FontSize = 13, FontWeight = FontWeight.Regular }, "BodySmall"),
                    DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(entry.Section)
                            ? $"{GetCategoryTitle(entry.Category)} · {Loc.Get(entry.Description)}"
                            : $"{GetCategoryTitle(entry.Category)} · {Loc.Get(entry.Section)}",
                        FontSize = 11,
                        Foreground = TryBrush("ContentSecondaryBrush"),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }, "Caption")
                }
            });
            var chevron = new DaisyIcon
            {
                Icon = "caret-right",
                Size = 16,
                Foreground = TryBrush("ContentTertiaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(chevron, 1);
            content.Children.Add(chevron);
            button.Content = content;
            return button;
        }

        private void OpenSearchResult(Button button)
        {
            if (button.Tag is not SettingsSearchEntry entry)
            {
                return;
            }

            _pendingSettingTitle = entry.Title;
            NavigationSearch.Text = string.Empty;
            SelectCategory(entry.Category, entry.Section);
        }

        private void FilterNavigation(string query)
        {
            var search = query.Trim();
            NavigationItems.IsVisible = string.IsNullOrWhiteSpace(search);
            NavigationSearchResultsPanel.IsVisible = !string.IsNullOrWhiteSpace(search);
            NavigationSearchResults.Children.Clear();

            if (string.IsNullOrWhiteSpace(search))
            {
                return;
            }

            var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var matches = SearchIndex
                .Where(entry => words.All(word => entry.SearchText.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(entry => Loc.Get(entry.Title).StartsWith(search, StringComparison.OrdinalIgnoreCase))
                .ThenBy(entry => entry.Category)
                .ThenBy(entry => entry.Title)
                .Take(7)
                .ToArray();

            if (matches.Length == 0)
            {
                NavigationSearchResults.Children.Add(Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
                {
                    Text = "No settings found. Try a broader term.",
                    FontSize = 12,
                    Foreground = TryBrush("ContentSecondaryBrush"),
                    Margin = new Thickness(12, 8)
                }, "Caption"), TextBlock.TextProperty, "No settings found. Try a broader term."));
                return;
            }

            foreach (var entry in matches)
            {
                NavigationSearchResults.Children.Add(CreateSearchResult(entry));
            }
        }

        public void SelectCategory(string category, string? section = null)
        {
            var navigationCategory = category == "InstalledApps" ? "Apps" : category;
            foreach (var button in GetNavigationButtons())
            {
                button.Classes.Set("Active", string.Equals(button.Tag as string, navigationCategory, StringComparison.Ordinal));
            }

            ShowCategory(category, section);
        }

        private void ShowCategory(string category, string? section = null)
        {
            if (!string.Equals(category, "Bluetooth", StringComparison.Ordinal)) StopAutomaticBluetoothDiscovery();
            if (string.Equals(category, "Network", StringComparison.Ordinal)) _connectionsRefreshTimer.Start();
            else _connectionsRefreshTimer.Stop();
            _storageScan?.Cancel();
            _storageDetail = null;
            var pageChanged = !string.Equals(_activeCategory, category, StringComparison.Ordinal);
            _activeCategory = category;
            var buildVersion = ++_categoryBuildVersion;
            _pendingSection = section;
            var isAppearance = string.Equals(category, "Appearance", StringComparison.Ordinal);
            SystemListView.IsVisible = false;
            SystemDetailView.IsVisible = false;
            CategoryView.IsVisible = !isAppearance;
            AppearanceView.IsVisible = isAppearance;

            if (isAppearance)
            {
                _pendingSection = null;
                RefreshAppearanceControls();
                _ = LoadPersonalizationOptionsAsync();
                if (pageChanged) _ = GuiMotion.RevealAsync(AppearanceContent, 6);
                FocusAppearanceSearchTarget(_pendingSettingTitle);
                return;
            }

            BuildCategory(category, buildVersion);
            if (pageChanged) _ = GuiMotion.RevealAsync(CategoryContent, 6);
        }

        private static string GetCategoryTitle(string category) => category switch
            {
                "Displays" => Loc.Get("Displays"),
                "Sound" => Loc.Get("Sound"),
                "Network" => Loc.Get("Wi-Fi"),
                "Ethernet" => Loc.Get("Ethernet"),
                "Bluetooth" => Loc.Get("Bluetooth"),
                "Notifications" => Loc.Get("Notifications"),
                "Apps" => Loc.Get("Apps"),
                "InstalledApps" => Loc.Get("Installed apps"),
                "Storage" => Loc.Get("Storage"),
                "Accessibility" => Loc.Get("Accessibility"),
                "Privacy" => Loc.Get("Privacy & security"),
                "Accounts" => Loc.Get("Accounts"),
                "Input" => Loc.Get("Language & input"),
                "DateTime" => Loc.Get("Date & time"),
                "Gaming" => Loc.Get("Gaming"),
                "About" => Loc.Get("About this system"),
                _ => Loc.Get("System")
            };

        private void BuildCategory(string category, int buildVersion)
        {
            CategoryContent.Children.Clear();
            var title = GetCategoryTitle(category);

            var pageHeader = new StackPanel { Spacing = 8 };
            if (category == "InstalledApps")
            {
                var appsCrumb = new Button
                {
                    Content = Loc.Bind(new TextBlock
                    {
                        Text = "Apps",
                        Classes = { "SettingsPageTitle" },
                        Foreground = TryBrush("ContentSecondaryBrush")
                    }, TextBlock.TextProperty, "Apps")
                };
                appsCrumb.Classes.Add("BreadcrumbItem");
                appsCrumb.Click += (_, _) => SelectCategory("Apps");
                ToolTip.SetTip(appsCrumb, Loc.Get("Back to Apps"));

                pageHeader.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        appsCrumb,
                        new DaisyIcon { Icon = "caret-right", Size = 18, Foreground = TryBrush("ContentTertiaryBrush"), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = title, Classes = { "SettingsPageTitle" }, VerticalAlignment = VerticalAlignment.Center }
                    }
                });
            }
            else
            {
                var pageTitle = new TextBlock
                {
                    Text = title,
                    Classes = { "SettingsPageTitle" }
                };
                pageHeader.Children.Add(pageTitle);
            }
            var description = category switch
            {
                "Displays" => Loc.Get("Arrange screens and tune how content is presented."),
                "Sound" => Loc.Get("Choose devices and control every active audio stream."),
                "Network" => Loc.Get("Manage Wi-Fi and wireless networks."),
                "Ethernet" => Loc.Get("Manage your wired connection."),
                "Bluetooth" => Loc.Get("Connect and manage nearby accessories."),
                "Notifications" => Loc.Get("Control when the desktop may interrupt you."),
                "Apps" => Loc.Get("Manage installed applications and choose which apps open common content."),
                "InstalledApps" => Loc.Get("Find, repair, or remove applications installed on this computer."),
                "Storage" => Loc.Get("Understand how space is used across mounted drives."),
                "Accessibility" => Loc.Get("Make DaisyOS more comfortable to see and use."),
                "Input" => Loc.Get("Configure your keyboard, mouse, and trackpad."),
                "DateTime" => Loc.Get("Keep the system clock and time zone accurate."),
                "About" => Loc.Get("Hardware, operating system, and session information."),
                _ => Loc.Get("Essential desktop and power preferences.")
            };
            pageHeader.Children.Add(Secondary(description));
            CategoryContent.Children.Add(pageHeader);

            switch (category)
            {
                case "Displays": BuildDisplays(buildVersion); break;
                case "Sound": BuildSound(buildVersion); break;
                case "Network": BuildNetwork(buildVersion); break;
                case "Ethernet": BuildEthernet(buildVersion); break;
                case "Bluetooth": BuildBluetooth(buildVersion); break;
                case "Notifications": BuildNotifications(); break;
                case "Apps": BuildApps(buildVersion); break;
                case "InstalledApps": BuildInstalledApplicationsSection(buildVersion); break;
                case "Storage": BuildStorage(); break;
                case "Accessibility": BuildAccessibility(); break;
                case "Input": BuildInput(); break;
                case "DateTime": BuildDateTime(buildVersion); break;
                case "About": BuildAbout(buildVersion); break;
                default: BuildSystem(buildVersion); break;
            }
        }

        private void BuildSystem(int buildVersion)
        {
            var checking = Secondary(Loc.Get("Reading system power profile…"));
            CategoryContent.Children.Add(checking);
            _ = BuildSystemAsync(buildVersion, checking);
        }

        private async Task BuildSystemAsync(int buildVersion, TextBlock checking)
        {
            var powerService = new LinuxPowerService(new SafeCommandRunner());
            var runner = new SafeCommandRunner();
            var profileTask = runner.RunAsync("powerprofilesctl", ["get"], TimeSpan.FromSeconds(2));
            var batteryTask = new LinuxBatteryStatusService().GetStatusAsync();
            await Task.WhenAll(profileTask, batteryTask);
            var profileStatus = await profileTask;
            var batteryStatus = await batteryTask;

            if (buildVersion != _categoryBuildVersion) return;
            CategoryContent.Children.Remove(checking);

            if (profileStatus.Succeeded && !string.IsNullOrWhiteSpace(profileStatus.StandardOutput))
            {
                var currentProfile = profileStatus.StandardOutput.Trim() switch
                {
                    "power-saver" => "Power saver",
                    "performance" => "Performance",
                    _ => "Balanced"
                };
                AddSection(Loc.Get("POWER"), Loc.Get("Choose how the system balances battery life and speed."),
                    Picker("Power mode", "power.mode", currentProfile, async mode =>
                    {
                        if (!await powerService.SetPowerProfileAsync(mode))
                            (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t change the power mode. The system power-profile service rejected the request."));
                        ShowCategory("System");
                    }, "Balanced", "Power saver", "Performance"));
            }
            else
            {
                AddUnavailableMessage(Loc.Get("Power modes aren’t available because the system power-profile service is missing or stopped."));
            }

            if (batteryStatus.IsPresent)
            {
                if (batteryStatus.ChargePercent is { } chargePercent)
                {
                    var batteryView = new BatteryStatusView
                    {
                        ChargePercent = chargePercent,
                        IsCharging = BatteryStatusView.IsChargingState(batteryStatus.State),
                        StateText = Loc.Get(batteryStatus.State),
                        ShowState = true,
                        Foreground = TryBrush("ContentSecondaryBrush"),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ToolTip.SetTip(batteryView, batteryStatus.Detail);
                    AddSection(Loc.Get("BATTERY"), Loc.Get("Live status reported by the system power supply."),
                        PreferenceRow(Loc.Get("Battery"), null, batteryView));
                }
                else
                {
                    AddSection(Loc.Get("BATTERY"), Loc.Get("Live status reported by the system power supply."),
                        InfoRow(Loc.Get("Battery"), Loc.Get(batteryStatus.State)));
                }
            }

            AddPowerAndSleepSections(batteryStatus.IsPresent);

            AddSection(Loc.Get("DESKTOP"), null,
                Toggle("Show date in the system bar", "system.show-date", false, value => { if (Application.Current is App app) app.SessionState.ShowDateInSystemBar = value; }));
        }

        private void AddPowerAndSleepSections(bool hasBattery)
        {
            var app = Application.Current as App;
            string Current(string key, string fallback) => app?.GetPreference(key, fallback) ?? fallback;
            Action<string> Save(string pKey) => value => app?.SetPreference(pKey, value);
            Action<bool> SaveBool(string pKey) => value => app?.SetPreference(pKey, value ? "true" : "false");

            // Older builds stored the lock timeout under the legacy key.
            var lockTimeout = Current(PowerPreferences.LockTimeoutKey,
                Current(PowerPreferences.LegacyScreenTimeoutKey, PowerPreferences.DefaultLockTimeout));

            var sleepRows = new List<Control>
            {
                Picker("Turn off the display after", PowerPreferences.ScreenOffTimeoutKey,
                    Current(PowerPreferences.ScreenOffTimeoutKey, PowerPreferences.DefaultScreenOffTimeout),
                    Save(PowerPreferences.ScreenOffTimeoutKey), PowerPreferences.ScreenOffTimeoutOptions),
                Picker("Lock the screen after", PowerPreferences.LockTimeoutKey, lockTimeout,
                    Save(PowerPreferences.LockTimeoutKey), PowerPreferences.LockTimeoutOptions),
                Picker(hasBattery ? "Sleep when plugged in after" : "Sleep after", PowerPreferences.SuspendTimeoutKey,
                    Current(PowerPreferences.SuspendTimeoutKey, PowerPreferences.DefaultSuspendTimeout),
                    Save(PowerPreferences.SuspendTimeoutKey), PowerPreferences.SuspendTimeoutOptions),
            };
            if (hasBattery)
            {
                sleepRows.Add(Picker("Sleep on battery after", PowerPreferences.SuspendOnBatteryTimeoutKey,
                    Current(PowerPreferences.SuspendOnBatteryTimeoutKey, PowerPreferences.DefaultSuspendOnBatteryTimeout),
                    Save(PowerPreferences.SuspendOnBatteryTimeoutKey), PowerPreferences.SuspendTimeoutOptions));
            }
            sleepRows.Add(Toggle("Lock the screen when going to sleep", PowerPreferences.LockOnSuspendKey,
                PowerPreferences.ParseBool(Current(PowerPreferences.LockOnSuspendKey, "true"), true),
                SaveBool(PowerPreferences.LockOnSuspendKey)));

            AddSection(Loc.Get("SCREEN AND SLEEP"),
                Loc.Get("Choose what happens when you stop using the computer. The display stays on and the computer stays awake while Gaming Mode is on or an app such as a video player asks to keep it awake."),
                sleepRows.ToArray());

            var energyRows = new List<Control>();
            if (hasBattery)
            {
                energyRows.Add(Toggle("Pause live wallpapers on battery", PowerPreferences.PauseLiveWallpaperOnBatteryKey,
                    PowerPreferences.ParseBool(Current(PowerPreferences.PauseLiveWallpaperOnBatteryKey, "true"), true),
                    SaveBool(PowerPreferences.PauseLiveWallpaperOnBatteryKey)));
            }
            energyRows.Add(Toggle("Free desktop memory while gaming or locked", PowerPreferences.ReleaseMemoryWhenGamingKey,
                PowerPreferences.ParseBool(Current(PowerPreferences.ReleaseMemoryWhenGamingKey, "true"), true),
                SaveBool(PowerPreferences.ReleaseMemoryWhenGamingKey)));
            AddSection(Loc.Get("ENERGY AND MEMORY"),
                Loc.Get("Reduce background work and return unused desktop memory to games and apps."),
                energyRows.ToArray());
        }

        private void BuildDisplays(int buildVersion)
        {
            var checking = Secondary(Loc.Get("Querying display configuration…"));
            CategoryContent.Children.Add(checking);
            _ = BuildDisplaysAsync(buildVersion, checking);
        }

        private async Task BuildDisplaysAsync(int buildVersion, TextBlock checking)
        {
            var displays = await DisplaySettingsHelper.DetectAllDisplaysAsync();
            if (buildVersion != _categoryBuildVersion) return;
            CategoryContent.Children.Remove(checking);
            if (displays.Count == 0)
            {
                AddUnavailableMessage(Loc.Get("Display settings aren’t available because neither KScreen nor XRandR could read an active display."));
                return;
            }

            if (_selectedDisplayIndex >= displays.Count) _selectedDisplayIndex = 0;
            var display = displays[_selectedDisplayIndex];
            if (display is null)
            {
                AddUnavailableMessage(Loc.Get("The selected display disappeared while its settings were loading."));
                return;
            }

            if (displays.Count > 1)
            {
                var monitorSelectorPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                for (int i = 0; i < displays.Count; i++)
                {
                    var d = displays[i];
                    var idx = i;
                    var isSelected = idx == _selectedDisplayIndex;
                    var btn = DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
                    {
                        Content = $"{d.Name}{(d.IsPrimary ? Loc.Get(" • Main") : "")}",
                        Classes = { isSelected ? "Subtle" : "Ghost" },
                        Height = 32,
                        Padding = new Thickness(12, 0)
                    });
                    btn.Click += (_, _) =>
                    {
                        _selectedDisplayIndex = idx;
                        BuildDisplays(_categoryBuildVersion);
                    };
                    monitorSelectorPanel.Children.Add(btn);
                }

                var multiDisplayControls = new List<Control>
                {
                    PreferenceRow(Loc.Get("Active display"), Loc.Get("Select a screen to view and change its configuration."), monitorSelectorPanel)
                };

                if (display is not null && !display.IsPrimary)
                {
                    var makePrimaryBtn = Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
                    {
                        Content = "Make this my main display",
                        Classes = { "Subtle", "Small" },
                        Padding = new Thickness(14, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    }), Button.ContentProperty, "Make this my main display");
                    var outName = display.Name;
                    makePrimaryBtn.Click += async (_, _) =>
                    {
                        makePrimaryBtn.IsEnabled = false;
                        IReadOnlyList<DetectedDisplay>? previous = null;
                        try
                        {
                            await RunDisplayChangeAsync(async () =>
                            {
                                previous = await DisplaySettingsHelper.DetectAllDisplaysAsync();
                                return previous.Any(d => d.Name == outName) && await DisplaySettingsHelper.SetPrimaryDisplayAsync(outName);
                            }, async () => previous is null || previous.Count == 0 ||
                                (await new SafeCommandRunner().RunAsync("kscreen-doctor",
                                    previous.Select(d => $"output.{d.Name}.priority.{d.Priority}").ToArray(), TimeSpan.FromSeconds(3))).Succeeded, buildVersion);
                        }
                        finally { makePrimaryBtn.IsEnabled = true; }
                    };
                    multiDisplayControls.Add(PreferenceRow(Loc.Get("Main display"), Loc.Get("Windows and the taskbar prioritize the main display."), makePrimaryBtn));
                }

                AddSection(Loc.Get("DISPLAY ARRANGEMENT"), Loc.Format("{0} displays connected.", displays.Count), multiDisplayControls.ToArray());
            }

            var outputName = display!.Name;
            var currentResolutionText = $"{display.Width} × {display.Height}";
            var currentScalePercent = $"{(int)Math.Round(display.Scale * 100)}%";
            var currentRefreshText = $"{DisplaySettingsHelper.FormatRefreshRate(display.RefreshRate)} Hz";

            var resolutionOptions = display.AvailableResolutions.Count > 0
                ? display.AvailableResolutions.ToArray()
                : DisplaySettingsHelper.StandardFallbackResolutions;
            if (!resolutionOptions.Contains(currentResolutionText))
            {
                resolutionOptions = [currentResolutionText, ..resolutionOptions];
            }

            var scaleOptions = new[] { "100%", "125%", "150%", "175%", "200%" };
            if (!scaleOptions.Contains(currentScalePercent))
            {
                scaleOptions = [currentScalePercent, ..scaleOptions];
            }

            var rateOptions = display.AvailableRefreshRates.Count > 0
                ? display.AvailableRefreshRates.Select(r => $"{DisplaySettingsHelper.FormatRefreshRate(r)} Hz").Distinct().ToArray()
                : new[] { currentRefreshText };

            var displayRows = new List<Control>();
            displayRows.Add(InfoRow(Loc.Get("Selected display"), $"{display.Name} ({display.Width} × {display.Height}){(display.IsPrimary ? " • Main Display" : "")}"));

            displayRows.Add(Picker("Resolution", "display.resolution", currentResolutionText, selectedResolution =>
            {
                _ = ApplyDisplayResolutionAsync(outputName, selectedResolution, buildVersion);
            }, resolutionOptions));

            displayRows.Add(Picker("Preferred scale", "display.scale", currentScalePercent, scale =>
            {
                if (!double.TryParse(scale.TrimEnd('%'), global::System.Globalization.NumberStyles.Float,
                    global::System.Globalization.CultureInfo.InvariantCulture, out var percentage)) return;
                _ = ApplyKScreenSettingAsync(outputName, "display scale", _ =>
                    $"output.{outputName}.scale.{(percentage / 100).ToString(global::System.Globalization.CultureInfo.InvariantCulture)}", buildVersion);
            }, scaleOptions));

            displayRows.Add(Picker("Refresh rate", "display.refresh-rate", currentRefreshText, rate =>
            {
                if (!double.TryParse(rate.Replace(" Hz", ""), global::System.Globalization.NumberStyles.Float,
                    global::System.Globalization.CultureInfo.InvariantCulture, out var hertz)) return;
                _ = ApplyKScreenSettingAsync(outputName, "refresh rate", current =>
                    DisplaySettingsHelper.BuildKScreenModeArgument(outputName,
                        current.Modes.FirstOrDefault(m => m.Width == current.Width && m.Height == current.Height && Math.Abs(m.RefreshRate - hertz) < .1),
                        current.Width, current.Height, hertz), buildVersion);
            }, rateOptions));

            if (display.HdrAvailable)
            {
                var hdrToggle = new ToggleSwitch { IsChecked = display.HdrEnabled, VerticalAlignment = VerticalAlignment.Center };
                hdrToggle.Click += async (_, _) =>
                {
                    var enabled = hdrToggle.IsChecked == true;
                    hdrToggle.IsEnabled = false;
                    try { await ApplyHdrSettingAsync(outputName, enabled, buildVersion); }
                    finally { hdrToggle.IsEnabled = true; }
                };
                displayRows.Add(PreferenceRow(Loc.Get("Use HDR"),
                    Loc.Get("Show a wider range of brightness and color on compatible displays."), hdrToggle));
            }

            var nightLight = new LinuxNightLightService();
            var nightLightEnabled = await nightLight.IsNightLightEnabledAsync();
            if (buildVersion != _categoryBuildVersion) return;
            var nightLightToggle = new ToggleSwitch { IsChecked = nightLightEnabled, VerticalAlignment = VerticalAlignment.Center };
            nightLightToggle.Click += async (_, _) =>
            {
                var enabled = nightLightToggle.IsChecked == true;
                nightLightToggle.IsEnabled = false;
                try
                {
                    var tempStr = (Application.Current as App)?.GetPreference("display.night-light-temp", "4000") ?? "4000";
                    if (!uint.TryParse(tempStr, out var temp) || temp == 0) temp = 4000;
                    NightLightConfiguration? previous = null;
                    await RunDisplayChangeAsync(async () =>
                    {
                        previous = await nightLight.CaptureConfigurationAsync();
                        return previous is not null && await nightLight.SetNightLightAsync(enabled, temp);
                    }, () => previous is null ? Task.FromResult(true) : nightLight.RestoreConfigurationAsync(previous), buildVersion);
                }
                finally { nightLightToggle.IsEnabled = true; }
            };
            displayRows.Add(PreferenceRow(Loc.Get("Use night light"), null, nightLightToggle));

            AddSection(Loc.Get("DISPLAY"), Loc.Get("Resolution, scaling, and refresh rate for active displays."), displayRows.ToArray());

        }

        private static string ModeArgument(DetectedDisplay display)
        {
            var mode = display.Modes.FirstOrDefault(m => m.Width == display.Width && m.Height == display.Height &&
                Math.Abs(m.RefreshRate - display.RefreshRate) < .01);
            return DisplaySettingsHelper.BuildKScreenModeArgument(display.Name, mode, display.Width, display.Height, display.RefreshRate);
        }

        private async Task<DisplayChangeResult> RunDisplayChangeAsync(Func<Task<bool>> apply, Func<Task<bool>> restore, int buildVersion)
        {
            if (_displayChangeGuard.IsBusy) return DisplayChangeResult.Busy;
            var token = _displayLifetime.Token;
            DisplayChangeResult result;
            CategoryContent.IsEnabled = false;
            try
            {
                _pendingDisplayChange = _displayChangeGuard.RunAsync(apply, restore,
                confirmation => Dispatcher.UIThread.Post(() =>
                {
                    try { OpenDisplayConfirmation(confirmation); }
                    catch { confirmation.Revert(); }
                }), token);
                result = await _pendingDisplayChange;
            }
            finally { CategoryContent.IsEnabled = true; }
            if (result == DisplayChangeResult.Busy) return result;
            if (result == DisplayChangeResult.RestoreFailed)
                (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t restore the previous display settings. Check the connected display and try again."));
            else if (result == DisplayChangeResult.Failed)
                (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t apply the display setting. The previous settings were restored."));
            if (!token.IsCancellationRequested && buildVersion == _categoryBuildVersion)
                ShowCategory("Displays");
            return result;
        }

        private async void OnDisplayOwnerClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_allowDisplayOwnerClose || _pendingDisplayChange is not { IsCompleted: false } pending || sender is not Window owner) return;
            e.Cancel = true;
            if (_closingForDisplayRevert) return;
            _closingForDisplayRevert = true;
            _displayLifetime.Cancel();
            try { await pending; }
            finally
            {
                _allowDisplayOwnerClose = true;
                owner.Close();
            }
        }

        private async Task ApplyDisplayResolutionAsync(string outputName, string selectedResolution, int buildVersion)
        {
            if (!DisplaySettingsHelper.TryParseResolution(selectedResolution, out var width, out var height)) return;
            DetectedDisplay? previous = null;
            var usedXrandr = false;
            var runner = new SafeCommandRunner();
            var result = await RunDisplayChangeAsync(async () =>
            {
                previous = (await DisplaySettingsHelper.DetectAllDisplaysAsync()).FirstOrDefault(d => d.Name == outputName);
                if (previous is null) return false;
                var mode = DisplaySettingsHelper.FindBestModeForResolution(previous.Modes, width, height, previous.RefreshRate);
                var cmdResult = await runner.RunAsync("kscreen-doctor",
                    [DisplaySettingsHelper.BuildKScreenModeArgument(outputName, mode, width, height, previous.RefreshRate)], TimeSpan.FromSeconds(5));
                if (cmdResult.Succeeded) return true;
                // XRandR changes the real display only in an X11 session, never XWayland.
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) return false;
                usedXrandr = true;
                return (await runner.RunAsync("xrandr", ["--output", outputName, "--mode", $"{width}x{height}"], TimeSpan.FromSeconds(5))).Succeeded;
            }, async () =>
            {
                if (previous is null) return true;
                return usedXrandr
                    ? (await runner.RunAsync("xrandr", ["--output", outputName, "--mode", $"{previous.Width}x{previous.Height}", "--rate", previous.RefreshRate.ToString(global::System.Globalization.CultureInfo.InvariantCulture)], TimeSpan.FromSeconds(5))).Succeeded
                    : (await runner.RunAsync("kscreen-doctor", [ModeArgument(previous)], TimeSpan.FromSeconds(5))).Succeeded;
            }, buildVersion);

            if (result == DisplayChangeResult.Kept)
            {
                (Application.Current as App)?.SetPreference("display.resolution", selectedResolution);
            }
        }

        private async Task ApplyKScreenSettingAsync(string outputName, string settingName, Func<DetectedDisplay, string> argument, int buildVersion)
        {
            DetectedDisplay? previous = null;
            var runner = new SafeCommandRunner();
            // Use current compositor state rather than the snapshot used to build the page.
            var result = await RunDisplayChangeAsync(async () =>
            {
                previous = (await DisplaySettingsHelper.DetectAllDisplaysAsync()).FirstOrDefault(d => d.Name == outputName);
                if (previous is null) return false;
                return (await runner.RunAsync("kscreen-doctor", [argument(previous)], TimeSpan.FromSeconds(5))).Succeeded;
            }, async () => previous is null || (await runner.RunAsync("kscreen-doctor",
                [settingName == "display scale" ? $"output.{outputName}.scale.{previous.Scale.ToString(global::System.Globalization.CultureInfo.InvariantCulture)}" : ModeArgument(previous)], TimeSpan.FromSeconds(5))).Succeeded,
                buildVersion);

            if (result == DisplayChangeResult.Kept)
            {
                var app = Application.Current as App;
                var updated = (await DisplaySettingsHelper.DetectAllDisplaysAsync()).FirstOrDefault(d => d.Name == outputName);
                if (updated is not null)
                {
                    if (settingName == "display scale")
                    {
                        var scaleStr = $"{(int)Math.Round(updated.Scale * 100)}%";
                        app?.SetPreference("display.scale", scaleStr);
                    }
                    else if (settingName == "refresh rate")
                    {
                        var rateStr = $"{DisplaySettingsHelper.FormatRefreshRate(updated.RefreshRate)} Hz";
                        app?.SetPreference("display.refresh-rate", rateStr);
                    }
                }
            }
        }

        private async Task ApplyHdrSettingAsync(string outputName, bool enabled, int buildVersion)
        {
            DetectedDisplay? previous = null;
            var runner = new SafeCommandRunner();
            await RunDisplayChangeAsync(async () =>
            {
                previous = (await DisplaySettingsHelper.DetectAllDisplaysAsync()).FirstOrDefault(d => d.Name == outputName);
                if (previous is null || !previous.HdrAvailable) return false;
                var state = enabled ? "enable" : "disable";
                var result = await runner.RunAsync("kscreen-doctor",
                    [$"output.{outputName}.hdr.{state}"], TimeSpan.FromSeconds(3));
                if (!result.Succeeded) return false;
                var applied = (await DisplaySettingsHelper.DetectAllDisplaysAsync()).FirstOrDefault(d => d.Name == outputName);
                return applied is not null && applied.HdrEnabled == enabled &&
                    applied.WideColorGamutEnabled == previous.WideColorGamutEnabled;
            }, async () =>
            {
                if (previous is null) return true;
                var hdr = previous.HdrEnabled ? "enable" : "disable";
                var restored = await runner.RunAsync("kscreen-doctor",
                    [$"output.{outputName}.hdr.{hdr}"], TimeSpan.FromSeconds(3));
                if (!restored.Succeeded) return false;
                var state = (await DisplaySettingsHelper.DetectAllDisplaysAsync()).FirstOrDefault(d => d.Name == outputName);
                return state is not null && state.HdrEnabled == previous.HdrEnabled &&
                    state.WideColorGamutEnabled == previous.WideColorGamutEnabled;
            }, buildVersion);
        }

        internal void OpenDisplayConfirmation(DisplayChangeConfirmation confirmation)
        {
            if (confirmation.IsComplete || TopLevel.GetTopLevel(this) is not Window owner || !owner.IsVisible)
            { confirmation.Revert(); return; }
            var keep = Loc.Bind(new Button { Content = "Keep changes", Classes = { "Primary" }, MinWidth = 112 }, Button.ContentProperty, "Keep changes");
            var revert = Loc.Bind(new Button { Content = "Revert", Classes = { "Subtle" }, MinWidth = 88 }, Button.ContentProperty, "Revert");
            var countdown = new TextBlock { Name = "DisplayChangeCountdown", TextWrapping = TextWrapping.Wrap, Foreground = TryBrush("ContentSecondaryBrush") };
            var progress = new ProgressBar { Minimum = 0, Maximum = 15, Value = 15, Height = 4 };
            var dialog = new Window
            {
                Title = Loc.Get("Keep display changes?"), Width = 420, SizeToContent = SizeToContent.Height,
                CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = TryBrush("SurfaceBaseSolidBrush"),
                Content = new StackPanel { Name = "DisplayChangeConfirmationPanel", Margin = new Thickness(24), Spacing = 18,
                    Children =
                    {
                        DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock { Text = Loc.Get("Keep display changes?"), FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, "Title"),
                        countdown, progress,
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { revert, keep } }
                    }
                }
            };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            void Update()
            {
                var seconds = confirmation.SecondsRemaining;
                countdown.Text = Loc.Format("Reverting to the previous settings in {0} seconds.", seconds);
                progress.Value = seconds;
                if (confirmation.IsComplete) dialog.Close();
            }
            keep.Click += (_, _) => { confirmation.Keep(); dialog.Close(); };
            revert.Click += (_, _) => { confirmation.Revert(); dialog.Close(); };
            dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { confirmation.Revert(); dialog.Close(); e.Handled = true; } };
            dialog.Closed += (_, _) => { timer.Stop(); confirmation.Revert(); };
            timer.Tick += (_, _) => Update();
            try { Update(); _ = dialog.ShowDialog(owner); timer.Start(); revert.Focus(); }
            catch { timer.Stop(); confirmation.Revert(); }
        }

        private void BuildSound(int buildVersion)
        {
            var checking = Secondary(Loc.Get("Checking sound devices…"));
            CategoryContent.Children.Add(checking);
            _ = BuildSoundDevicesAsync(buildVersion, checking);
        }

        private async Task BuildSoundDevicesAsync(int buildVersion, TextBlock checking)
        {
            var audio = new LinuxAudioService(new SafeCommandRunner());
            IReadOnlyList<AudioDeviceInfo> outputDevices;
            IReadOnlyList<AudioDeviceInfo> inputDevices;
            IReadOnlyList<AudioAppStreamInfo> streams;
            try
            {
                outputDevices = await audio.GetAudioDevicesAsync();
            }
            catch
            {
                outputDevices = Array.Empty<AudioDeviceInfo>();
            }

            try
            {
                inputDevices = await audio.GetAudioInputDevicesAsync();
            }
            catch
            {
                inputDevices = Array.Empty<AudioDeviceInfo>();
            }

            try
            {
                streams = await audio.GetAudioAppStreamsAsync();
            }
            catch
            {
                streams = Array.Empty<AudioAppStreamInfo>();
            }

            if (buildVersion != _categoryBuildVersion) return;
            CategoryContent.Children.Remove(checking);

            var outputControls = new List<Control>();

            if (outputDevices.Count > 0)
            {
                var deviceNames = outputDevices.Select(d => d.Name).ToArray();
                var defaultDevice = outputDevices.FirstOrDefault(d => d.IsDefault)?.Name ?? deviceNames[0];
                var devicePicker = Picker("Speakers and headphones", "sound.output-device", defaultDevice, async selectedName =>
                {
                    var target = outputDevices.FirstOrDefault(d => string.Equals(d.Name, selectedName, StringComparison.Ordinal));
                    if (target is not null && !await audio.SetDefaultAudioDeviceAsync(target.Id))
                        (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t switch speakers. Check that the device is connected and try again."));
                    ShowCategory("Sound");
                }, deviceNames);
                outputControls.Add(devicePicker);
                outputControls.Add(VolumeRow("Output volume", false));

                var testSoundButton = Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
                {
                    Content = "Play test sound",
                    Classes = { "Subtle", "Small" },
                    Padding = new Thickness(14, 0),
                    VerticalAlignment = VerticalAlignment.Center
                }), Button.ContentProperty, "Play test sound");
                testSoundButton.Click += async (_, _) =>
                {
                    var result = await new SafeCommandRunner().RunAsync("canberra-gtk-play", ["--id=bell"], TimeSpan.FromSeconds(2));
                    if (!result.Succeeded)
                        (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t play the test sound. Try again or check your speakers."));
                };
                outputControls.Add(PreferenceRow(Loc.Get("Sound test"), Loc.Get("Play a sample chime through active speakers."), testSoundButton));
            }
            else
            {
                outputControls.Add(InfoRow(Loc.Get("Audio output"), Loc.Get("No speakers or headphones are connected.")));
            }

            AddSection(Loc.Get("OUTPUT"), Loc.Get("Choose where sound plays and adjust its volume."), outputControls.ToArray());

            var inputControls = new List<Control>();
            if (inputDevices.Count > 0)
            {
                var inputNames = inputDevices.Select(d => d.Name).ToArray();
                var defaultInput = inputDevices.FirstOrDefault(d => d.IsDefault)?.Name ?? inputNames[0];
                var inputPicker = Picker("Microphone", "sound.input-device", defaultInput, async selectedName =>
                {
                    var target = inputDevices.FirstOrDefault(d => string.Equals(d.Name, selectedName, StringComparison.Ordinal));
                    if (target is not null && !await audio.SetDefaultAudioInputDeviceAsync(target.Id))
                        (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t switch microphones. Check that the device is connected and try again."));
                    ShowCategory("Sound");
                }, inputNames);
                inputControls.Add(inputPicker);
                inputControls.Add(VolumeRow("Input volume", true));
                var inputMuted = await audio.GetInputMutedAsync();
                inputControls.Add(Toggle("Mute microphone", "sound.input-muted", inputMuted, value => { _ = SetMicrophoneMuted(value); }));
            }
            else
            {
                inputControls.Add(InfoRow(Loc.Get("Audio input"), Loc.Get("No microphone is connected.")));
            }
            AddSection(Loc.Get("INPUT"), Loc.Get("Choose a microphone and adjust its volume."), inputControls.ToArray());

            if (streams.Count > 0)
            {
                var streamRows = streams.Select(s => AppStreamVolumeRow(s, audio)).ToArray();
                AddSection(Loc.Get("VOLUME MIXER"), Loc.Get("Control volume independently for each running application."), streamRows);
            }

        }

        private Border AppStreamVolumeRow(AudioAppStreamInfo stream, LinuxAudioService audio)
        {
            var slider = new QuickSettingsSlider
            {
                Minimum = 0,
                Maximum = 100,
                Width = 200,
                Value = stream.Volume,
                Icon = "speaker-high",
                VerticalAlignment = VerticalAlignment.Center
            };
            slider.ValueChanged += (_, _) =>
            {
                var vol = Math.Round(slider.Value);
                _ = audio.SetAppStreamVolumeAsync(stream.Id, vol);
            };
            return PreferenceRow(stream.Name, Loc.Format("Application volume: {0}%", (int)stream.Volume), slider);
        }

        private void BuildNetwork(int buildVersion)
        {
            var checking = Secondary(Loc.Get("Checking network connectivity…"));
            CategoryContent.Children.Add(checking);
            _ = BuildNetworkAvailabilityAsync(buildVersion, checking);
        }

        private async Task BuildNetworkAvailabilityAsync(int buildVersion, TextBlock? checking)
        {
            var statusService = new LinuxNetworkStatusService(new SafeCommandRunner());
            var wirelessService = _settingsWirelessService;

            NetworkStatus networkStatus;
            WirelessRadioStatus radio;
            WirelessNetworkStatus? wirelessNetworks = null;
            try
            {
                networkStatus = await statusService.GetStatusAsync();
            }
            catch
            {
                networkStatus = new NetworkStatus(false, "Offline", "No active connection found.");
            }

            try
            {
                radio = await wirelessService.GetRadioStatusAsync();
                if (radio.IsAvailable && radio.IsEnabled)
                    wirelessNetworks = await wirelessService.GetNetworksAsync();
            }
            catch
            {
                radio = new WirelessRadioStatus(false, false, "Wi-Fi isn’t available on this device.");
            }

            if (buildVersion != _categoryBuildVersion) return;
            if (checking is not null) CategoryContent.Children.Remove(checking);
            while (CategoryContent.Children.Count > 1)
                CategoryContent.Children.RemoveAt(CategoryContent.Children.Count - 1);

            var connected = networkStatus.IsConnected && networkStatus.ConnectionKind == NetworkConnectionKind.WiFi;
            if (radio.IsAvailable)
            {
                AddSection(Loc.Get("WI-FI"), Loc.Get("Connect to a wireless network."),
                    Toggle("Wi-Fi", "network.wifi", radio.IsEnabled, value => { _ = SetWifi(value); }),
                    InfoRow(Loc.Get("Current connection"), connected ? networkStatus.DisplayName : Loc.Get("Not connected")));

                if (radio.IsEnabled && wirelessNetworks is { IsAvailable: true })
                {
                    var knownRows = new List<Control>();
                    foreach (var network in wirelessNetworks.Networks.Where(network => network.IsKnown).Take(12))
                    {
                        var action = DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
                        {
                            Content = Loc.Get(network.IsActive ? "Disconnect" : "Connect"),
                            Classes = { network.IsActive ? "Subtle" : "Ghost", "Small" },
                            MinWidth = 92,
                            IsEnabled = network.IsActive || network.IsAvailable
                        });
                        var selectedNetwork = network;
                        action.Click += async (_, _) =>
                        {
                            action.IsEnabled = false;
                            action.Content = Loc.Get(selectedNetwork.IsActive ? "Disconnecting…" : "Connecting…");
                            var success = selectedNetwork.IsActive
                                ? await wirelessService.DisconnectFromNetworkAsync()
                                : await ConnectToWirelessNetworkAsync(wirelessService, selectedNetwork);
                            if (!success)
                                (Application.Current as App)?.Feedback.Show(selectedNetwork.IsActive
                                    ? Loc.Format("Couldn’t disconnect from {0}.", selectedNetwork.Ssid)
                                    : Loc.Format("Couldn’t connect to {0}. Check the password and signal, then try again.", selectedNetwork.Ssid));
                            ShowCategory("Network");
                        };
                        var forget = Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
                        {
                            Content = "Forget", Classes = { "Ghost", "Small" }, Padding = new Thickness(10, 0)
                        }), Button.ContentProperty, "Forget");
                        forget.Click += async (_, _) =>
                        {
                            action.IsEnabled = forget.IsEnabled = false;
                            forget.Content = Loc.Get("Forgetting…");
                            if (!await wirelessService.ForgetNetworkAsync(selectedNetwork.Ssid))
                                (Application.Current as App)?.Feedback.Show(Loc.Format("Couldn’t forget {0}.", selectedNetwork.Ssid));
                            ShowCategory("Network");
                        };
                        var detail = selectedNetwork.IsActive ? Loc.Get("Connected")
                            : selectedNetwork.IsAvailable ? Loc.Format("Available · {0}% signal", selectedNetwork.SignalPercent)
                            : Loc.Get("Not in range");
                        knownRows.Add(PreferenceRow(network.Ssid, detail, new StackPanel
                        {
                            Orientation = Orientation.Horizontal, Spacing = 6,
                            Children = { action, forget }
                        }, "wifi-02"));
                    }
                    if (knownRows.Count == 0)
                        knownRows.Add(InfoRow(Loc.Get("Known networks"), Loc.Get("Networks you connect to will appear here.")));
                    AddSection(Loc.Get("KNOWN NETWORKS"), Loc.Get("Saved networks connect without asking for a password again."), knownRows.ToArray());

                    var otherRows = new List<Control>();
                    foreach (var network in wirelessNetworks.Networks.Where(network => !network.IsKnown && network.IsAvailable).Take(12))
                    {
                        var selectedNetwork = network;
                        var action = DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
                        {
                            Content = Loc.Get("Connect"), Classes = { "Subtle", "Small" }, MinWidth = 92
                        });
                        action.Click += async (_, _) =>
                        {
                            action.IsEnabled = false;
                            action.Content = Loc.Get("Connecting…");
                            if (!await ConnectToWirelessNetworkAsync(wirelessService, selectedNetwork))
                                (Application.Current as App)?.Feedback.Show(Loc.Format("Couldn’t connect to {0}. Check the password and signal, then try again.", selectedNetwork.Ssid));
                            ShowCategory("Network");
                        };
                        var security = IsOpenWirelessNetwork(network.Security) ? Loc.Get("Open network") : network.Security;
                        otherRows.Add(PreferenceRow(network.Ssid,
                            Loc.Format("{0} · {1}% signal", security, network.SignalPercent), action, "wifi-02"));
                    }
                    if (otherRows.Count == 0)
                        otherRows.Add(InfoRow(Loc.Get("Searching…"), Loc.Get("Looking for other Wi-Fi networks nearby.")));
                    AddSection(Loc.Get("OTHER NETWORKS"), Loc.Get("Nearby networks refresh automatically."), otherRows.ToArray());
                }
            }
            else
            {
                AddSection(Loc.Get("WI-FI"), Loc.Get("Wi-Fi adapter status."), InfoRow(Loc.Get("Wi-Fi status"), radio.Detail));
            }

        }

        private async void BuildEthernet(int buildVersion)
        {
            var checking = Secondary(Loc.Get("Checking network connectivity…"));
            CategoryContent.Children.Add(checking);
            NetworkStatus status;
            try { status = await new LinuxNetworkStatusService(new SafeCommandRunner()).GetStatusAsync(); }
            catch { status = new NetworkStatus(false, "Offline", "", IsAvailable: false); }
            if (buildVersion != _categoryBuildVersion) return;
            CategoryContent.Children.Remove(checking);
            var connected = status.IsConnected && status.ConnectionKind == NetworkConnectionKind.Ethernet;
            AddSection(Loc.Get("ETHERNET"), Loc.Get("Manage your wired connection."),
                InfoRow(Loc.Get("Ethernet"), connected ? status.DisplayName : Loc.Get("Not connected")),
                InfoRow(Loc.Get("Internet access"), connected && status.Connectivity == NetworkConnectivity.Full
                    ? Loc.Get("Internet available") : Loc.Get("No internet access")));
        }

        private async Task<bool> ConnectToWirelessNetworkAsync(LinuxWirelessNetworkService service, WirelessNetworkInfo network)
        {
            if (IsOpenWirelessNetwork(network.Security))
                return await service.ConnectToNetworkAsync(network.Ssid);

            // Reuse NetworkManager's saved profile before asking for a password.
            // New networks fall through to the credential dialog unchanged.
            if (await service.ConnectToNetworkAsync(network.Ssid))
                return true;

            var password = await PromptForWifiPasswordAsync(network.Ssid);
            return password is not null && await service.ConnectToNetworkAsync(network.Ssid, password);
        }

        private static bool IsOpenWirelessNetwork(string security) =>
            string.IsNullOrWhiteSpace(security) || security is "--" or "Open";

        private async Task<string?> PromptForWifiPasswordAsync(string ssid)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return null;

            var password = Loc.Bind(new TextBox
            {
                PlaceholderText = "Network password",
                PasswordChar = '●',
                MinWidth = 300
            }, TextBox.PlaceholderTextProperty, "Network password");
            var connect = Loc.Bind(new Button { Content = "Connect", Classes = { "Primary" }, IsDefault = true }, Button.ContentProperty, "Connect");
            var cancel = Loc.Bind(new Button { Content = "Cancel", Classes = { "Subtle" }, IsCancel = true }, Button.ContentProperty, "Cancel");
            var dialog = new Window
            {
                Title = Loc.Format("Connect to {0}", ssid),
                Width = 420,
                Height = 220,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = TryBrush("SurfaceBaseSolidBrush"),
                Content = new StackPanel
                {
                    Margin = new Thickness(24),
                    Spacing = 14,
                    Children =
                    {
                        DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock { Text = ssid, FontSize = 20, FontWeight = FontWeight.SemiBold }, "Title"),
                        Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock { Text = "Enter the password provided by the network owner.", FontSize = 13, Foreground = TryBrush("ContentSecondaryBrush") }, "BodySmall"), TextBlock.TextProperty, "Enter the password provided by the network owner."),
                        password,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { cancel, connect }
                        }
                    }
                }
            };
            connect.Click += (_, _) => dialog.Close(password.Text);
            cancel.Click += (_, _) => dialog.Close(null);
            dialog.Opened += (_, _) => password.Focus();
            return await dialog.ShowDialog<string?>(owner);
        }

        private void RefreshBluetooth()
        {
            var buildVersion = ++_categoryBuildVersion;
            BuildBluetooth(buildVersion);
        }

        private void BuildBluetooth(int buildVersion)
        {
            TextBlock? checking = null;
            if (CategoryContent.Children.Count <= 1)
            {
                checking = Secondary(Loc.Get("Checking Bluetooth availability…"));
                CategoryContent.Children.Add(checking);
            }
            _ = BuildBluetoothAvailabilityAsync(buildVersion, checking);
        }

        private async Task BuildBluetoothAvailabilityAsync(int buildVersion, TextBlock? checking)
        {
            var bluetoothService = _settingsBluetoothService;
            BluetoothStatus adapter;
            try
            {
                adapter = await bluetoothService.GetStatusAsync();
            }
            catch
            {
                adapter = new BluetoothStatus(false, false, false, Array.Empty<BluetoothDevice>(), "Bluetooth isn’t available on this device.");
            }

            if (buildVersion != _categoryBuildVersion) return;
            if (checking is not null) CategoryContent.Children.Remove(checking);

            // Keep the page title/description header (index 0) and remove all existing category sections
            while (CategoryContent.Children.Count > 1)
            {
                CategoryContent.Children.RemoveAt(CategoryContent.Children.Count - 1);
            }

            if (!adapter.IsAvailable)
            {
                AddUnavailableMessage(adapter.Detail);
                return;
            }

            AddSection(Loc.Get("BLUETOOTH"), Loc.Get("Bluetooth changes are applied through the system adapter."),
                Toggle("Bluetooth", "bluetooth.enabled", adapter.IsEnabled, value => { _ = SetBluetooth(value); }));
            if (!adapter.IsEnabled) StopAutomaticBluetoothDiscovery();

            var pairedDevices = adapter.Devices.Where(d => d.IsPaired)
                .OrderByDescending(d => d.IsConnected)
                .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var pairedRows = new List<Control>();
            foreach (var device in pairedDevices)
            {
                var statusText = device.IsConnected ? Loc.Get("Connected") : Loc.Get("Paired");
                var actionButton = DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
                {
                    Content = Loc.Get(device.IsConnected ? "Disconnect" : "Connect"),
                    Classes = { "Subtle", "Small" },
                    Padding = new Thickness(12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsEnabled = adapter.IsEnabled
                });
                var dev = device;
                actionButton.Click += async (_, _) =>
                {
                    actionButton.IsEnabled = false;
                    actionButton.Content = Loc.Get(dev.IsConnected ? "Disconnecting…" : "Connecting…");
                    var success = await bluetoothService.SetConnectedAsync(dev.Address, !dev.IsConnected);
                    if (!success)
                    {
                        (Application.Current as App)?.Feedback.Show(dev.IsConnected
                            ? Loc.Format("Couldn’t disconnect {0}.", dev.Name)
                            : Loc.Format("Couldn’t connect to {0}.", dev.Name));
                    }
                    RefreshBluetooth();
                };

                var forgetButton = Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
                {
                    Content = "Forget",
                    Classes = { "Ghost", "Small" },
                    Padding = new Thickness(10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                }), Button.ContentProperty, "Forget");
                forgetButton.Click += async (_, _) =>
                {
                    forgetButton.IsEnabled = false;
                    actionButton.IsEnabled = false;
                    forgetButton.Content = Loc.Get("Forgetting…");
                    if (!await bluetoothService.ForgetDeviceAsync(dev.Address))
                        (Application.Current as App)?.Feedback.Show(Loc.Format("Couldn’t forget {0}.", dev.Name));
                    RefreshBluetooth();
                };

                pairedRows.Add(PreferenceRow(device.Name, statusText, new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { actionButton, forgetButton }
                }, BluetoothDevicePresentation.IconFor(device)));
            }
            if (pairedRows.Count == 0)
                pairedRows.Add(InfoRow(Loc.Get("Paired accessories"), Loc.Get("No paired devices found.")));

            AddSection(Loc.Get("MY DEVICES"), Loc.Get("Devices you have paired before remain here, even when Bluetooth is off."), pairedRows.ToArray());

            if (adapter.IsEnabled)
            {
                EnsureAutomaticBluetoothDiscovery();

                var availableDevices = adapter.Devices
                    .Where(d => !d.IsPaired && !LinuxBluetoothService.IsAnonymousName(d.Name))
                    .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                var availableRows = new List<Control>();
                if (availableDevices.Count > 0)
                {
                    foreach (var device in availableDevices)
                    {
                        var pairButton = DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
                        {
                            Content = Loc.Get("Pair"),
                            Classes = { "Subtle", "Small" },
                            Padding = new Thickness(14, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        var dev = device;
                        pairButton.Click += async (_, _) =>
                        {
                            pairButton.IsEnabled = false;
                            pairButton.Content = Loc.Get("Pairing…");
                            var paired = await bluetoothService.PairDeviceAsync(dev.Address);
                            if (paired)
                            {
                                (Application.Current as App)?.Feedback.Show(Loc.Format("Connected to {0}.", dev.Name));
                            }
                            else
                            {
                                (Application.Current as App)?.Feedback.Show(Loc.Format("Couldn’t pair with {0}. Ensure it is in pairing mode and try again.", dev.Name));
                            }
                            RefreshBluetooth();
                        };

                        availableRows.Add(PreferenceRow(device.Name, Loc.Get("Ready to pair"), pairButton, BluetoothDevicePresentation.IconFor(device)));
                    }
                }
                else availableRows.Add(InfoRow(Loc.Get("Searching…"), Loc.Get("Looking for nearby Bluetooth devices in pairing mode…")));

                AddSection(Loc.Get("NEARBY DEVICES"), Loc.Get("Nearby devices appear automatically while this page is open."), availableRows.ToArray());
            }
        }

        private void EnsureAutomaticBluetoothDiscovery()
        {
            if (_bluetoothDiscoveryLifetime is not null || !string.Equals(_activeCategory, "Bluetooth", StringComparison.Ordinal)) return;
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_displayLifetime.Token);
            _bluetoothDiscoveryLifetime = lifetime;
            _ = RunAutomaticBluetoothDiscoveryAsync(lifetime);
        }

        private async Task RunAutomaticBluetoothDiscoveryAsync(CancellationTokenSource lifetime)
        {
            var cancellationToken = lifetime.Token;
            _isScanningBluetooth = true;
            try
            {
                if (!await _settingsBluetoothService.StartDiscoveryAsync(cancellationToken))
                {
                    (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t search for nearby Bluetooth devices."));
                    return;
                }
                while (!cancellationToken.IsCancellationRequested
                       && string.Equals(_activeCategory, "Bluetooth", StringComparison.Ordinal))
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                    if (!cancellationToken.IsCancellationRequested) RefreshBluetooth();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            finally
            {
                _isScanningBluetooth = false;
                try { await _settingsBluetoothService.StopDiscoveryAsync(CancellationToken.None); } catch { }
                if (ReferenceEquals(_bluetoothDiscoveryLifetime, lifetime))
                    _bluetoothDiscoveryLifetime = null;
                lifetime.Dispose();
            }
        }

        private void StopAutomaticBluetoothDiscovery()
        {
            _bluetoothDiscoveryLifetime?.Cancel();
            _bluetoothDiscoveryLifetime = null;
            _isScanningBluetooth = false;
        }

        private void BuildNotifications()
        {
            AddSection(Loc.Get("FOCUS"), Loc.Get("Notifications still appear in Notification Centre while focus is on."),
                Toggle("Do not disturb", "notifications.dnd", false, value => { if (Application.Current is App app) app.SetDoNotDisturb(value); }));
            var shell = Application.Current as App;
            AddSection(Loc.Get("GAMING"), Loc.Get("Gaming Mode boosts performance with GameMode and quiets popups while you play."),
                Toggle("Gaming Mode", "gaming.mode", false, value => { if (shell is not null) shell.SessionState.GamingMode = value; }),
                Toggle("Turn on automatically when a game starts", GamingModeAutomation.AutomaticPreferenceKey,
                    shell?.GetPreference(GamingModeAutomation.AutomaticPreferenceKey, "true") != "false",
                    value => shell?.SetPreference(GamingModeAutomation.AutomaticPreferenceKey, value ? "true" : "false")));
        }

        private Button SettingsAction(string title, string description, string icon, Action activate, string? value = null, bool literalTitle = false, bool literalDescription = false)
        {
            var glyph = new DaisyIcon { Icon = icon, Size = 20, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            glyph.Bind(DaisyIcon.ForegroundProperty, this.GetResourceObservable("ContentSecondaryBrush"));
            var label = new TextBlock { Classes = { "SettingsLabel" }, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
            if (literalTitle) label.Text = title;
            else Loc.Bind(label, TextBlock.TextProperty, title);
            var detail = Secondary(literalDescription ? description : Loc.Get(description));
            detail.TextWrapping = TextWrapping.NoWrap;
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { label, detail } };
            var size = Secondary(value ?? "");
            size.VerticalAlignment = VerticalAlignment.Center;
            size.TextWrapping = TextWrapping.NoWrap;
            size.Margin = new Thickness(16, 0);
            size.IsVisible = value is not null;
            var arrow = new DaisyIcon { Icon = "caret-right", Size = 16, VerticalAlignment = VerticalAlignment.Center };
            arrow.Bind(DaisyIcon.ForegroundProperty, this.GetResourceObservable("ContentSecondaryBrush"));
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Children = { glyph, labels, size, arrow } };
            Grid.SetColumn(labels, 1); Grid.SetColumn(size, 2); Grid.SetColumn(arrow, 3);
            var button = new Button { Content = content, Classes = { "Subtle", "SettingsGroupAction" } };
            button.Click += (_, _) => activate();
            return button;
        }

        private void BuildApps(int buildVersion)
        {
            var installedApps = SettingsAction("Installed apps", "View, repair, and uninstall applications.", "squares-four", () => SelectCategory("InstalledApps"));
            AddSection(Loc.Get("APP MANAGEMENT"), Loc.Get("See applications available from the launcher."), installedApps);

            var checking = Secondary(Loc.Get("Checking default applications…"));
            CategoryContent.Children.Add(checking);
            _ = BuildAppsAsync(buildVersion, checking);
        }

        private async Task BuildAppsAsync(int buildVersion, TextBlock checking)
        {
            var defaultAppsService = new DaisyOS.System.Processes.LinuxDefaultAppsService();
            var status = await defaultAppsService.GetDefaultAppsAsync();

            if (buildVersion != _categoryBuildVersion) return;
            CategoryContent.Children.Remove(checking);

            var defaultRows = new List<Control>();

            // 1. Web Browser
            if (status.AvailableBrowsers.Count > 0)
            {
                var browserNames = status.AvailableBrowsers.Select(b => b.DisplayName).Distinct().ToArray();
                var currentBrowser = status.AvailableBrowsers.FirstOrDefault(b => string.Equals(b.DesktopId, status.CurrentBrowserDesktopId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                    ?? browserNames[0];
                defaultRows.Add(Picker("Web browser", "apps.default-browser", currentBrowser, async chosenName =>
                {
                    var app = status.AvailableBrowsers.FirstOrDefault(b => b.DisplayName == chosenName);
                    if (app is not null && !await defaultAppsService.SetDefaultBrowserAsync(app.DesktopId))
                        (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t change the default browser."));
                    ShowCategory("Apps");
                }, browserNames));
            }
            else
            {
                defaultRows.Add(InfoRow(Loc.Get("Web browser"), Loc.Get("No installed browsers detected")));
            }

            // 2. File Manager
            if (status.AvailableFileManagers.Count > 0)
            {
                var fmNames = status.AvailableFileManagers.Select(f => f.DisplayName).Distinct().ToArray();
                var currentFm = status.AvailableFileManagers.FirstOrDefault(f => string.Equals(f.DesktopId, status.CurrentFileManagerDesktopId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                    ?? fmNames[0];
                defaultRows.Add(Picker("File manager", "apps.default-filemanager", currentFm, async chosenName =>
                {
                    var app = status.AvailableFileManagers.FirstOrDefault(f => f.DisplayName == chosenName);
                    if (app is not null && !await defaultAppsService.SetDefaultFileManagerAsync(app.DesktopId))
                        (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t change the default file manager."));
                    ShowCategory("Apps");
                }, fmNames));
            }

            // 3. Text Editor
            if (status.AvailableTextEditors.Count > 0)
            {
                var editorNames = status.AvailableTextEditors.Select(e => e.DisplayName).Distinct().ToArray();
                var currentEd = status.AvailableTextEditors.FirstOrDefault(e => string.Equals(e.DesktopId, status.CurrentTextEditorDesktopId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                    ?? editorNames[0];
                defaultRows.Add(Picker("Text editor", "apps.default-editor", currentEd, async chosenName =>
                {
                    var app = status.AvailableTextEditors.FirstOrDefault(e => e.DisplayName == chosenName);
                    if (app is not null && !await defaultAppsService.SetDefaultTextEditorAsync(app.DesktopId))
                        (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t change the default text editor."));
                    ShowCategory("Apps");
                }, editorNames));
            }

            AddSection(Loc.Get("DEFAULT APPLICATIONS"), Loc.Get("The system uses these choices to open web links and documents."), defaultRows.ToArray());

        }

        private void BuildInstalledApplicationsSection(int buildVersion)
        {
            var skeleton = new SkeletonLoader();
            CategoryContent.Children.Add(skeleton);
            _ = LoadInstalledApplicationsAsync(buildVersion, skeleton);
        }

        private async Task LoadInstalledApplicationsAsync(int buildVersion, SkeletonLoader skeleton)
        {
            var service = new InstalledApplicationService();
            IReadOnlyList<AppEntry> apps;
            try { apps = await Task.Run(service.GetInstalledApplications); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (buildVersion != _categoryBuildVersion) return;
                CategoryContent.Children.Remove(skeleton);
                CategoryContent.Children.Add(Secondary(Loc.Get("Installed applications could not be read. Try again.")));
                return;
            }
            if (buildVersion != _categoryBuildVersion) return;
            CategoryContent.Children.Remove(skeleton);
            var search = new SearchBar
            {
                PlaceholderText = Loc.Get("Search installed apps")
            };
            var rows = new List<(AppEntry App, Border Row)>();
            search.PropertyChanged += (_, args) =>
            {
                if (args.Property != SearchBar.TextProperty) return;
                var query = search.Text?.Trim() ?? string.Empty;
                foreach (var (app, row) in rows)
                {
                    row.IsVisible = query.Length == 0
                        || app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                        || app.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                }
            };
            CategoryContent.Children.Add(search);
            CategoryContent.Children.Add(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
            {
                Text = Loc.Format("{0} apps installed", apps.Count),
                FontSize = 12,
                Foreground = TryBrush("ContentSecondaryBrush"),
                Margin = new Thickness(0, -10, 0, -8)
            }, "Caption"));
            var rowsHost = new StackPanel { Spacing = 8 };
            CategoryContent.Children.Add(rowsHost);
            foreach (var app in apps)
            {
                if (buildVersion != _categoryBuildVersion) return;
                var entry = CreateInstalledApplicationRow(app, service, buildVersion);
                var query = search.Text?.Trim() ?? string.Empty;
                entry.Row.IsVisible = query.Length == 0 || app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || app.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                rows.Add(entry);
                rowsHost.Children.Add(entry.Row);
                // Let input and rendering run between batches of icon decoding and layout.
                if (rows.Count % 8 == 0) await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            _ = GuiMotion.RevealAsync(rowsHost, 4);
        }

        private (AppEntry App, Border Row) CreateInstalledApplicationRow(
            AppEntry app,
            InstalledApplicationService service,
            int buildVersion)
        {
            var more = new Button
            {
                Width = 34,
                Height = 34,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = TryBrush("ContentSecondaryBrush"),
                Content = new DaisyIcon { Icon = UIIcons.MoreVertical, Size = 18, Foreground = TryBrush("ContentSecondaryBrush") }
            };
            more.Classes.Add("Ghost");
            more.Classes.Add("IconButton");
            ((DaisyIcon)more.Content!).Bind(DaisyIcon.ForegroundProperty, this.GetResourceObservable("ContentSecondaryBrush"));
            ToolTip.SetTip(more, Loc.Get("Application options"));
            Avalonia.Automation.AutomationProperties.SetName(more, Loc.Format("Options for {0}", app.Name));

            var menu = new ContextMenu();
            var modify = new MenuItem { Header = "Modify", IsEnabled = false };
            ToolTip.SetTip(modify, Loc.Get("This package does not expose optional components."));
            Loc.Bind(modify, MenuItem.HeaderProperty, "Modify");
            modify.Icon = new DaisyIcon { Icon = "sliders-horizontal", Classes = { "MenuIcon" } };
            var repair = new MenuItem { Header = "Repair" };
            Loc.Bind(repair, MenuItem.HeaderProperty, "Repair");
            repair.Icon = new DaisyIcon { Icon = "wrench", Classes = { "MenuIcon" } };
            var uninstall = new MenuItem { Header = "Uninstall", Classes = { "Danger" } };
            Loc.Bind(uninstall, MenuItem.HeaderProperty, "Uninstall");
            uninstall.Icon = new DaisyIcon { Icon = "trash", Classes = { "MenuIcon" } };
            menu.Items.Add(modify);
            menu.Items.Add(repair);
            menu.Items.Add(new Separator());
            menu.Items.Add(uninstall);
            more.ContextMenu = menu;
            more.Click += (_, _) => menu.Open(more);

            repair.Click += async (_, _) =>
            {
                menu.Close();
                more.IsEnabled = false;
                var target = await service.ResolveAsync(app);
                if (!target.CanManage)
                {
                    more.IsEnabled = true;
                    (Application.Current as App)?.Feedback.Show(target.UnavailableReason);
                    return;
                }
                var repaired = await service.RepairAsync(target);
                more.IsEnabled = true;
                (Application.Current as App)?.Feedback.Show(repaired ? Loc.Format("{0} was repaired.", app.Name) : Loc.Format("Couldn’t repair {0}.", app.Name));
            };
            uninstall.Click += async (_, _) =>
            {
                menu.Close();
                var target = await service.ResolveAsync(app);
                if (!target.CanManage)
                {
                    (Application.Current as App)?.Feedback.Show(target.UnavailableReason);
                    return;
                }
                if (!await ConfirmApplicationRemovalAsync(app.Name)) return;
                more.IsEnabled = false;
                var removed = await service.UninstallAsync(target);
                (Application.Current as App)?.Feedback.Show(removed ? Loc.Format("{0} was uninstalled.", app.Name) : Loc.Format("Couldn’t uninstall {0}.", app.Name));
                if (removed && buildVersion == _categoryBuildVersion) ShowCategory("Apps");
                else more.IsEnabled = true;
            };

            var row = new Border
            {
                Padding = new Thickness(18, 12),
                MinHeight = 68,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = TryBrush("SurfaceRaisedSubtleBrush"),
                CornerRadius = new CornerRadius(12),
                Child = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    Children =
                    {
                        CreateInstalledApplicationIcon(app),
                        new StackPanel
                        {
                            Margin = new Thickness(14, 0, 16, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Spacing = 2,
                            Children =
                            {
                                new TextBlock { Text = app.Name, Classes = { "SettingsLabel" } },
                                DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock { Text = app.Description, FontSize = 12, Foreground = TryBrush("ContentSecondaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis }, "Caption")
                            }
                        },
                        more
                    }
                }
            };
            Grid.SetColumn(((Grid)row.Child).Children[1], 1);
            Grid.SetColumn(more, 2);
            return (app, row);
        }

        private static Control CreateInstalledApplicationIcon(AppEntry app)
        {
            var iconPath = DesktopItemLoader.ResolveApplicationIconPath([app.Icon, app.Id, app.Name]);
            if (iconPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                return new Avalonia.Svg.Skia.Svg(new Uri("file:///"))
                {
                    Path = iconPath,
                    Width = 34,
                    Height = 34,
                    Stretch = Stretch.Uniform,
                    EnableCache = true,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                var icon = new Image
                {
                    Source = DesktopItemLoader.LoadBitmapSafe(iconPath),
                    Width = 34,
                    Height = 34,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                RenderOptions.SetBitmapInterpolationMode(icon, BitmapInterpolationMode.HighQuality);
                return icon;
            }

            return new DaisyIcon
            {
                Icon = "squares-four",
                Size = 20,
                Width = 34,
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private async Task<bool> ConfirmApplicationRemovalAsync(string appName)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return false;
            return await DaisyOS.Shell.Views.Dialogs.ConfirmationDialog.ShowApplicationRemovalAsync(owner, appName);
        }

        private async void BuildStorage()
        {
            using var busyCursor = DaisyOS.System.Processes.BusyCursor.Begin();
            var version = _categoryBuildVersion;
            var token = BeginStorageScan();
            var checking = Secondary(Loc.Get("Reading storage usage…"));
            CategoryContent.Children.Add(checking);
            try
            {
                var drives = await Task.Run(DaisyOS.System.Storage.LinuxStorageOverview.ReadDrives);
                if (version != _categoryBuildVersion) return;
                CategoryContent.Children.Remove(checking);
                if (drives.Count == 0) { AddUnavailableMessage(Loc.Get("Storage information couldn’t be read from this system.")); return; }
                var categoryHosts = new Dictionary<string, StackPanel>();
                foreach (var drive in drives)
                {
                    var content = new StackPanel { Spacing = 12 };
                    var title = new TextBlock
                    {
                        Text = drive.Path == "/" ? Loc.Get("Local disk") : drive.Path.TrimEnd('/') == "/home" ? Loc.Get("Home disk") : drive.Name,
                        FontSize = 16, FontWeight = FontWeight.SemiBold
                    };
                    title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentPrimaryBrush"));
                    content.Children.Add(title);
                    content.Children.Add(Secondary(Loc.Format("{0} used of {1}", StorageSize(drive.Used), StorageSize(drive.Capacity))));
                    var progress = new ProgressBar
                    {
                        Minimum = 0, Maximum = 100, Value = drive.Capacity > 0 ? 100.0 * drive.Used / drive.Capacity : 0,
                        Height = 8, CornerRadius = new CornerRadius(4)
                    };
                    progress.Bind(ProgressBar.ForegroundProperty, this.GetResourceObservable("ActionPrimaryBrush"));
                    content.Children.Add(progress);
                    content.Children.Add(Secondary(Loc.Format("{0} free", StorageSize(drive.Capacity - drive.Used))));
                    var categories = new StackPanel { Spacing = 8 };
                    categories.Children.Add(Secondary(Loc.Get("Calculating file sizes…")));
                    categoryHosts[drive.Path] = categories;
                    var summary = new Border
                    {
                        Padding = new Thickness(20), CornerRadius = new CornerRadius(12),
                        Background = TryBrush("SurfaceRaisedSubtleBrush"), Child = content
                    };
                    summary.Bind(Border.BackgroundProperty, this.GetResourceObservable("SurfaceRaisedSubtleBrush"));
                    CategoryContent.Children.Add(new StackPanel { Spacing = 12, Children = { summary, categories } });
                }
                var measured = await DaisyOS.System.Storage.LinuxStorageOverview.MeasureAsync(drives,
                    DaisyOS.Shell.Apps.Files.UserDirectoryPaths.Current, token);
                if (version != _categoryBuildVersion) return;
                foreach (var drive in measured)
                {
                    var host = categoryHosts[drive.Path];
                    host.Children.Clear();
                    foreach (var category in drive.Categories.OrderByDescending(c => c.Bytes))
                    {
                        var installed = category.Name == "Installed apps";
                        var button = SettingsAction(category.Name,
                            installed ? "View, repair, and uninstall applications." : "View and manage files.",
                            StorageCategoryIcon(category.Name),
                            () => ShowStorageCategory(category), StorageSize(category.Bytes));
                        button.Tag = category;
                        host.Children.Add(button);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (version == _categoryBuildVersion)
                {
                    CategoryContent.Children.Remove(checking);
                    AddUnavailableMessage(Loc.Get("Storage information couldn’t be read from this system."));
                }
            }
        }

        internal void ShowStorageCategory(DaisyOS.System.Storage.StorageUsageCategory category)
        {
            if (category.Name == "Installed apps") SelectCategory("InstalledApps");
            else
            {
                if (category.Name == "Trash")
                    category = category with { Paths = (category.Paths ?? []).Select(path => Directory.Exists(Path.Combine(path, "files")) ? Path.Combine(path, "files") : path).ToArray() };
                ShowStorageDetails(new(category));
            }
        }

        private CancellationToken BeginStorageScan()
        {
            _storageScan?.Cancel();
            _storageScan?.Dispose();
            _storageScan = CancellationTokenSource.CreateLinkedTokenSource(_displayLifetime.Token);
            return _storageScan.Token;
        }

        private static string StorageCategoryIcon(string name) => name switch
        {
            "Installed apps" => "squares-four", "System" => "gear-six", "Downloads" => "download-simple",
            "Documents" => "file-text", "Pictures" => "image", "Videos" => "film-strip",
            "Music" => "music-note", "Trash" => "trash", _ => "folder"
        };

        private async void ShowStorageDetails(StorageDetailState state)
        {
            _storageDetail = state;
            var version = ++_categoryBuildVersion;
            var token = BeginStorageScan();
            CategoryContent.Children.Clear();
            var title = state.FolderPath is null ? Loc.Get(state.Category.Name) : Path.GetFileName(state.FolderPath.TrimEnd('/'));
            var backIcon = new DaisyIcon { Icon = "caret-left", Size = 18 };
            backIcon.Bind(DaisyIcon.ForegroundProperty, this.GetResourceObservable("ContentPrimaryBrush"));
            var back = new Button { Content = backIcon, Classes = { "Subtle", "Small", "IconButton" } };
            ToolTip.SetTip(back, Loc.Get("Back"));
            Avalonia.Automation.AutomationProperties.SetName(back, Loc.Get("Back"));
            back.Click += (_, _) => { if (state.Parent is not null) ShowStorageDetails(state.Parent); else SelectCategory("Storage"); };
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            var heading = new TextBlock { Text = title, Classes = { "SettingsPageTitle" }, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            Grid.SetColumn(heading, 1); header.Children.Add(back); header.Children.Add(heading);
            if (state.Category.Paths is { Count: 1 } paths)
            {
                var open = new Button { Classes = { "Subtle" } };
                Loc.Bind(open, Button.ContentProperty, "Open in Files");
                open.Click += (_, _) => OpenStoragePath(paths[0], false);
                Grid.SetColumn(open, 2); header.Children.Add(open);
            }
            CategoryContent.Children.Add(header);
            CategoryContent.Children.Add(Secondary(Loc.Get("Largest items first. Open folders to explore, or use Files to manage them.")));
            CategoryContent.Children.Add(Secondary(StorageSize(state.Category.Bytes)));
            var loading = Secondary(Loc.Get("Calculating file sizes…"));
            CategoryContent.Children.Add(loading);
            try
            {
                var entries = await DaisyOS.System.Storage.LinuxStorageOverview.ReadEntriesAsync(state.Category, token);
                if (version != _categoryBuildVersion) return;
                CategoryContent.Children.Remove(loading);
                if (entries.Count == 0) CategoryContent.Children.Add(Secondary(Loc.Get("No files to show.")));
                var items = new StackPanel { Spacing = 8 };
                CategoryContent.Children.Add(items);
                var shown = 0;
                var more = new Button { Classes = { "Subtle" }, HorizontalAlignment = HorizontalAlignment.Stretch };
                Loc.Bind(more, Button.ContentProperty, "Show more");
                void ShowNextItems()
                {
                    items.Children.Remove(more);
                    foreach (var entry in entries.Skip(shown).Take(100))
                    {
                        var name = Path.GetFileName(entry.Path.TrimEnd('/'));
                        var button = SettingsAction(name, entry.Path, entry.IsDirectory ? "folder" : "file-text", () =>
                        {
                            if (entry.IsDirectory) ShowStorageDetails(new(state.Category with { Bytes = entry.Bytes, Paths = [entry.Path] }, entry.Path, state));
                            else OpenStoragePath(entry.Path, true);
                        }, StorageSize(entry.Bytes), literalTitle: true, literalDescription: true);
                        button.Tag = entry;
                        items.Children.Add(button);
                    }
                    shown = Math.Min(shown + 100, entries.Count);
                    if (shown < entries.Count) items.Children.Add(more);
                }
                more.Click += (_, _) => ShowNextItems();
                ShowNextItems();
            }
            catch (OperationCanceledException) { }
            catch (IOException)
            {
                if (version != _categoryBuildVersion) return;
                CategoryContent.Children.Remove(loading);
                CategoryContent.Children.Add(Secondary(Loc.Get("These files couldn’t be read. Open the folder in Files to inspect it.")));
            }
        }

        private async void OpenStoragePath(string path, bool selectItem)
        {
            var result = await PackagedApplicationLauncher.LaunchFilesAsync(new DaisyOS.Shell.Apps.Files.Services.FileManagerActivationRequest([path], selectItem));
            if (!result.Succeeded) (Application.Current as App)?.Feedback.Show(Loc.Get(result.Message));
        }

        private static string StorageSize(long bytes) => bytes switch
        {
            >= 1_000_000_000 => Loc.Format("{0:F1} GB", bytes / 1_000_000_000.0),
            >= 1_000_000 => Loc.Format("{0:F1} MB", bytes / 1_000_000.0),
            >= 1_000 => Loc.Format("{0:F1} KB", bytes / 1_000.0),
            _ => Loc.Format("{0} B", bytes)
        };

        private void BuildAccessibility()
        {
            AddSection(Loc.Get("DISPLAY"), Loc.Get("These changes apply immediately across the DaisyOS shell."),
                Toggle("High contrast", "accessibility.high-contrast", false, value => { _ = SetHighContrast(value); }),
                Toggle("Reduce motion", "accessibility.reduce-motion", false, value => { _ = SetReduceMotion(value); }));
        }

        private string _displayLanguageTag = Loc.Culture.Name;

        private void BuildLanguagePreference()
        {
            var picker = new ComboBox { MinWidth = 160, Classes = { "ThemeModePicker" }, Name = "SystemLanguagePicker" };
            foreach (var language in SystemLanguages.Supported)
                picker.Items.Add(new ComboBoxItem { Content = language.NativeName, Tag = language.Tag });
            var app = Application.Current as App;
            SelectComboByTag(picker, app?.CurrentLanguageTag ?? SystemLanguages.DefaultTag);
            picker.SelectionChanged += async (_, _) =>
            {
                if (app is null || picker.SelectedItem is not ComboBoxItem { Tag: string tag } || tag == app.CurrentLanguageTag) return;
                picker.IsEnabled = false;
                try { await app.SetLanguageAsync(tag); }
                finally { picker.IsEnabled = true; SelectComboByTag(picker, app.CurrentLanguageTag); }
            };
            AddSection(Loc.Get("LANGUAGE"), Loc.Get("Choose the language used by DaisyOS and newly started apps."),
                PreferenceRow(Loc.Get("System language"), Loc.Get("Sign out to apply the language to all applications."), picker));
        }

        private void BuildInput()
        {
            BuildLanguagePreference();
            var checking = Secondary(Loc.Get("Checking available keyboard layouts…"));
            CategoryContent.Children.Add(checking);
            _ = BuildKeyboardLayoutAsync(_categoryBuildVersion, checking);
        }

        private async Task BuildKeyboardLayoutAsync(int buildVersion, TextBlock checking)
        {
            KeyboardLayoutStatus status;
            try
            {
                status = await new LinuxKeyboardLayoutService(new SafeCommandRunner()).GetStatusAsync();
            }
            catch
            {
                status = new KeyboardLayoutStatus(false, "", "", [], "Keyboard layouts are unavailable on this system.");
            }

            if (buildVersion != _categoryBuildVersion)
            {
                return;
            }

            CategoryContent.Children.Remove(checking);
            if (!status.IsAvailable)
            {
                AddUnavailableMessage(Loc.Get(status.Detail));
                await BuildInputPreferencesAsync(buildVersion);
                return;
            }

            var layoutPicker = DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new ComboBox
            {
                Width = 260,
                Height = 32,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            });
            layoutPicker.Classes.Add("ThemeModePicker");
            layoutPicker.Classes.Add("KeyboardLayoutPicker");
            foreach (var layout in status.AvailableLayouts)
            {
                var item = new ComboBoxItem
                {
                    Content = new TextBlock
                    {
                        Text = layout.Label,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    Tag = layout
                };
                item.Classes.Add("ThemeModeOption");
                item.Classes.Add("KeyboardLayoutOption");
                ToolTip.SetTip(item, layout.Label);
                layoutPicker.Items.Add(item);
            }

            layoutPicker.SelectedItem = layoutPicker.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is KeyboardLayoutOption option &&
                                        string.Equals(option.Id, status.CurrentLayoutId, StringComparison.Ordinal));
            var apply = Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
            {
                Content = "Apply",
                Padding = new Thickness(12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = false
            }), Button.ContentProperty, "Apply");
            apply.Classes.Add("Active");
            apply.Classes.Add("Small");

            void UpdateApplyAvailability()
            {
                apply.IsEnabled = layoutPicker.SelectedItem is ComboBoxItem { Tag: KeyboardLayoutOption selected } &&
                                  !string.Equals(selected.Id, status.CurrentLayoutId, StringComparison.OrdinalIgnoreCase);
            }

            layoutPicker.SelectionChanged += (_, _) => UpdateApplyAvailability();
            apply.Click += async (_, _) =>
            {
                if (layoutPicker.SelectedItem is not ComboBoxItem { Tag: KeyboardLayoutOption selected })
                {
                    return;
                }

                layoutPicker.IsEnabled = false;
                apply.IsEnabled = false;
                var result = await new LinuxKeyboardLayoutService(new SafeCommandRunner()).SetLayoutAsync(selected.Id);
                if (buildVersion != _categoryBuildVersion)
                {
                    return;
                }

                if (!result.Succeeded)
                {
                    layoutPicker.SelectedItem = layoutPicker.Items.OfType<ComboBoxItem>()
                        .FirstOrDefault(item => item.Tag is KeyboardLayoutOption option &&
                                                string.Equals(option.Id, status.CurrentLayoutId, StringComparison.Ordinal));
                    layoutPicker.IsEnabled = true;
                    (Application.Current as App)?.Feedback.Show(Loc.Get(result.Message));
                    return;
                }

                (Application.Current as App)?.Feedback.Show(Loc.Get(result.Message));
                ShowCategory("Input");
            };

            var layoutControls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
            layoutControls.Children.Add(layoutPicker);
            layoutControls.Children.Add(apply);

            AddSection(Loc.Get("KEYBOARD LAYOUT"), Loc.Get("Choose the language and physical key arrangement used while typing."),
                PreferenceRow(Loc.Get("Keyboard layout"), Loc.Get(status.Detail), layoutControls));
            await BuildInputPreferencesAsync(buildVersion);
        }

        private async Task BuildInputPreferencesAsync(int buildVersion)
        {
            var hardwareService = new DaisyOS.System.Input.LinuxInputHardwareService();
            var status = await hardwareService.GetStatusAsync();
            if (buildVersion != _categoryBuildVersion) return;

            var scrollSpeedSlider = new QuickSettingsSlider
            {
                Minimum = 0,
                Maximum = 4,
                Value = status.TrackpadScrollSpeed,
                StepFrequency = 1,
                ShowTicks = true,
                ShowIcon = false,
                Width = 220,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Avalonia.Automation.AutomationProperties.SetName(scrollSpeedSlider, Loc.Get("Trackpad scroll speed"));
            ToolTip.SetTip(scrollSpeedSlider, GetScrollSpeedLabel((int)Math.Round(scrollSpeedSlider.Value)));

            int lastScrollSpeed = status.TrackpadScrollSpeed;
            int scrollSpeedChangeVersion = 0;
            var scrollSpeedUpdateLock = new SemaphoreSlim(1, 1);
            scrollSpeedSlider.ValueChanged += async (_, _) =>
            {
                var speedIndex = (int)Math.Round(scrollSpeedSlider.Value);
                ToolTip.SetTip(scrollSpeedSlider, GetScrollSpeedLabel(speedIndex));
                if (speedIndex == lastScrollSpeed) return;
                lastScrollSpeed = speedIndex;
                var changeVersion = ++scrollSpeedChangeVersion;

                // Coalesce drag events and serialize writes. If an older write is
                // already running, the newest selected tick is always written last.
                await Task.Delay(100);
                if (changeVersion != scrollSpeedChangeVersion) return;

                await scrollSpeedUpdateLock.WaitAsync();
                try
                {
                    if (changeVersion != scrollSpeedChangeVersion) return;
                    if (!await hardwareService.SetTrackpadScrollSpeedAsync(speedIndex))
                    {
                        (Application.Current as App)?.Feedback.Show(Loc.Get("Trackpad scroll speed was saved, but couldn’t be applied yet."));
                    }
                }
                finally
                {
                    scrollSpeedUpdateLock.Release();
                }
            };

            AddSection(Loc.Get("MOUSE & TRACKPAD"), null,
                Picker("Pointer speed", "input.pointer-speed", status.PointerSpeed, async speed =>
                {
                    if (!await hardwareService.SetPointerSpeedAsync(speed))
                        (Application.Current as App)?.Feedback.Show(Loc.Get("Pointer speed was saved, but couldn’t be applied yet."));
                    ShowCategory("Input");
                }, "Slow", "Normal", "Fast"),
                PreferenceRow(Loc.Get("Trackpad scroll speed"), $"{Loc.Get("Slowest")} ({Loc.Get("Default")})", scrollSpeedSlider),
                Toggle("Natural scrolling", "input.natural-scroll", status.NaturalScroll, async enabled =>
                {
                    if (!await hardwareService.SetNaturalScrollAsync(enabled))
                    {
                        (Application.Current as App)?.Feedback.Show(Loc.Get("Scrolling direction was saved, but couldn’t be applied yet."));
                        ShowCategory("Input");
                    }
                }),
                Toggle("Tap to click", "input.tap-to-click", status.TapToClick, async enabled =>
                {
                    if (!await hardwareService.SetTapToClickAsync(enabled))
                    {
                        (Application.Current as App)?.Feedback.Show(Loc.Get("Tap-to-click was saved, but couldn’t be applied yet."));
                        ShowCategory("Input");
                    }
                }));
            await BuildCursorPreferencesAsync(buildVersion);
        }

        private static string GetScrollSpeedLabel(int index) => index switch
        {
            0 => $"{Loc.Get("Slowest")} ({Loc.Get("Default")})",
            1 => Loc.Get("Slower"),
            2 => Loc.Get("Medium"),
            3 => Loc.Get("Faster"),
            4 => Loc.Get("Fastest"),
            _ => Loc.Get("Default")
        };

        private async Task BuildCursorPreferencesAsync(int buildVersion)
        {
            if (Application.Current is not App app) return;
            var themes = await app.Personalization.GetCursorThemesAsync();
            if (buildVersion != _categoryBuildVersion) return;

            var themePicker = DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new ComboBox
            {
                Width = 260,
                Height = 32,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            });
            themePicker.Classes.Add("ThemeModePicker");
            foreach (var theme in themes)
            {
                var item = new ComboBoxItem { Content = theme.DisplayName, Tag = theme.Id };
                item.Classes.Add("ThemeModeOption");
                themePicker.Items.Add(item);
            }

            var currentTheme = app.CurrentCursorThemeId ?? "DaisyOS-Dark";
            if (!themePicker.Items.OfType<ComboBoxItem>().Any(item => string.Equals(item.Tag as string, currentTheme, StringComparison.OrdinalIgnoreCase)))
            {
                var fallback = new ComboBoxItem { Content = currentTheme.Replace('_', ' '), Tag = currentTheme };
                fallback.Classes.Add("ThemeModeOption");
                themePicker.Items.Insert(0, fallback);
            }

            var sizePicker = DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new ComboBox
            {
                Width = 180,
                Height = 32,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            });
            sizePicker.Classes.Add("ThemeModePicker");
            foreach (var (label, size) in new[] { ("Small · 24 px", 24), ("Default · 32 px", 32), ("Large · 48 px", 48), ("Extra large · 64 px", 64) })
            {
                var item = new ComboBoxItem { Content = Loc.Get(label), Tag = size };
                item.Classes.Add("ThemeModeOption");
                sizePicker.Items.Add(item);
            }

            themePicker.SelectedItem = themePicker.Items.OfType<ComboBoxItem>()
                .First(item => string.Equals(item.Tag as string, currentTheme, StringComparison.OrdinalIgnoreCase));
            sizePicker.SelectedItem = sizePicker.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is int size && size == app.CurrentCursorSize)
                ?? sizePicker.Items.OfType<ComboBoxItem>().ElementAt(1);

            themePicker.SelectionChanged += async (_, _) =>
            {
                if (themePicker.SelectedItem is not ComboBoxItem { Tag: string theme }) return;
                themePicker.IsEnabled = sizePicker.IsEnabled = false;
                var succeeded = await app.SetCursorAsync(theme, app.CurrentCursorSize);
                if (!succeeded && buildVersion == _categoryBuildVersion) ShowCategory("Input");
                else themePicker.IsEnabled = sizePicker.IsEnabled = true;
            };
            sizePicker.SelectionChanged += async (_, _) =>
            {
                if (sizePicker.SelectedItem is not ComboBoxItem { Tag: int size }) return;
                var theme = (themePicker.SelectedItem as ComboBoxItem)?.Tag as string ?? currentTheme;
                themePicker.IsEnabled = sizePicker.IsEnabled = false;
                var succeeded = await app.SetCursorAsync(theme, size);
                if (!succeeded && buildVersion == _categoryBuildVersion) ShowCategory("Input");
                else themePicker.IsEnabled = sizePicker.IsEnabled = true;
            };

            AddSection(Loc.Get("CURSOR"), Loc.Get("Choose the pointer style and a comfortable size for every display."),
                PreferenceRow(Loc.Get("Pointer style"), Loc.Get("Installed cursor themes"), themePicker),
                PreferenceRow(Loc.Get("Pointer size"), Loc.Get("Applied immediately across the desktop"), sizePicker));
        }

        private void BuildDateTime(int buildVersion)
        {
            var checking = Secondary(Loc.Get("Checking date, time, and time-zone settings…"));
            CategoryContent.Children.Add(checking);
            _ = BuildDateTimeAsync(buildVersion, checking);
        }

        private async Task BuildDateTimeAsync(int buildVersion, TextBlock checking)
        {
            DateTimeSettingsStatus status;
            try
            {
                status = await new LinuxDateTimeSettingsService(new SafeCommandRunner()).GetStatusAsync();
            }
            catch
            {
                status = new DateTimeSettingsStatus(
                    false,
                    TimeZoneInfo.Local.Id,
                    false,
                    DateTimeOffset.Now,
                    [],
                    "Date and time controls are unavailable on this system.");
            }

            if (buildVersion != _categoryBuildVersion)
            {
                return;
            }

            CategoryContent.Children.Remove(checking);
            if (!status.IsAvailable)
            {
                AddUnavailableMessage(Loc.Get(status.Detail));
                return;
            }

            var automaticTime = new ToggleSwitch
            {
                IsChecked = status.AutomaticTime,
                VerticalAlignment = VerticalAlignment.Center
            };
            automaticTime.Click += async (_, _) =>
            {
                var enabled = automaticTime.IsChecked == true;
                automaticTime.IsEnabled = false;
                var result = await new LinuxDateTimeSettingsService(new SafeCommandRunner()).SetAutomaticTimeAsync(enabled);
                if (buildVersion != _categoryBuildVersion)
                {
                    return;
                }

                if (!result.Succeeded)
                {
                    automaticTime.IsChecked = !enabled;
                    automaticTime.IsEnabled = true;
                    (Application.Current as App)?.Feedback.Show(Loc.Get(result.Message));
                    return;
                }

                ShowCategory("DateTime");
            };

            var zonePicker = DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new ComboBox
            {
                Width = 300,
                Height = 32,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            });
            zonePicker.Classes.Add("ThemeModePicker");
            foreach (var zone in status.AvailableTimeZones
                         .Select(TimeZoneOption.Create)
                         .OrderBy(option => option.SortKey, StringComparer.Ordinal))
            {
                var item = new ComboBoxItem { Content = zone.DisplayName, Tag = zone };
                item.Classes.Add("ThemeModeOption");
                zonePicker.Items.Add(item);
            }
            zonePicker.SelectedItem = zonePicker.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is TimeZoneOption option &&
                                        string.Equals(option.Id, status.TimeZoneId, StringComparison.Ordinal));

            var zoneSelectionIsReady = false;
            zonePicker.SelectionChanged += async (_, _) =>
            {
                if (!zoneSelectionIsReady || zonePicker.SelectedItem is not ComboBoxItem { Tag: TimeZoneOption selected } ||
                    string.Equals(selected.Id, status.TimeZoneId, StringComparison.Ordinal))
                {
                    return;
                }

                zonePicker.IsEnabled = false;
                var result = await new LinuxDateTimeSettingsService(new SafeCommandRunner()).SetTimeZoneAsync(selected.Id);
                if (buildVersion != _categoryBuildVersion)
                {
                    return;
                }

                if (!result.Succeeded)
                {
                    zonePicker.SelectedItem = zonePicker.Items
                        .OfType<ComboBoxItem>()
                        .FirstOrDefault(item => item.Tag is TimeZoneOption option &&
                                                string.Equals(option.Id, status.TimeZoneId, StringComparison.Ordinal));
                    zonePicker.IsEnabled = true;
                    (Application.Current as App)?.Feedback.Show(Loc.Get(result.Message));
                    return;
                }

                ShowCategory("DateTime");
            };
            zoneSelectionIsReady = true;

            var isTwelveHourDefault = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('t') ||
                                      CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('h');
            var fallbackFormat = isTwelveHourDefault ? "12-hour" : "24-hour";
            var currentClockFormat = (Application.Current as App)?.GetPreference("time.clock-format", fallbackFormat) ?? fallbackFormat;
            if (currentClockFormat != "12-hour" && currentClockFormat != "24-hour")
            {
                currentClockFormat = fallbackFormat;
            }

            var timeFormatPicker = Picker("Time format", "time.clock-format", currentClockFormat, selected =>
            {
                var app = Application.Current as App;
                app?.SetPreference("time.clock-format", selected);
                ShowCategory("DateTime");
            }, "24-hour", "12-hour");

            var useTwelveHourTime = string.Equals(currentClockFormat, "12-hour", StringComparison.OrdinalIgnoreCase);
            var currentTimePattern = useTwelveHourTime ? "dddd, d MMMM yyyy, h:mm tt" : "dddd, d MMMM yyyy, HH:mm";

            AddSection(Loc.Get("TIME"), Loc.Get("Choose how your computer sets the date and time."),
                PreferenceRow(Loc.Get("Set time automatically"), Loc.Get("Use an internet time service when available."), automaticTime),
                InfoRow(Loc.Get("Current time"), status.LocalTime.ToString(currentTimePattern, global::System.Globalization.CultureInfo.CurrentCulture)),
                PreferenceRow(Loc.Get("Time zone"), Loc.Get("Choose where you are so time and daylight saving stay correct."), zonePicker),
                timeFormatPicker);

            if (!status.AutomaticTime)
            {
                AddSection(Loc.Get("MANUAL TIME"), Loc.Get("Set the date and time only when automatic time is turned off."),
                    ManualDateTimeRow(status.LocalTime, buildVersion, useTwelveHourTime));
            }

        }

        private Border ManualDateTimeRow(DateTimeOffset localTime, int buildVersion, bool useTwelveHour = false)
        {
            var datePicker = new DatePicker
            {
                SelectedDate = localTime,
                VerticalAlignment = VerticalAlignment.Center
            };
            var timePicker = new TimePicker
            {
                SelectedTime = localTime.TimeOfDay,
                ClockIdentifier = useTwelveHour ? "12HourClock" : "24HourClock",
                VerticalAlignment = VerticalAlignment.Center
            };
            var apply = Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
            {
                Content = "Set time",
                Padding = new Thickness(12, 0),
                VerticalAlignment = VerticalAlignment.Center
            }), Button.ContentProperty, "Set time");
            apply.Classes.Add("Active");
            apply.Classes.Add("Small");
            apply.Click += async (_, _) =>
            {
                if (datePicker.SelectedDate is not { } date || timePicker.SelectedTime is not { } time)
                {
                    (Application.Current as App)?.Feedback.Show(Loc.Get("Choose both a date and a time."));
                    return;
                }

                apply.IsEnabled = false;
                var local = date.Date + time;
                var result = await new LinuxDateTimeSettingsService(new SafeCommandRunner()).SetLocalTimeAsync(local);
                if (buildVersion != _categoryBuildVersion)
                {
                    return;
                }

                if (!result.Succeeded)
                {
                    apply.IsEnabled = true;
                    (Application.Current as App)?.Feedback.Show(Loc.Get(result.Message));
                    return;
                }

                ShowCategory("DateTime");
            };

            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { datePicker, timePicker, apply }
            };
            return PreferenceRow(Loc.Get("Date and time"), null, controls);
        }

        private void BuildAbout(int buildVersion)
        {
            AddSection(Loc.Get("DAISYOS"), Loc.Get("Software and hardware information for this computer."),
                InfoRow(Loc.Get("Version"), Loc.Get("0.1.0-alpha")));

            AddSection(Loc.Get("HARDWARE"), Loc.Get("System specifications detected on this computer."),
                InfoRow(Loc.Get("Processor"), GetCpuModel()),
                InfoRow(Loc.Get("Memory"), GetTotalMemory()),
                InfoRow(Loc.Get("Processor type"), RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => Loc.Get("64-bit"), Architecture.X86 => Loc.Get("32-bit"), Architecture.Arm64 => Loc.Get("64-bit ARM"), Architecture.Arm => Loc.Get("32-bit ARM"), _ => RuntimeInformation.ProcessArchitecture.ToString() }));

            AddSection(Loc.Get("OPERATING SYSTEM"), null,
                InfoRow(Loc.Get("Operating system"), GetOsPrettyName()),
                InfoRow(Loc.Get("Linux version"), GetKernelVersion()));

            var checking = Secondary(Loc.Get("Checking for system updates…"));
            CategoryContent.Children.Add(checking);
            _ = BuildUpdateStatusAsync(buildVersion, checking);

        }

        private async Task BuildUpdateStatusAsync(int buildVersion, TextBlock checking)
        {
            UpdateStatus status;
            try
            {
                status = await new LinuxUpdateService(new SafeCommandRunner()).CheckAsync();
            }
            catch
            {
                status = new UpdateStatus(false, false, false, true, "Couldn’t check for updates.", 0, 0, [], DateTimeOffset.Now,
                    "The system update service did not respond.");
            }

            if (buildVersion != _categoryBuildVersion) return;
            CategoryContent.Children.Remove(checking);

            var refresh = Loc.Bind(DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button
            {
                Content = "Check again",
                Classes = { "Subtle", "Small" },
                MinWidth = 96
            }), Button.ContentProperty, "Check again");
            refresh.Click += (_, _) => ShowCategory("About");

            var summary = status.HasError
                ? status.Detail
                : status.PendingCount == 0
                    ? "Your system is up to date."
                    : $"{status.PendingCount} {(status.PendingCount == 1 ? "update" : "updates")} available.";
            var rows = new List<Control>
            {
                InfoRow(Loc.Get("Update status"), summary),
                PreferenceRow(Loc.Get("Last checked"), status.LastChecked.ToLocalTime().ToString("g"), refresh)
            };
            if (status.IsRebootRequired)
                rows.Insert(1, InfoRow(Loc.Get("Restart required"), Loc.Get("Restart to finish applying installed updates.")));

            AddSection(Loc.Get("SYSTEM UPDATES"), status.HasError ? status.ErrorMessage : Loc.Get("Keep your computer up to date."), rows.ToArray());
        }

        private static string GetOsPrettyName()
        {
            try
            {
                if (File.Exists("/etc/os-release"))
                {
                    foreach (var line in File.ReadLines("/etc/os-release"))
                    {
                        if (line.StartsWith("PRETTY_NAME=", StringComparison.OrdinalIgnoreCase))
                        {
                            return line["PRETTY_NAME=".Length..].Trim('"', '\'', ' ');
                        }
                    }
                }
            }
            catch { }
            return RuntimeInformation.OSDescription;
        }

        private static string GetKernelVersion()
        {
            try
            {
                if (File.Exists("/proc/sys/kernel/osrelease"))
                {
                    return File.ReadAllText("/proc/sys/kernel/osrelease").Trim();
                }
            }
            catch { }
            return Environment.OSVersion.VersionString;
        }

        private static string GetCpuModel()
        {
            try
            {
                if (File.Exists("/proc/cpuinfo"))
                {
                    foreach (var line in File.ReadLines("/proc/cpuinfo"))
                    {
                        if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                        {
                            var idx = line.IndexOf(':');
                            if (idx > 0) return line[(idx + 1)..].Trim();
                        }
                    }
                }
            }
            catch { }
            return "Standard Processor";
        }

        private static string GetTotalMemory()
        {
            try
            {
                if (File.Exists("/proc/meminfo"))
                {
                    foreach (var line in File.ReadLines("/proc/meminfo"))
                    {
                        if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
                            {
                                var gb = kb / (1024.0 * 1024.0);
                                return $"{gb:F1} GB";
                            }
                        }
                    }
                }
            }
            catch { }

            var totalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (totalBytes > 0)
            {
                return $"{totalBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
            }
            return "Unknown";
        }

        private void AddSection(string label, string? description, params Control[] rows)
        {
            var section = new StackPanel { Spacing = 12 };
            var heading = new StackPanel { Spacing = 4 };
            heading.Children.Add(new TextBlock
            {
                Text = SentenceCase(label),
                Classes = { "SettingsSectionTitle" }
            });
            if (!string.IsNullOrWhiteSpace(description))
                heading.Children.Add(Secondary(description));
            section.Children.Add(heading);
            // Group related preferences into one calm, scannable surface. Individual rows
            // remain borderless; dividers expose relationships without visual noise.
            var rowsHost = new StackPanel { Spacing = 0 };
            for (var index = 0; index < rows.Length; index++)
            {
                rowsHost.Children.Add(rows[index]);
                if (index < rows.Length - 1)
                {
                    rowsHost.Children.Add(new Border
                    {
                        Height = 1,
                        Margin = new Thickness(16, 0),
                        Background = TryBrush("StrokeDefaultBrush")
                    });
                }
            }

            var group = new Border { Child = rowsHost };
            group.Classes.Add("SettingsGroupCard");
            if (rows.Length == 1 && rows[0] is Button button && button.Classes.Contains("SettingsGroupAction"))
            {
                rowsHost.Children.Clear();
                section.Children.Add(button);
            }
            else section.Children.Add(group);
            CategoryContent.Children.Add(section);

            if (string.Equals(_pendingSection, Loc.Original(label), StringComparison.Ordinal))
            {
                _pendingSection = null;
                var requestedTitle = _pendingSettingTitle;
                _pendingSettingTitle = null;
                Dispatcher.UIThread.Post(() =>
                {
                    var targetRow = rows.FirstOrDefault(row => string.Equals(Loc.Original(row.Tag as string ?? string.Empty), requestedTitle, StringComparison.OrdinalIgnoreCase))
                        ?? rows.FirstOrDefault();
                    (targetRow ?? section).BringIntoView();
                    targetRow?.GetVisualDescendants()
                        .OfType<Control>()
                        .FirstOrDefault(control => control.Focusable && control.IsEffectivelyEnabled)
                        ?.Focus(NavigationMethod.Directional);
                }, DispatcherPriority.Render);
            }
        }

        private void FocusAppearanceSearchTarget(string? title)
        {
            _pendingSettingTitle = null;
            Control? target = title switch
            {
                "Profile picture" => ChooseProfilePictureButton,
                "Theme" => ThemeModePicker,
                "Taskbar size" => TaskbarSizeSlider,
                "Taskbar and systembar" => TaskbarVisibilityPicker,
                "Wallpaper" => WallpaperTypePicker,
                "Accent color" => AccentDefaultButton,
                "System font" => SystemFontPicker,
                _ => null
            };
            if (target is null) return;
            Dispatcher.UIThread.Post(() =>
            {
                target.BringIntoView();
                target.Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Render);
        }

        private void AddUnavailableMessage(string message)
        {
            // Hardware availability is not a preference. Keep this calm and ungrouped so it
            // does not look like a disabled row with hidden actions or decorative dividers.
            CategoryContent.Children.Add(DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
            {
                Text = message,
                FontSize = 13,
                Foreground = TryBrush("ContentSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            }, "BodySmall"));
        }

        private Border Toggle(string title, string key, bool fallback, Action<bool>? apply = null)
        {
            var app = Application.Current as App;
            var value = key switch
            {
                "accessibility.high-contrast" when app is not null => app.HighContrastEnabled,
                "accessibility.reduce-motion" when app is not null => app.ReduceMotionEnabled,
                "notifications.dnd" when app is not null => app.SessionState.DoNotDisturb,
                "gaming.mode" when app is not null => app.SessionState.GamingMode,
                "system.show-date" when app is not null => app.SessionState.ShowDateInSystemBar,
                _ => fallback
            };
            var toggle = new ToggleSwitch { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += (_, _) =>
            {
                var enabled = toggle.IsChecked == true;
                apply?.Invoke(enabled);
            };
            return PreferenceRow(Loc.Get(title), null, toggle);
        }

        private Border Picker(string title, string key, string fallback, params string[] options) =>
            Picker(title, key, fallback, null, options);

        private Border Picker(string title, string key, string fallback, Action<string>? onChanged, params string[] options)
        {
            // Backend-backed pages pass the state just read from Linux as the fallback.
            // Never let an old shell preference override newer changes made by the system,
            // another settings tool, or a hardware event.
            var saved = fallback;
            if (options.Length > 0 && Array.IndexOf(options, saved) < 0)
            {
                var pref = (Application.Current as App)?.GetPreference(key);
                if (!string.IsNullOrEmpty(pref) && Array.IndexOf(options, pref) >= 0)
                    saved = pref;
            }
            else if (saved == "100%" && key == "display.scale")
            {
                var pref = (Application.Current as App)?.GetPreference(key);
                if (!string.IsNullOrEmpty(pref) && Array.IndexOf(options, pref) >= 0)
                    saved = pref;
            }
            var picker = DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new ComboBox
            {
                Width = double.NaN,
                MinWidth = 200,
                MaxWidth = 440,
                Height = 32,
                SelectionBoxItemTemplate = new FuncDataTemplate<string>((value, _) => new TextBlock
                {
                    Text = value,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            });
            // Reuse Appearance's picker contract instead of creating a second dropdown visual language.
            picker.Classes.Add("ThemeModePicker");
            foreach (var option in options)
            {
                var item = new ComboBoxItem { Content = key.StartsWith("power.", StringComparison.Ordinal) || key == "input.pointer-speed" || key == "time.clock-format" ? Loc.Get(option) : option, Tag = option };
                item.Classes.Add("ThemeModeOption");
                picker.Items.Add(item);
            }
            picker.SelectedIndex = Math.Max(0, Array.IndexOf(options, saved));
            ToolTip.SetTip(picker, saved);
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is ComboBoxItem { Tag: string selected })
                {
                    ToolTip.SetTip(picker, selected);
                    Dispatcher.UIThread.Post(() => onChanged?.Invoke(selected));
                }
            };
            return PreferenceRow(Loc.Get(title), null, picker);
        }

        private Border InfoRow(string title, string value) =>
            PreferenceRow(title, null, DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
            {
                Text = value,
                FontSize = 13,
                Foreground = TryBrush("ContentSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                MaxWidth = 360,
                TextWrapping = TextWrapping.Wrap
            }, "BodySmall"));

        private Border VolumeRow(string title, bool input)
        {
            var app = Application.Current as App;
            var slider = new QuickSettingsSlider
            {
                Minimum = 0,
                Maximum = 100,
                Width = 200,
                Value = app?.SessionState.LastKnownVolume ?? 72,
                Icon = input ? "microphone" : "speaker-high",
                VerticalAlignment = VerticalAlignment.Center
            };
            slider.ValueChanged += (_, _) =>
            {
                var volume = Math.Round(slider.Value);
                if (!input && app is not null) app.SessionState.LastKnownVolume = volume;
                var audio = new LinuxAudioService(new SafeCommandRunner());
                _ = input ? audio.SetInputVolumeAsync(volume) : audio.SetVolumeAsync(volume);
            };
            _ = LoadVolumeAsync(slider, input);
            return PreferenceRow(title, input ? Loc.Get("Microphone") : Loc.Get("System output"), slider);
        }

        private Border PreferenceRow(string title, string? description, Control? trailing, string? iconOverride = null)
        {
            var icon = iconOverride is null ? GetSettingIcon(Loc.Original(title)) : new SettingIcon(iconOverride);
            var content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(icon is null ? "*,Auto" : "Auto,*,Auto"),
                ColumnSpacing = icon is null ? 16 : 0,
                VerticalAlignment = VerticalAlignment.Center
            };
            var copy = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(new TextBlock { Text = title, Classes = { "SettingsLabel" } });
            if (!string.IsNullOrWhiteSpace(description)) copy.Children.Add(Secondary(description));
            if (icon is not null)
            {
                var iconBlock = new DaisyIcon { Icon = icon.Glyph, Foreground = TryBrush(icon.BrushResource ?? "ContentSecondaryBrush") };
                iconBlock.Classes.Add("SettingsRowIcon");
                content.Children.Add(iconBlock);
                Grid.SetColumn(copy, 1);
            }
            content.Children.Add(copy);
            if (trailing is not null)
            {
                Grid.SetColumn(trailing, icon is null ? 1 : 2);
                content.Children.Add(trailing);
            }

            var row = new Border
            {
                Background = Brushes.Transparent,
                Tag = title,
                Child = content
            };
            row.Classes.Add("SettingsGroupRow");
            // Give labels a full line when an input would otherwise squeeze them.
            if (trailing is not null)
            {
                bool? stacked = null;
                row.SizeChanged += (_, _) =>
                {
                    var fontScale = (Application.Current?.Resources["TypeSizeBody"] as double? ?? 14) / 14;
                    var narrow = row.Bounds.Width < 600 * fontScale && trailing is not ToggleSwitch;
                    if (trailing is ComboBox picker && double.IsNaN(picker.Width))
                    {
                        var available = row.Bounds.Width - row.Padding.Left - row.Padding.Right - (icon is null ? 0 : 38);
                        picker.MaxWidth = Math.Max(0, Math.Min(440, available));
                        picker.MinWidth = Math.Min(picker.MinWidth, picker.MaxWidth);
                    }
                    if (stacked == narrow) return;
                    stacked = narrow;
                    content.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
                    content.RowSpacing = narrow ? 12 : 0;
                    Grid.SetRow(trailing, narrow ? 1 : 0);
                    Grid.SetColumn(trailing, narrow ? (icon is null ? 0 : 1) : (icon is null ? 1 : 2));
                    trailing.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                };
            }
            return row;
        }

        private static SettingIcon? GetSettingIcon(string title) => title switch
        {
            "Power mode" => new("power"),
            "Show date in the system bar" => new("calendar"),

            "Keyboard layout" => new("keyboard"),

            "Connected display" => new("desktop"),
            "Resolution" => new("monitor"),
            "Preferred scale" => new("arrows-out-cardinal"),
            "Refresh rate" => new("gauge"),
            "Use night light" => new("sun"),
            "Use HDR when available" => new("high-definition"),

            "Speakers and headphones" => new("speaker-high"),
            "Output volume" => new("speaker-high"),
            "Input volume" => new("microphone"),
            "Mute microphone" => new("microphone-slash"),

            "Current connection" or "Connection" or "Internet access" => new("globe"),
            "Ethernet" or "Ethernet status" or "Wired" => new("Ethernet"),
            "Wi-Fi" or "Wi-Fi status" => new("wifi-02"),
            "Bluetooth" => new("bluetooth"),

            "Version" => new("info"),
            "Processor" => new("cpu"),
            "Memory" => new("ram-memory"),
            "Processor type" => new("desktop"),
            "Operating system" or "Linux version" => new("terminal-window"),

            "Do not disturb" => new("minus-circle"),

            "Web links" => new("globe"),
            "New files open with" => new("folder-open"),
            "High contrast" => new("circle-half"),
            "Reduce motion" => new("pause-circle"),
            "Pointer speed" => new("pointer_speed"),
            "Pointer style" or "Pointer size" => new("cursor"),
            "Trackpad scroll speed" or "Scrolling speed" => new("arrows-down-up"),
            "Natural scrolling" => new("arrows-down-up"),
            "Tap to click" => new("hand-tap"),

            "Set time automatically" => new("clock"),
            "Current time" or "Date and time" => new("calendar"),
            "Time zone" => new("globe"),
            "Time format" => new("clock"),
            "Desktop session" => new("desktop"),
            _ => null
        };

        private static bool ContainsIgnoreCase(string value, string fragment) =>
            value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

        private void UpdateContentWidth()
        {
            // Scrollbars live at the panel edge. Reserve a stable gutter plus readable
            // content insets here, instead of shrinking the ScrollViewer itself.
            const double scrollbarGutter = 16;
            const double contentHorizontalInset = 72;
            var width = Math.Min(800, ContentArea.Bounds.Width - scrollbarGutter - contentHorizontalInset);
            if (width <= 0)
            {
                return;
            }

            CategoryContent.Width = width;
            AppearanceContent.Width = width;
            UpdateAppearanceLayout(width < 640 * (Application.Current?.Resources["TypeSizeBody"] as double? ?? 14) / 14);
        }

        private bool? _appearanceStacked;

        private void UpdateAppearanceLayout(bool stacked)
        {
            if (_appearanceStacked == stacked) return;
            _appearanceStacked = stacked;
            WallpaperLayout.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "200,*");
            WallpaperLayout.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
            Grid.SetColumn(WallpaperActions, stacked ? 0 : 1);
            Grid.SetRow(WallpaperActions, stacked ? 1 : 0);

            FontHeaderLayout.ColumnDefinitions = new ColumnDefinitions(stacked ? "Auto,Auto,*" : "Auto,*,Auto,Auto");
            FontHeaderLayout.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
            Grid.SetColumnSpan(FontHeaderLayout.Children[1], stacked ? 2 : 1);
            for (var i = 2; i < FontHeaderLayout.Children.Count; i++)
            {
                Grid.SetRow(FontHeaderLayout.Children[i], stacked ? 1 : 0);
                Grid.SetColumn(FontHeaderLayout.Children[i], stacked ? i - 1 : i);
                FontHeaderLayout.Children[i].HorizontalAlignment = HorizontalAlignment.Left;
            }
            FontPickerLayout.ColumnDefinitions = new ColumnDefinitions("*");
            FontPickerLayout.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(SystemFontPicker, 0);
            Grid.SetRow(SystemFontPicker, 0);
            foreach (var grid in new[] { TextSizeLayout, AccentLayout })
            {
                grid.ColumnDefinitions = new ColumnDefinitions(stacked ? "Auto,*" : "Auto,*,Auto");
                grid.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
                Grid.SetColumn(grid.Children[2], stacked ? 1 : 2);
                Grid.SetRow(grid.Children[2], stacked ? 1 : 0);
                grid.Children[2].HorizontalAlignment = HorizontalAlignment.Left;
            }
            TextScalePicker.Width = 200;
        }

        private static string SentenceCase(string label) => label switch
        {
            "DAISYOS" => "DaisyOS",
            "WI-FI" => "Wi-Fi",
            _ => label.Length == 0 ? label : char.ToUpperInvariant(label[0]) + label[1..].ToLowerInvariant()
        };

        private static TextBlock Secondary(string text) => new()
        {
            Text = text,
            Classes = { "SettingsDescription" }
        };

        private sealed record TimeZoneOption(string Id, string DisplayName, string SortKey)
        {
            public override string ToString() => DisplayName;

            public static TimeZoneOption Create(string id)
            {
                var friendlyName = id.Replace('_', ' ');
                var parts = friendlyName.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var city = parts.LastOrDefault() ?? friendlyName;
                var region = string.Join(" · ", parts.Take(Math.Max(0, parts.Length - 1)));
                var offset = TimeSpan.Zero;
                try
                {
                    offset = TimeZoneInfo.FindSystemTimeZoneById(id).GetUtcOffset(DateTimeOffset.Now);
                }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }

                var sign = offset < TimeSpan.Zero ? "-" : "+";
                var display = $"(UTC{sign}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}) {city}";
                if (!string.IsNullOrWhiteSpace(region)) display += $" · {region}";
                return new TimeZoneOption(id, display, $"{offset.TotalMinutes + 840:0000} {display}");
            }
        }

        private sealed record SettingsSearchEntry(
            string Category,
            string Section,
            string Title,
            string Description,
            string Keywords)
        {
            public string SearchText => $"{Category} {Section} {Title} {Description} {Keywords} {Loc.Get(Section)} {Loc.Get(Title)} {Loc.Get(Description)}";
        }

        private sealed record SettingIcon(string Glyph, string? BrushResource = null);

        private IBrush? TryBrush(string resourceKey) =>
            this.TryFindResource(resourceKey, ActualThemeVariant, out var value) ? value as IBrush : null;

        private async Task LoadVolumeAsync(QuickSettingsSlider slider, bool input)
        {
            try
            {
                var audio = new LinuxAudioService(new SafeCommandRunner());
                var volume = input ? await audio.GetInputVolumeAsync() : await audio.GetVolumeAsync();
                if (volume is not null) slider.Value = volume.Value;
            }
            catch { /* Keep the last safe slider position if PipeWire is unavailable. */ }
        }

        private async Task SetMicrophoneMuted(bool muted)
        {
            try
            {
                await new LinuxAudioService(new SafeCommandRunner()).SetInputMutedAsync(muted);
                ShowCategory("Sound");
            }
            catch
            {
                (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t change microphone mute."));
                ShowCategory("Sound");
            }
        }

        private async Task SetWifi(bool enabled)
        {
            try
            {
                var changed = await new LinuxWirelessNetworkService(new SafeCommandRunner()).SetEnabledAsync(enabled);
                if (!changed) (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t change Wi-Fi."));
            }
            catch { (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t change Wi-Fi.")); }
            ShowCategory("Network");
        }

        private async Task SetBluetooth(bool enabled)
        {
            try
            {
                var changed = await new LinuxBluetoothService(new SafeCommandRunner()).SetEnabledAsync(enabled);
                if (!changed) (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t change Bluetooth."));
            }
            catch { (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t change Bluetooth.")); }
            ShowCategory("Bluetooth");
        }

        private async Task SetHighContrast(bool enabled)
        {
            if (Application.Current is App app) await app.SetHighContrastAsync(enabled);
        }

        private async Task SetReduceMotion(bool enabled)
        {
            if (Application.Current is App app) await app.SetReduceMotionAsync(enabled);
        }

        private async void OnThemeModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingAppearanceControls ||
                sender is not ComboBox { SelectedItem: ComboBoxItem { Tag: string value } } ||
                !Enum.TryParse<ThemeMode>(value, out var mode) ||
                Application.Current is not App app)
            {
                return;
            }

            await app.SetThemeModeAsync(mode);
        }

        private async void OnAccentColorClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string value } ||
                !Enum.TryParse<AccentColor>(value, out var color) ||
                Application.Current is not App app)
            {
                return;
            }

            await app.SetAccentColorAsync(color);
        }

        private bool _changingProfilePicture;

        private async void OnChooseProfilePictureClicked(object? sender, RoutedEventArgs e)
        {
            if (_changingProfilePicture || TopLevel.GetTopLevel(this) is not { } topLevel || Application.Current is not App app) return;
            _changingProfilePicture = true;
            ChooseProfilePictureButton.IsEnabled = RemoveProfilePictureButton.IsEnabled = false;
            try
            {
                var files = await app.PickImageFilesAsync(topLevel, allowMultiple: false);
                if (files.Count > 0) await app.SetProfilePictureAsync(files[0]);
            }
            catch (Exception ex)
            {
                app.Feedback.Show(Loc.Get("This picture couldn’t be used. Choose a PNG, JPEG or WebP picture smaller than 16 MB and 24 megapixels."));
                global::System.Console.Error.WriteLine($"[settings] Profile picture import failed: {ex.Message}");
            }
            finally
            {
                _changingProfilePicture = false;
                ChooseProfilePictureButton.IsEnabled = RemoveProfilePictureButton.IsEnabled = true;
                RemoveProfilePictureButton.IsVisible = app.HasProfilePicture;
            }
        }

        private async void OnRemoveProfilePictureClicked(object? sender, RoutedEventArgs e)
        {
            if (_changingProfilePicture || Application.Current is not App app) return;
            _changingProfilePicture = true;
            ChooseProfilePictureButton.IsEnabled = RemoveProfilePictureButton.IsEnabled = false;
            try { await app.SetProfilePictureAsync(null); }
            catch { app.Feedback.Show(Loc.Get("The profile picture couldn’t be removed. Please try again.")); }
            finally
            {
                _changingProfilePicture = false;
                ChooseProfilePictureButton.IsEnabled = RemoveProfilePictureButton.IsEnabled = true;
                RemoveProfilePictureButton.IsVisible = app.HasProfilePicture;
            }
        }

        private async void OnPickWallpaperClicked(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null || Application.Current is not App app)
            {
                return;
            }

            var choosingVideo = _wallpaperSelection == WallpaperSelection.Video;
            var files = choosingVideo
                ? await app.PickFilesAsync(topLevel, new DaisyOS.Shell.Apps.Files.Picker.DaisyFilePickerOptions(
                    "Choose video wallpaper", false, false,
                    WallpaperFileTypes.VideoPatterns.Select(pattern => pattern.TrimStart('*')).ToArray())) ?? []
                : await app.PickImageFilesAsync(topLevel, allowMultiple: false, allowHeic: true);

            if (files.Count == 0 || string.IsNullOrWhiteSpace(files[0]))
            {
                return;
            }

            try
            {
                await app.SetWallpaperAsync(files[0]);
            }
            catch (Exception ex)
            {
                app.Feedback.Show(Loc.Get("This wallpaper couldn’t be used. Your current wallpaper is unchanged."));
                global::System.Console.Error.WriteLine($"[settings] Could not use wallpaper safely: {ex.Message}");
            }
        }

        private async void OnWallpaperTypeSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingAppearanceControls || Application.Current is not App app)
            {
                return;
            }

            _wallpaperSelection = WallpaperTypePicker.SelectedItem is ComboBoxItem { Tag: string type }
                ? ParseWallpaperSelection(type)
                : WallpaperSelection.Picture;

            if (_wallpaperSelection == WallpaperSelection.Solid)
            {
                await app.SetSolidWallpaperAsync(app.CurrentSolidWallpaperColor);
            }
            else if (app.UsesSolidWallpaper)
            {
                // Solid colour is a reversible presentation choice. The stored
                // file source remains untouched, so returning to Picture/Video
                // always restores the exact wallpaper the user had before.
                await app.UseImageWallpaperAsync();
            }

            UpdateWallpaperSelectionUi(_wallpaperSelection, app);
        }

        private async void OnSolidWallpaperColorClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || Application.Current is not App app)
            {
                return;
            }

            var color = button.Tag switch
            {
                Color value => value,
                string text when Color.TryParse(text, out var parsed) => parsed,
                _ => default
            };
            if (color == default) return;

            await app.SetSolidWallpaperAsync(color);
        }

        private async void OnHighContrastToggled(object? sender, RoutedEventArgs e)
        {
            if (Application.Current is App app && HighContrastToggle is not null)
            {
                await app.SetHighContrastAsync(HighContrastToggle.IsChecked == true);
            }
        }

        private async void OnReduceMotionToggled(object? sender, RoutedEventArgs e)
        {
            if (Application.Current is App app && ReduceMotionToggle is not null)
            {
                await app.SetReduceMotionAsync(ReduceMotionToggle.IsChecked == true);
            }
        }

        private async void OnTaskbarSizeChanged(object? sender, EventArgs e)
        {
            if (_isRefreshingAppearanceControls || sender is not QuickSettingsSlider { IsLoaded: true } slider || Application.Current is not App app) return;
            await app.SetTaskbarSizeAsync((int)Math.Round(slider.Value));
        }

        private async void OnTaskbarVisibilitySelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingAppearanceControls || TaskbarVisibilityPicker.SelectedItem is not ComboBoxItem { Tag: string value } || Application.Current is not App app)
                return;
            await app.SetTaskbarAutoHideAsync(value == "AutoHide");
        }

        private async void OnMediaWidgetVisibilitySelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingAppearanceControls || MediaWidgetVisibilityPicker.SelectedItem is not ComboBoxItem { Tag: string value } || Application.Current is not App app)
                return;
            await app.SetMediaWidgetAutoHideAsync(value == "AutoHide");
        }

        private async void OnSystemBarVisibilitySelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingAppearanceControls || SystemBarVisibilityPicker.SelectedItem is not ComboBoxItem { Tag: string value } || Application.Current is not App app)
                return;
            await app.SetSystemBarAutoHideAsync(value == "AutoHide");
        }

        private async Task LoadPersonalizationOptionsAsync(bool force = false)
        {
            if (_personalizationListsLoaded && !force || Application.Current is not App app) return;
            _personalizationListsLoaded = true;
            var fonts = await app.Personalization.GetFontFamiliesAsync();
            if (SystemFontPicker is null) return;
            _availableFontFamilies.Clear();
            _availableFontFamilies.Add(app.CurrentSystemFontFamily);
            _availableFontFamilies.Add(DaisyOS.Shell.Services.Theming.TypographyPolicy.DefaultFamilyName);
            _availableFontFamilies.Add("Inter");
            _availableFontFamilies.AddRange(fonts.Select(font => font.FamilyName));
            var distinct = _availableFontFamilies.Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(family => family, StringComparer.CurrentCultureIgnoreCase).ToArray();
            _availableFontFamilies.Clear();
            _availableFontFamilies.AddRange(distinct);
            PopulateFontPicker(FontSearchBar.Text);
        }

        private void PopulateFontPicker(string? query)
        {
            if (Application.Current is not App app || SystemFontPicker is null) return;
            _isRefreshingAppearanceControls = true;
            SystemFontPicker.Items.Clear();
            foreach (var family in _availableFontFamilies)
            {
                var item = new ComboBoxItem { Content = family, Tag = family };
                item.FontFamily = DaisyOS.Shell.Services.Theming.TypographyPolicy.ResolveFontFamily(family);
                SystemFontPicker.Items.Add(item);
            }
            SelectComboByTag(SystemFontPicker, app.CurrentSystemFontFamily);
            _isRefreshingAppearanceControls = false;
            FilterFontPicker(query);
        }

        private void FilterFontPicker(string? query)
        {
            var normalized = query?.Trim() ?? string.Empty;
            foreach (var item in SystemFontPicker.Items.OfType<ComboBoxItem>())
                item.IsVisible = normalized.Length == 0 || (item.Tag is string family && family.Contains(normalized, StringComparison.CurrentCultureIgnoreCase));
        }

        private async void OnSystemFontSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingAppearanceControls || Application.Current is not App app || SystemFontPicker.SelectedItem is not ComboBoxItem { Tag: string family }) return;
            await ApplyTypographyAsync(app, family, app.CurrentTextScalePercent);
        }

        private async void OnTextScaleSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingAppearanceControls || Application.Current is not App app ||
                TextScalePicker.SelectedItem is not ComboBoxItem { Tag: string value } || !int.TryParse(value, out var scale)) return;
            await ApplyTypographyAsync(app, app.CurrentSystemFontFamily, scale);
        }

        private async Task ApplyTypographyAsync(App app, string family, int scale)
        {
            SystemFontPicker.IsEnabled = TextScalePicker.IsEnabled = false;
            var success = await app.SetTypographyAsync(family, scale);
            SystemFontPicker.IsEnabled = TextScalePicker.IsEnabled = true;
            if (!success) RefreshAppearanceControls();
        }

        private async void OnAddFontClicked(object? sender, RoutedEventArgs e)
        {
            if (Application.Current is not App app) return;
            var path = await app.PickFontFileAsync(TopLevel.GetTopLevel(this));
            if (path is null) return;
            var result = await app.InstallSystemFontAsync(path);
            if (!result.Succeeded || result.Families.Count == 0) return;
            await LoadPersonalizationOptionsAsync(force: true);
            await app.SetTypographyAsync(result.Families[0], app.CurrentTextScalePercent);
            SelectComboByTag(SystemFontPicker, result.Families[0]);
            app.Feedback.Show(Loc.Format("{0} was installed and selected.", result.Families[0]));
        }

        private async void OnRestoreDefaultFontClicked(object? sender, RoutedEventArgs e)
        {
            if (Application.Current is not App app) return;
            FontSearchBar.Text = string.Empty;
            await ApplyTypographyAsync(app, DaisyOS.Shell.Services.Theming.TypographyPolicy.DefaultFamilyName, app.CurrentTextScalePercent);
            SelectComboByTag(SystemFontPicker, DaisyOS.Shell.Services.Theming.TypographyPolicy.DefaultFamilyName);
            app.Feedback.Show(Loc.Get("The DaisyOS system font was restored."));
        }

        private static void SelectComboByTag(ComboBox comboBox, object? tag)
        {
            comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, tag));
        }

        private void OnAppearanceChanged(object? sender, EventArgs e)
        {
            RefreshAppearanceControls();
            UpdateContentWidth();
            if (_displayLanguageTag == Loc.Culture.Name) return;
            _displayLanguageTag = Loc.Culture.Name;
            OrganizeNavigation();
            FilterNavigation(NavigationSearch.Text ?? string.Empty);
            if (_activeCategory == "Storage" && _storageDetail is not null) ShowStorageDetails(_storageDetail);
            else ShowCategory(_activeCategory);
        }

        private void RefreshAppearanceControls()
        {
            if (Application.Current is not App app || AppearanceView is null)
            {
                return;
            }

            _isRefreshingAppearanceControls = true;
            ThemeModePicker.SelectedIndex = app.CurrentThemeMode switch
            {
                ThemeMode.Light => 0,
                ThemeMode.Dark => 1,
                _ => 2
            };
            AutomaticThemeInfo.IsVisible = app.CurrentThemeMode == ThemeMode.Automatic;
            AutomaticThemeInfo.Text = app.GetAutomaticThemeScheduleDescription();
            HighContrastToggle.IsChecked = app.HighContrastEnabled;
            ReduceMotionToggle.IsChecked = app.ReduceMotionEnabled;
            TaskbarSizeSlider.Value = app.CurrentTaskbarSize;
            SelectComboByTag(TaskbarVisibilityPicker, app.TaskbarAutoHideEnabled ? "AutoHide" : "Always");
            SelectComboByTag(SystemBarVisibilityPicker, app.SystemBarAutoHideEnabled ? "AutoHide" : "Always");
            SelectComboByTag(MediaWidgetVisibilityPicker, app.MediaWidgetAutoHideEnabled ? "AutoHide" : "Always");
            SelectComboByTag(SystemFontPicker, app.CurrentSystemFontFamily);
            RestoreDefaultFontButton.IsEnabled = !string.Equals(app.CurrentSystemFontFamily, DaisyOS.Shell.Services.Theming.TypographyPolicy.DefaultFamilyName, StringComparison.OrdinalIgnoreCase);
            RemoveProfilePictureButton.IsVisible = app.HasProfilePicture;
            TextScalePicker.SelectedIndex = app.CurrentTextScalePercent switch { 90 => 0, 110 => 2, 125 => 3, 150 => 4, _ => 1 };
            _wallpaperSelection = app.UsesSolidWallpaper
                ? WallpaperSelection.Solid
                : WallpaperFileTypes.IsVideo(app.CurrentWallpaperUri)
                    ? WallpaperSelection.Video
                    : WallpaperSelection.Picture;
            WallpaperTypePicker.SelectedIndex = (int)_wallpaperSelection;
            _isRefreshingAppearanceControls = false;
            UpdateWallpaperSelectionUi(_wallpaperSelection, app);
            if (app.UsesSolidWallpaper)
            {
                SolidWallpaperPreview.Background = new SolidColorBrush(app.CurrentSolidWallpaperColor);
                SetSolidWallpaperColourSelection(app.CurrentSolidWallpaperColor);
            }
            else
            {
                RefreshWallpaperPreview(app.CurrentWallpaperUri);
            }

            SetActive(AccentDefaultButton, app.CurrentAccentColor is AccentColor.Default or AccentColor.Blue);
            SetActive(AccentPurpleButton, app.CurrentAccentColor == AccentColor.Purple);
            SetActive(AccentPinkButton, app.CurrentAccentColor == AccentColor.Pink);
            SetActive(AccentRedButton, app.CurrentAccentColor == AccentColor.Red);
            SetActive(AccentOrangeButton, app.CurrentAccentColor == AccentColor.Orange);
            SetActive(AccentGreenButton, app.CurrentAccentColor == AccentColor.Green);
            SetActive(AccentGrayButton, app.CurrentAccentColor == AccentColor.Gray);
            SetActive(AccentWhiteButton, app.CurrentAccentColor == AccentColor.White);
        }

        private static void SetActive(Button? button, bool isActive)
        {
            if (button is null) return;
            var selectedClass = button.Classes.Contains("AccentSwatch") ? "Selected" : "Active";
            if (isActive) button.Classes.Add(selectedClass);
            else button.Classes.Remove(selectedClass);
        }

        private void SetSolidWallpaperColourSelection(Color color)
        {
            foreach (var button in new[]
                     {
                         SolidColorBlackButton, SolidColorGraphiteButton, SolidColorSlateButton, SolidColorMidnightButton,
                         SolidColorOceanButton, SolidColorTealButton, SolidColorForestButton, SolidColorPlumButton,
                         SolidColorClayButton, SolidColorSandButton, SolidColorMistButton, SolidColorWhiteButton
                     })
            {
                if (button?.Tag is not string colorText || !Color.TryParse(colorText, out var optionColor))
                {
                    continue;
                }

                button.Classes.Set("Selected", optionColor.R == color.R && optionColor.G == color.G && optionColor.B == color.B);
            }
        }

        private void UpdateWallpaperSelectionUi(WallpaperSelection selection, App app)
        {
            var isSolid = selection == WallpaperSelection.Solid;
            WallpaperBrowseRow.IsVisible = !isSolid;
            WallpaperBrowseButton.IsVisible = !isSolid;
            SolidWallpaperOptions.IsVisible = isSolid;
            SolidWallpaperPreview.IsVisible = isSolid;
            WallpaperPreviewImage.IsVisible = !isSolid;

            if (isSolid)
            {
                SolidWallpaperPreview.Background = new SolidColorBrush(app.CurrentSolidWallpaperColor);
                SetSolidWallpaperColourSelection(app.CurrentSolidWallpaperColor);
                return;
            }

            WallpaperChooserLabel.Text = selection == WallpaperSelection.Video
                ? Loc.Get("Choose a video wallpaper")
                : Loc.Get("Choose an image wallpaper");
            WallpaperBrowseButtonText.Text = selection == WallpaperSelection.Video ? Loc.Get("Add video") : Loc.Get("Add wallpaper");
        }

        private static WallpaperSelection ParseWallpaperSelection(string value) =>
            value switch
            {
                "Video" => WallpaperSelection.Video,
                "Solid" => WallpaperSelection.Solid,
                _ => WallpaperSelection.Picture
            };

        private void RefreshWallpaperPreview(string wallpaperUri)
        {
            // Hidden settings pages must not decode images or start video preview workers.
            if (_activeCategory != "Appearance" || TopLevel.GetTopLevel(this) is null || WallpaperPreviewImage is null) return;
            if (!WallpaperFileTypes.IsHeic(wallpaperUri) && string.Equals(_previewWallpaperUri, wallpaperUri, StringComparison.Ordinal)) return;

            _previewWallpaperUri = wallpaperUri;
            _ = RefreshWallpaperPreviewAsync(wallpaperUri, Interlocked.Increment(ref _previewRequestVersion));
        }

        private async Task RefreshWallpaperPreviewAsync(string wallpaperUri, int requestVersion)
        {
            if (WallpaperPreviewImage is null) return;

            try
            {
                var resolvedPath = ResolveWallpaperPath(wallpaperUri);
                Bitmap? preview = null;

                if (File.Exists(resolvedPath) && WallpaperFileTypes.IsImage(resolvedPath))
                {
                    var imagePath = WallpaperFileTypes.IsHeic(resolvedPath)
                        ? await HeicWallpaper.ResolveFrameAsync(resolvedPath, ActualThemeVariant == ThemeVariant.Dark, DateTimeOffset.Now)
                        : resolvedPath;
                    preview = await Task.Run(() => new Bitmap(imagePath));
                }
                else if (WallpaperFileTypes.IsVideo(resolvedPath))
                {
                    preview = await LoadVideoPreviewAsync(resolvedPath);
                }
                else if (wallpaperUri.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                {
                    preview = await Task.Run(() =>
                    {
                        using var stream = AssetLoader.Open(new Uri(wallpaperUri));
                        return new Bitmap(stream);
                    });
                }

                if (requestVersion != Volatile.Read(ref _previewRequestVersion))
                {
                    preview?.Dispose();
                    return;
                }

                var previousPreview = _wallpaperPreview;
                _wallpaperPreview = preview;
                WallpaperPreviewImage.Source = preview;
                RetireWallpaperPreviewAfterReplacement(previousPreview);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Couldn’t create wallpaper preview: {ex.Message}");
            }
        }

        private static string ResolveWallpaperPath(string wallpaperUri) =>
            WallpaperSource.ResolveWallpaperPath(wallpaperUri);

        private static async Task<WriteableBitmap?> LoadVideoPreviewAsync(string videoPath) =>
            File.Exists(videoPath) ? await MpvWallpaperSession.PreviewAsync(videoPath) : null;

        private void RetireWallpaperPreviewAfterReplacement(Bitmap? previous)
        {
            // The old preview may still be retained by Avalonia's compositor for
            // one frame. Dispose it later on the UI thread rather than letting a
            // finalizer touch graphics memory after the Settings window closes.
            if (previous is null) return;

            _retiredWallpaperPreviews.Add(previous);
            _ = DisposePreviewAfterCompositionAsync(previous);
        }

        private async Task DisposePreviewAfterCompositionAsync(Bitmap preview)
        {
            await Task.Delay(160);
            if (!_retiredWallpaperPreviews.Remove(preview)) return;
            preview.Dispose();
        }

    }
}
