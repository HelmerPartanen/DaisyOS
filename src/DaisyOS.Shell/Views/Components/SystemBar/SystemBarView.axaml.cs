using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using DaisyOS.Shell;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Motion;
using DaisyOS.Shell.Services.Shell;

namespace DaisyOS.Shell.Views.Components.SystemBar;

public partial class SystemBarView : UserControl
{
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _statusTimer;
    private readonly ShellSessionState _sessionState = (Application.Current as App)?.SessionState ?? new ShellSessionState();
    private static readonly IReadOnlyDictionary<string, string> PreferredCultureByCountry = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AU"] = "en-AU", ["AT"] = "de-AT", ["BE"] = "nl-BE", ["BR"] = "pt-BR", ["CA"] = "en-CA",
        ["CH"] = "de-CH", ["CN"] = "zh-CN", ["CZ"] = "cs-CZ", ["DE"] = "de-DE", ["DK"] = "da-DK",
        ["EE"] = "et-EE", ["ES"] = "es-ES", ["FI"] = "fi-FI", ["FR"] = "fr-FR", ["GB"] = "en-GB",
        ["GR"] = "el-GR", ["HU"] = "hu-HU", ["IE"] = "en-IE", ["IN"] = "en-IN", ["IT"] = "it-IT",
        ["JP"] = "ja-JP", ["KR"] = "ko-KR", ["LT"] = "lt-LT", ["LV"] = "lv-LV", ["MX"] = "es-MX",
        ["NL"] = "nl-NL", ["NO"] = "nb-NO", ["NZ"] = "en-NZ", ["PL"] = "pl-PL", ["PT"] = "pt-PT",
        ["RO"] = "ro-RO", ["RU"] = "ru-RU", ["SE"] = "sv-SE", ["TR"] = "tr-TR", ["UA"] = "uk-UA",
        ["US"] = "en-US"
    };
    private TextBlock? _clockText;
    private TextBlock? _dateText;
    private string? _lastClockValue;
    private string? _lastDateValue;
    private readonly QuickSettings.QuickSettings _quickSettings;
    private readonly Control _notifications;
    private readonly Control _calendar;
    private readonly AppTrayView _appTray;
    private readonly DaisyIcon _trayChevron;
    public Control AppTrayMaterialFrame => _appTray.MaterialSurface;
    public Border QuickSettingsMaterialFrame => _quickSettings.MaterialSurface;
    public Control NotificationMaterialFrame => ((Notifications.NotificationCenterView)_notifications).MaterialSurface;
    public Control CalendarMaterialFrame => _calendar;

    public event EventHandler? QuickSettingsRequested;
    public event EventHandler? QuickSettingsDismissRequested;
    public event EventHandler? ConsoleModeRequested;
    public event EventHandler<bool>? FlyoutVisibilityChanged;

    public SystemBarView()
    {
        InitializeComponent();
        _quickSettings = this.FindControl<QuickSettings.QuickSettings>("QuickSettingsPanel")!;
        _notifications = this.FindControl<Control>("NotificationDrawer")!;
        _calendar = this.FindControl<Control>("CalendarPanel")!;
        _appTray = this.FindControl<AppTrayView>("AppTrayPanel")!;
        _trayChevron = this.FindControl<DaisyIcon>("AppTrayChevron")!;
        _quickSettings.DismissRequested += (_, _) => QuickSettingsDismissRequested?.Invoke(this, EventArgs.Empty);
        _quickSettings.ConsoleModeRequested += (_, _) => ConsoleModeRequested?.Invoke(this, EventArgs.Empty);
        _quickSettings.NetworkStatusChanged += UpdateNetworkPresentation;
        _quickSettings.NetworkStatusUnavailable += ShowOfflineNetworkPresentation;

        // Poll the wall clock frequently rather than advancing from an arbitrary
        // 30-second startup phase. The displayed minute now changes within one
        // second of the real minute boundary, even after suspend or UI stalls.
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _statusTimer.Tick += (_, _) => QueueStatusRefresh();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public bool IsAppTrayVisible => GuiMotion.IsOpen(_appTray);

    public bool IsQuickSettingsVisible => GuiMotion.IsOpen(_quickSettings);
    public bool IsNotificationPanelVisible => GuiMotion.IsOpen(_notifications);
    public bool IsCalendarPanelVisible => GuiMotion.IsOpen(_calendar);

    public Control DetachFlyoutsForNativeOverlay()
    {
        var overlay = new BottomChromeOverlayHost();
        if (Content is not Grid root)
            return overlay;

        // Keep the cards on a viewport that ends three pixels above the
        // visible bottom chrome. Their transforms are clipped at this fixed
        // edge, leaving the blur and card opacity untouched. Context menus
        // added to the outer host remain outside this clipping viewport.

        foreach (var name in new[] { "QuickSettingsPanel", "NotificationDrawer", "CalendarPanel", "AppTrayPanel" })
        {
            if (this.FindControl<Control>(name) is { } panel)
            {
                root.Children.Remove(panel);
                // The viewport itself ends at the shared bar clearance, so
                // cards rest flush against that clip boundary.
                panel.Margin = new Thickness(
                    0,
                    0,
                    ShellSurfacePlan.OverlayEdgeSafeInset,
                    0);
                overlay.Viewport.Children.Add(panel);
            }
        }

        return overlay;
    }

    private void RaiseFlyoutVisibilityChanged() =>
        FlyoutVisibilityChanged?.Invoke(this, IsQuickSettingsVisible || IsNotificationPanelVisible || IsCalendarPanelVisible || IsAppTrayVisible);

    /// <summary>Shows Quick Settings as an overlay within the shell window.</summary>
    public void ShowQuickSettingsPanel()
    {
        HideAppTrayPanel(immediate: true);
        _quickSettings.ShowPanel();
        HideNotificationPanel(immediate: true);
        HideCalendarPanel(immediate: true);
        _ = GuiMotion.SetVisibleAsync(_quickSettings, _quickSettings.MaterialSurface, true, RaiseFlyoutVisibilityChanged,
            offset: GuiMotion.ShellSlideDistance, durationOverride: GuiMotion.ShellSlideDuration(this, true));
        QueueStatusRefresh();
        SetFlyoutButtonActive("QuickSettingsButton", true);
        RaiseFlyoutVisibilityChanged();
    }

    /// <summary>Hides Quick Settings overlay.</summary>
    public void HideQuickSettingsPanel(bool immediate = false)
    {
        _ = GuiMotion.SetVisibleAsync(_quickSettings, _quickSettings.MaterialSurface, false, RaiseFlyoutVisibilityChanged, immediate,
            GuiMotion.ShellSlideDistance, GuiMotion.ShellSlideDuration(this, false), fadeOnExit: false);
        SetFlyoutButtonActive("QuickSettingsButton", false);
        RaiseFlyoutVisibilityChanged();
    }

    public void ShowNotificationPanel()
    {
        HideAppTrayPanel(immediate: true);
        _ = GuiMotion.SetVisibleAsync(_notifications, NotificationMaterialFrame, true, RaiseFlyoutVisibilityChanged,
            offset: GuiMotion.ShellSlideDistance, durationOverride: GuiMotion.ShellSlideDuration(this, true));
        HideQuickSettingsPanel(immediate: true);
        HideCalendarPanel(immediate: true);
        SetFlyoutButtonActive("NotificationsButton", true);
        RaiseFlyoutVisibilityChanged();
    }

    public void HideNotificationPanel(bool immediate = false)
    {
        _ = GuiMotion.SetVisibleAsync(_notifications, NotificationMaterialFrame, false, RaiseFlyoutVisibilityChanged, immediate,
            GuiMotion.ShellSlideDistance, GuiMotion.ShellSlideDuration(this, false), fadeOnExit: false);
        SetFlyoutButtonActive("NotificationsButton", false);
        RaiseFlyoutVisibilityChanged();
    }

    public void ShowCalendarPanel()
    {
        HideAppTrayPanel(immediate: true);
        _ = GuiMotion.SetVisibleAsync(_calendar, _calendar, true, RaiseFlyoutVisibilityChanged,
            offset: GuiMotion.ShellSlideDistance, durationOverride: GuiMotion.ShellSlideDuration(this, true));
        HideQuickSettingsPanel(immediate: true);
        HideNotificationPanel(immediate: true);
        SetFlyoutButtonActive("CalendarButton", true);
        RaiseFlyoutVisibilityChanged();
    }

    public void HideCalendarPanel(bool immediate = false)
    {
        _ = GuiMotion.SetVisibleAsync(_calendar, _calendar, false, RaiseFlyoutVisibilityChanged, immediate,
            GuiMotion.ShellSlideDistance, GuiMotion.ShellSlideDuration(this, false), fadeOnExit: false);
        SetFlyoutButtonActive("CalendarButton", false);
        RaiseFlyoutVisibilityChanged();
    }

    public void ShowAppTrayPanel()
    {
        HideQuickSettingsPanel(immediate: true);
        HideNotificationPanel(immediate: true);
        HideCalendarPanel(immediate: true);
        _ = GuiMotion.SetVisibleAsync(_appTray, _appTray.MaterialSurface, true, RaiseFlyoutVisibilityChanged,
            offset: GuiMotion.ShellSlideDistance, durationOverride: GuiMotion.ShellSlideDuration(this, true));
        ConfigureTrayTransition();
        _trayChevron.RenderTransform = TransformOperations.Parse("rotate(180deg)");
        SetFlyoutButtonActive("AppTrayButton", true);
        RaiseFlyoutVisibilityChanged();
    }

    public void HideAppTrayPanel(bool immediate = false)
    {
        _ = GuiMotion.SetVisibleAsync(_appTray, _appTray.MaterialSurface, false, RaiseFlyoutVisibilityChanged, immediate,
            GuiMotion.ShellSlideDistance, GuiMotion.ShellSlideDuration(this, false), fadeOnExit: false);
        ConfigureTrayTransition();
        _trayChevron.RenderTransform = TransformOperations.Parse("rotate(0deg)");
        SetFlyoutButtonActive("AppTrayButton", false);
        RaiseFlyoutVisibilityChanged();
    }

    private void ConfigureTrayTransition()
    {
        var duration = this.TryFindResource("MotionDurationBase", out var resource) && resource is TimeSpan configured
            ? configured : TimeSpan.FromMilliseconds(160);
        if (duration == TimeSpan.Zero)
        {
            _trayChevron.Transitions = null;
            return;
        }
        if (_trayChevron.Transitions?.FirstOrDefault() is Avalonia.Animation.TransformOperationsTransition current && current.Duration == duration) return;
        _trayChevron.Transitions = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = duration,
                Easing = new Avalonia.Animation.Easings.CubicEaseOut()
            }
        };
    }

    private void OnAppTrayRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (IsAppTrayVisible) HideAppTrayPanel();
        else ShowAppTrayPanel();
    }

    private void OnNotificationsRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (IsNotificationPanelVisible) HideNotificationPanel();
        else ShowNotificationPanel();
    }

    private void OnCalendarRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (IsCalendarPanelVisible) HideCalendarPanel();
        else ShowCalendarPanel();
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _clockText ??= this.FindControl<TextBlock>("ClockText");
        _dateText ??= this.FindControl<TextBlock>("DateText");
        UpdateClock();
        if (Application.Current is App trayApp) _appTray.Connect(trayApp.AppTray);
        _clockTimer.Start();
        _statusTimer.Start();
        if (Application.Current is App app)
        {
            app.Notifications.UnreadCountChanged += OnUnreadNotificationsCountChanged;
            app.SessionState.Changed += OnSessionStateChanged;
            UpdateNotificationBadge(app.Notifications.UnreadCount);
        }
        // Fast adapter state preloads after the shell is interactive so
        // opening Quick Settings never waits behind a Wi-Fi scan or paired-device enumeration.
        Dispatcher.UIThread.Post(QueueStatusRefresh, DispatcherPriority.Background);
    }

    private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _appTray.Disconnect();
        _clockTimer.Stop();
        _statusTimer.Stop();
        if (Application.Current is App app)
        {
            app.Notifications.UnreadCountChanged -= OnUnreadNotificationsCountChanged;
            app.SessionState.Changed -= OnSessionStateChanged;
        }
    }

    private void OnUnreadNotificationsCountChanged(object? sender, int count)
    {
        Dispatcher.UIThread.Post(() => UpdateNotificationBadge(count));
    }

    private void UpdateNotificationBadge(int count)
    {
        if (this.FindControl<Border>("NotificationBadge") is { } badge)
        {
            badge.IsVisible = count > 0;
        }
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        // Clock-format and system-bar date changes are shell preferences, so
        // acknowledge them immediately instead of waiting for the next tick.
        Dispatcher.UIThread.Post(UpdateClock);
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var culture = ResolveDateTimeCulture();
        var clockPreference = (Application.Current as App)?.GetPreference("time.clock-format", "System default")
            ?? "System default";
        var showDate = (Application.Current as App)?.SessionState.ShowDateInSystemBar ?? false;
        var timePattern = ResolveTimePattern(culture, clockPreference);
        // Keep the date compact, but preserve the user's regional ordering,
        // separator, weekday language, and local timezone. For example, this
        // becomes "su 23.8 03.36" in Finnish and "Sun 8/23 3:36 AM" in US English.
        var clockValue = now.ToString(timePattern, culture);
        if (_clockText is not null && !string.Equals(clockValue, _lastClockValue, StringComparison.Ordinal))
        {
            _lastClockValue = clockValue;
            _clockText.Text = clockValue;
        }

        if (_dateText is not null)
        {
            _dateText.IsVisible = showDate;
            // The regional culture controls numeric order and separators. Keep
            // the weekday in the shell language so an English DaisyOS session
            // in Helsinki reads "Sun 23.8", not an unexpectedly translated
            // label; a Finnish shell naturally renders "su 23.8" instead.
            var dateValue = $"{now.ToString("ddd", CultureInfo.CurrentCulture)} {now.ToString(GetCompactDatePattern(culture.DateTimeFormat.ShortDatePattern), culture)}";
            if (!string.Equals(dateValue, _lastDateValue, StringComparison.Ordinal))
            {
                _lastDateValue = dateValue;
                _dateText.Text = dateValue;
            }
        }
    }

    private static string ResolveTimePattern(CultureInfo culture, string preference) => preference switch
    {
        "12-hour" => "h:mm tt",
        "24-hour" => culture.DateTimeFormat.ShortTimePattern
            .Replace('h', 'H')
            .Replace('K', 'H')
            .Replace(" tt", string.Empty, StringComparison.Ordinal)
            .Replace("t", string.Empty, StringComparison.Ordinal)
            .Trim(),
        _ => culture.DateTimeFormat.ShortTimePattern
    };

    private static string GetCompactDatePattern(string shortDatePattern)
    {
        var yearStart = shortDatePattern.IndexOfAny(['y', 'Y']);
        if (yearStart < 0)
        {
            return shortDatePattern;
        }

        var yearEnd = yearStart;
        while (yearEnd < shortDatePattern.Length && shortDatePattern[yearEnd] is 'y' or 'Y')
        {
            yearEnd++;
        }

        // A leading year (for example yyyy-MM-dd) loses the following
        // separator; a trailing/middle year loses the separator before it.
        if (yearStart == 0)
        {
            while (yearEnd < shortDatePattern.Length && IsDateSeparator(shortDatePattern[yearEnd]))
            {
                yearEnd++;
            }

            return shortDatePattern[yearEnd..].Trim();
        }

        var removalStart = yearStart;
        while (removalStart > 0 && IsDateSeparator(shortDatePattern[removalStart - 1]))
        {
            removalStart--;
        }

        return shortDatePattern.Remove(removalStart, yearEnd - removalStart).Trim();
    }

    private static bool IsDateSeparator(char value) =>
        value is '/' or '.' or '-' || char.IsWhiteSpace(value);

    private static CultureInfo ResolveDateTimeCulture()
    {
        var systemCulture = CultureInfo.CurrentCulture;
        var timeLocale = Environment.GetEnvironmentVariable("LC_TIME");
        if (!IsGenericTimeLocale(timeLocale))
        {
            return systemCulture;
        }

        var countryCode = TryGetTimeZoneCountryCode(TimeZoneInfo.Local.Id);
        if (countryCode is null || !PreferredCultureByCountry.TryGetValue(countryCode, out var cultureName))
        {
            return systemCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            return systemCulture;
        }
    }

    private static bool IsGenericTimeLocale(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value, "C", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "C.UTF-8", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "POSIX", StringComparison.OrdinalIgnoreCase);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> TimeZoneCountries = new(LoadTimeZoneCountries);

    private static string? TryGetTimeZoneCountryCode(string timeZoneId) =>
        TimeZoneCountries.Value.TryGetValue(timeZoneId, out var country) ? country : null;

    private static IReadOnlyDictionary<string, string> LoadTimeZoneCountries()
    {
        var countries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in new[] { "/usr/share/zoneinfo/zone1970.tab", "/usr/share/zoneinfo/zone.tab" })
        {
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (line.StartsWith('#')) continue;
                    var columns = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (columns.Length < 3) continue;
                    var country = columns[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                    if (country is not null) countries.TryAdd(columns[2], country);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return countries;
    }

    private void ShowOfflineNetworkPresentation()
    {
        if (this.FindControl<WifiSignalIcon>("SystemBarWifiIcon") is { } wifiIcon) wifiIcon.IsVisible = false;
        if (this.FindControl<DaisyIcon>("SystemBarEthernetIcon") is { } ethernetIcon) ethernetIcon.IsVisible = false;
        if (this.FindControl<DaisyIcon>("SystemBarOfflineIcon") is { } offlineIcon) offlineIcon.IsVisible = true;
    }

    private void OnQuickSettingsRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        QuickSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnNotificationsFlyoutOpened(object? sender, EventArgs e) =>
        SetFlyoutButtonActive("NotificationsButton", true);

    private void OnNotificationsFlyoutClosed(object? sender, EventArgs e) =>
        SetFlyoutButtonActive("NotificationsButton", false);

    private void SetFlyoutButtonActive(string buttonName, bool isActive) =>
        this.FindControl<Button>(buttonName)?.Classes.Set("ShellButtonActive", isActive);

    private void UpdateNetworkPresentation(NetworkStatus network, WirelessRadioStatus wifi, BluetoothStatus bluetooth)
    {
        var hasInternet = network.IsConnected && network.Connectivity == NetworkConnectivity.Full;
        var wifiIsOnline = hasInternet && network.ConnectionKind == NetworkConnectionKind.WiFi;
        var ethernetIsOnline = hasInternet && network.ConnectionKind == NetworkConnectionKind.Ethernet;
        var ethernetIsActive = network.IsConnected && network.ConnectionKind == NetworkConnectionKind.Ethernet;
        var wifiReceiverMissing = !wifi.IsAvailable;
        var bluetoothReceiverMissing = !bluetooth.IsAvailable;

        if (this.FindControl<WifiSignalIcon>("SystemBarWifiIcon") is { } wifiIcon)
        {
            wifiIcon.IsVisible = wifiIsOnline;
            wifiIcon.SignalPercent = network.SignalPercent ?? 0;
            wifiIcon.ActiveBrush = this.FindResource("ContentSecondaryBrush") as IBrush ?? Brushes.White;
            wifiIcon.InactiveBrush = this.FindResource("ContentTertiaryBrush") as IBrush ?? Brushes.Gray;
            ToolTip.SetTip(wifiIcon, network.DisplayName);
        }
        if (this.FindControl<DaisyIcon>("SystemBarEthernetIcon") is { } ethernetIcon)
        {
            ethernetIcon.IsVisible = ethernetIsOnline;
            ToolTip.SetTip(ethernetIcon, network.DisplayName);
        }
        if (this.FindControl<DaisyIcon>("SystemBarOfflineIcon") is { } offlineIcon)
        {
            offlineIcon.IsVisible = !wifiIsOnline && !ethernetIsOnline;
            ToolTip.SetTip(offlineIcon, Loc.Get("No internet connection"));
        }
        if (this.FindControl<DaisyIcon>("SystemBarBluetoothIcon") is { } bluetoothIcon)
        {
            bluetoothIcon.IsVisible = !bluetoothReceiverMissing;
            bluetoothIcon.Icon = bluetooth.IsEnabled ? "bluetooth" : "bluetooth-slash";
            ToolTip.SetTip(bluetoothIcon, bluetooth.Detail);
        }

    }

    private void QueueStatusRefresh() => _quickSettings.RefreshStatus();
}
