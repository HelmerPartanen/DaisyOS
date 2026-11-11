using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Skia;
using Avalonia.Wayland;
using Avalonia.Wayland.Server.LayerShell;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Rendering.Composition;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.Shell.Services.Wallpaper;
using DaisyOS.Shell.Services.Wallpaper.VideoWallpaper;
using DaisyOS.Shell.Apps.Files.Picker;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Theming;
using DaisyOS.Shell.Views;
using System;
using System.IO;

using DaisyOS.Shell.Apps.Calculator;
using DaisyOS.Shell.Views.Windows;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.Apps.Calendar;
using DaisyOS.Shell.Apps.UIKit;
using DaisyOS.Shell.Services.Windows;
using DaisyOS.Shell.Services.Notifications;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.Services.Diagnostics;
using DaisyOS.System.Power;
using DaisyOS.Shell.Services.Shell;
using DaisyOS.System.Appearance;
using DaisyOS.System.Processes;
using DaisyOS.System.Networking.LocalSend;
using DaisyOS.System.Media;
using Avalonia.Threading;
using System.Threading.Tasks;
using DaisyOS.Shell.Apps.LocalSend;
using DaisyOS.Shell.Services.Screenshot;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Services.Idle;
using DaisyOS.System.Input;

namespace DaisyOS.Shell;

public partial class App : Application
{
    // Native KWin toplevels must be destroyed on close. Reusing a hidden
    // Wayland Window can leave a taskbar entry without a mapped surface.
    private readonly DaisyOS.Shell.Services.Windows.LightweightWindowCache _lightweightWindows =
        new(retainWindows: !IsNativeShellSession);
    private readonly DaisyOS.Shell.Services.Diagnostics.RecentAppPrefetcher _recentPrefetch;

    private IWallpaperService? _wallpaperService;
    private readonly AppearanceSettingsStore _appearanceSettingsStore = new();
    private AppearanceSettings _appearanceSettings;
    private readonly Action<App, IClassicDesktopStyleApplicationLifetime>? _applicationStartup;
    private DateTime _applicationAppearanceWriteTime = DateTime.MinValue;
    private int _applicationAppearanceMinute = -1;
    public Action<IEnumerable<string>?>? ShareRequested { get; set; }
    private readonly AutomaticThemeScheduler _automaticThemeScheduler;
    private readonly WindowDecorationThemeService _windowDecorationThemeService = new();
    private readonly LinuxSystemThemeService _systemThemeService = new(new SafeCommandRunner());
    private readonly LinuxPersonalizationService _personalizationService = new(new SafeCommandRunner());
    private CancellationTokenSource? _systemThemeSyncCancellation;
    private CalendarWindow? _calendarWindow;
    private CalculatorWindow? _calculatorWindow;
    private DaisyOS.Shell.Apps.AppStore.AppStoreWindow? _appStoreWindow;
    private DaisyOS.Shell.Apps.AppStore.AppStoreViewModel? _appStoreSession;
    private DaisyOS.Shell.Apps.System.Diagnostics.TaskManagerWindow? _taskManagerWindow;
    private UIKitWindow? _uiKitWindow;
    private DaisyOS.Shell.Views.Components.Clipboard.ClipboardHistoryWindow? _clipboardHistoryWindow;
    // LocalSend is shell UI in a native session. Its persistent content is
    // mounted once on a layer-shell overlay, not created as an xdg app window
    // for each share action.
    private LocalSendWindow? _localSendWindow;
    private Window? _nativeLocalSendSurface;
    private Window? _nativeBottomEdgeSensorSurface;
    private Window? _windowedLocalSendHost;
    public DaisyOS.Shell.Services.Tray.StatusNotifierService AppTray { get; } = new();

    private DaisyOS.Shell.Services.Notifications.FreedesktopNotificationDaemonService? _notificationDaemonService;
    // LocalSend brings an optional HTTP/TLS host. Keep it out of the shell's
    // construction path so a missing optional runtime can never prevent the
    // desktop from starting.
    private LocalSendService? _localSendService;
    private Task? _localSendStartTask;
    private bool _localSendUnavailable;
    private ShellView? _shellView;
    private readonly List<Window> _nativeShellSurfaces = [];
    private DaisyOS.Shell.Views.Components.Gaming.GamingOverlay? _gamingOverlay;
    private Window? _gamingOverlaySurface;
    private Views.Components.Volume.VolumeIndicator? _volumeIndicator;
    private Window? _volumeIndicatorSurface;
    private Services.VolumeKeyController? _volumeKeys;
    private int _gameTelemetryVisible;
    private int _gameTelemetryWindowPid;
    private ILayerShellWindowBackend? _layerShellBackend;
    private Window? _nativePanelSurface;
    private Window? _nativeLauncherSurface;
    private Views.Components.Launcher.LauncherHint? _launcherHint;
    private Window? _launcherHintSurface;
    private DispatcherTimer? _launcherHintTimer;
    private readonly LauncherGesturePolicy _launcherGesture = new();
    private string? _launcherReturnWindowId;
    private bool _launcherLaunchedApp;
    private Window? _nativeSystemFlyoutSurface;
    private DesktopContextMenuOverlay? _nativeDesktopMenus;
    private Window? _nativeScreenshotSurface;
    private Window? _nativeScreenshotBackdropSurface;
    private KWinBlur? _launcherBlur;
    private readonly NativeAppWindowTracker _nativeWindowTracker = new();
    private ICompositorWindowService? _compositorWindows;
    private readonly DaisyScreenshotService _screenshotService = new();
    private bool _isScreenshotOpening;
    private readonly HashSet<string> _reportedUnresponsiveWindows = new(StringComparer.Ordinal);
    private int _handlingDispatcherFailure;
    private bool _externalDesktopOccluded;
    private readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _recentTaskbarActivations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<DaisyOS.System.Processes.LinuxAppLauncherService> _sharedLauncherService = new(() => new DaisyOS.System.Processes.LinuxAppLauncherService());

    /// <summary>
    /// Builds a multi-key index from the cached app catalog for O(1) lookup
    /// instead of the previous O(N×6) linear scan per taskbar launch.
    /// </summary>
    internal static Dictionary<string, DaisyOS.Core.Models.AppEntry> BuildAppLookupIndex(
        global::System.Collections.Generic.IReadOnlyList<DaisyOS.Core.Models.AppEntry> apps)
    {
        var index = new Dictionary<string, DaisyOS.Core.Models.AppEntry>(
            apps.Count * 4, StringComparer.OrdinalIgnoreCase);

        foreach (var app in apps)
        {
            // By full ID (e.g. "firefox.desktop")
            index.TryAdd(app.Id, app);

            // By ID without .desktop suffix (e.g. "firefox")
            if (app.Id.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
                index.TryAdd(app.Id[..^8], app);

            // The taskbar uses short DaisyOS IDs; translated display names must
            // never be needed to resolve Settings, Files, or Notes.
            var canonicalId = app.Id.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? app.Id[..^8] : app.Id;
            if (canonicalId.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase))
                index[canonicalId["org.daisyos.".Length..]] = app;

            // By display name (e.g. "Firefox")
            if (!string.IsNullOrWhiteSpace(app.Name))
                index.TryAdd(app.Name, app);

            // By executable basename (e.g. "firefox" from "/usr/bin/firefox")
            if (!string.IsNullOrWhiteSpace(app.LaunchTarget))
            {
                var exeBaseName = Path.GetFileNameWithoutExtension(app.LaunchTarget);
                if (!string.IsNullOrWhiteSpace(exeBaseName))
                    index.TryAdd(exeBaseName, app);
            }

            // By icon name
            if (!string.IsNullOrWhiteSpace(app.Icon))
                index.TryAdd(app.Icon, app);
        }

        return index;
    }

    private static readonly bool _isNativeShellSession =
        string.Equals(Environment.GetEnvironmentVariable("DAISYOS_SHELL_SESSION"), "1", StringComparison.Ordinal);
    public static bool IsNativeShellSession => _isNativeShellSession;

    public ShellSessionState SessionState { get; } = new();
    public ShellFeedbackService Feedback { get; } = new();
    public NotificationService Notifications { get; } = new();
    public AirPodsCoordinator? AirPods { get; private set; }
    private BluetoothConnectionMonitor? _bluetoothConnectionMonitor;
    public NativeAppWindowTracker WindowTracker => _nativeWindowTracker;
    // The persisted preference must be available before the first native
    // background surface maps. A native shell restart constructs that surface
    // before its Opened callback, so falling back to the hard-coded default
    // here can briefly load a different source and leave a flat background.
    public string CurrentWallpaperUri => _wallpaperService?.CurrentWallpaperUri
                                         ?? GetCurrentWallpaperSource();
    public bool UsesSolidWallpaper => _appearanceSettings.UseSolidWallpaper
        || WallpaperSource.TryGetSolidColor(GetSafeWallpaperUri(_appearanceSettings.WallpaperUri), out _);
    public Color CurrentSolidWallpaperColor => GetSolidWallpaperColor();
    public ThemeMode CurrentThemeMode => _appearanceSettings.ThemeMode;
    public AccentColor CurrentAccentColor => _appearanceSettings.AccentColor;
    public bool HighContrastEnabled => _appearanceSettings.HighContrast;
    public bool ReduceMotionEnabled => _appearanceSettings.ReduceMotion;
    public int CurrentTaskbarSize => Math.Clamp(_appearanceSettings.TaskbarSize, 0, 2);

    public bool TaskbarAutoHideEnabled => _appearanceSettings.TaskbarAutoHide;
    public bool SystemBarAutoHideEnabled => _appearanceSettings.SystemBarAutoHide;
    public bool MediaWidgetAutoHideEnabled => _appearanceSettings.MediaWidgetAutoHide;
    public string? CurrentCursorThemeId => _appearanceSettings.CursorThemeId;
    public int CurrentCursorSize => _appearanceSettings.CursorSize;
    public string CurrentLanguageTag => SystemLanguages.Resolve(_appearanceSettings.LanguageTag).Tag;
    public bool HasProfilePicture => _profilePicture is not null || (!string.IsNullOrWhiteSpace(_appearanceSettings.ProfilePictureUri) && File.Exists(_appearanceSettings.ProfilePictureUri));

    public string CurrentSystemFontFamily => _appearanceSettings.SystemFontFamily;
    public int CurrentTextScalePercent => _appearanceSettings.TextScalePercent;
    public LinuxPersonalizationService Personalization => _personalizationService;
    public ShellView? ShellView => _shellView;
    /// <summary>True when KWin reports that the active external app fully covers the desktop workspace.</summary>
    public bool IsDesktopOccludedByExternalApp => _externalDesktopOccluded;
    private bool _hasAnyOpenExternalWindow;
    /// <summary>True when any compositor-tracked external application window is open and not minimized on the current workspace.</summary>
    public bool HasAnyOpenExternalWindow =>
        _hasAnyOpenExternalWindow ||
        (_compositorWindows?.Windows.Any(window => !window.IsMinimized && window.IsOnCurrentWorkspace) ?? false);
    /// <summary>True when the lock screen is active and occluding the desktop workspace.</summary>
    private DaisyOS.System.Continuity.ContinuitySessionBridge? _continuitySession;
    public bool IsLockScreenActive => _lockScreenWindow is not null;
    /// <summary>LocalSend only accepts inbound transfers while the user has explicitly enabled this temporary window.</summary>
    public bool IsLocalSendReceivingEnabled => _localSendService?.IsReceivingEnabled == true;

    public string GetPreference(string key, string fallback = "") => SessionState.GetPreference(key, fallback);

    public event EventHandler<(string Key, string Value)>? PreferenceChanged;

    public void SetPreference(string key, string value)
    {
        SessionState.SetPreference(key, value);
        PreferenceChanged?.Invoke(this, (key, value));
    }

    public void ToggleGamingOverlay() => HandleShellAction("gaming-overlay");

    public void SetDoNotDisturb(bool enabled)
    {
        SessionState.DoNotDisturb = enabled;
        Notifications.SetDoNotDisturb(enabled);
    }

    public event EventHandler<string>? WallpaperChanged;
    public event EventHandler? DesktopOcclusionChanged;
    /// <summary>Raised after the compositor publishes a fresh application-window snapshot.</summary>
    public event EventHandler? CompositorWindowsChanged;
    public event EventHandler<bool>? LauncherVisibilityChanged;
    public event EventHandler? AppearanceChanged;
    public event EventHandler<bool>? LocalSendReceivingChanged;

    private readonly global::System.Collections.Generic.List<KWinBlur> _blurEffects = new();

    public App() : this(null) { }

    public App(Action<App, IClassicDesktopStyleApplicationLifetime>? applicationStartup)
    {
        _recentPrefetch = new(enabled: applicationStartup is null &&
            Environment.GetEnvironmentVariable("DAISYOS_PREFETCH") == "1");
        _applicationStartup = applicationStartup;
        _appearanceSettings = _appearanceSettingsStore.Load();
        _automaticThemeScheduler = new AutomaticThemeScheduler(theme => _ = ApplyThemeVariantAsync(theme));
        Notifications.SetDoNotDisturb(SessionState.DoNotDisturb);
        Notifications.NotificationActionTriggered += OnNotificationActionTriggered;
        if (_applicationStartup is null)
            AppearanceChanged += (_, _) => _ = ExportWindowDecorationThemeAsync();
        _nativeWindowTracker.StateChanged += (_, args) =>
            _shellView?.Taskbar.SetAppWindowState(args.AppId, args.State);
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnProcessUnhandledException;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyLanguageResources();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (_applicationStartup is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime application)
        {
            ApplyInitialApplicationAppearance();
            _applicationStartup(this, application);
            base.OnFrameworkInitializationCompleted();
            return;
        }
        if (_appearanceSettings.ThemeMode == ThemeMode.Automatic)
        {
            _automaticThemeScheduler.Start();
        }
        else
        {
            RequestedThemeVariant = ToThemeVariant(_appearanceSettings.ThemeMode);
            SynchronizeSystemTheme(RequestedThemeVariant);
        }

        // Fire the Wayland capability probe in the background immediately — it
        // forks wayland-info and reads its output. Starting it here lets it run
        // concurrently with theme/decoration setup instead of blocking the UI thread
        // cold inside StartNativeShellSession.
        var layerShellProbeTask = IsNativeShellSession
            ? LayerShellCapability.ProbeAsync()
            : Task.FromResult(false);

        ApplyAccessibilityPreferences();
        ApplyAccentPreference();
        ApplyTypographyResources();
        _ = Task.Run(() => ExportWindowDecorationThemeAsync());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var traceLifetime = new CancellationTokenSource();
            if (IsNativeShellSession)
            {
                _continuitySession = new DaisyOS.System.Continuity.ContinuitySessionBridge();
                _ = _continuitySession.RunAsync(traceLifetime.Token);
                _ = new ContinuityPairingService().RunAsync(traceLifetime.Token);
            }
            StartApplicationSettingsService(traceLifetime.Token);
            _ = DaisyOS.Shell.Services.Diagnostics.ResponsivenessTrace.RunAsync(traceLifetime.Token);
            _ = Task.Run(() => ShellResourceTrace.RunAsync(traceLifetime.Token));
            _ = ShellMemoryPressureMonitor.RunAsync(traceLifetime.Token, ReclaimHiddenShellCaches);
            var gamingAutomation = new GamingModeAutomation(SessionState, () =>
                SessionState.GetPreference(GamingModeAutomation.AutomaticPreferenceKey, "true") != "false",
                telemetryVisible: () => Volatile.Read(ref _gameTelemetryVisible) != 0,
                telemetryWindowPid: () => Volatile.Read(ref _gameTelemetryWindowPid));
            _recentPrefetch.SetSuspended(SessionState.GamingMode);
            SessionState.Changed += OnGamingPrefetchPolicyChanged;
            gamingAutomation.Start();
            desktop.Exit += (_, _) =>
            {
                StopNativeDisplayTracking();
                _continuitySession?.Dispose();
                traceLifetime.Cancel();
                gamingAutomation.Dispose();
                _bluetoothConnectionMonitor?.Dispose();
                AirPods?.Dispose();
                _ = AppTray.DisposeAsync();
                SessionState.Changed -= OnGamingPrefetchPolicyChanged;
                _launcherHintTimer?.Stop();
                _recentPrefetch.Dispose();
                _memoryTrimmer?.Dispose();
                _powerIdleService?.Dispose();
                _lightweightWindows.Dispose();
                foreach (var blur in _blurEffects) blur.Dispose();
                _blurEffects.Clear();
            };
            StartNotificationDaemonService();
            if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("DAISYOS_SHELL_DEMO") != "1")
            {
                // KWin owns input delivery for the shell and every system app.
                // Applying the default at the compositor makes launcher and app
                // ScrollViewers receive the same scaled trackpad deltas.
                _ = new LinuxInputHardwareService().EnsureDefaultTrackpadScrollSpeedAsync();
                _bluetoothConnectionMonitor = new BluetoothConnectionMonitor(Notifications);
                _bluetoothConnectionMonitor.Start();
                AirPods = new AirPodsCoordinator(this);
                AirPods.Start();
            }
            if (IsNativeShellSession) _ = AppTray.StartAsync();
            StartLocalSendService();
            StartIdleLockService();
            if (IsNativeShellSession)
            {
                StartNativeShellSession(desktop, layerShellProbeTask);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            mainWindow.Opened += (_, _) =>
            {
                ConfigureShellOverlays(mainWindow.ShellContent);
                StartCompositorBridge(mainWindow.ShellContent);
                var startupApp = desktop.Args?
                    .FirstOrDefault(argument => argument.StartsWith("--open-app=", StringComparison.OrdinalIgnoreCase));
                if (startupApp is not null)
                {
                    var appId = startupApp["--open-app=".Length..];
                    // Hide first so the app is an independent toplevel rather
                    // than an owned child of the prototype shell window.
                    mainWindow.Hide();
                    if (!TryLaunchSystemApp(appId))
                    {
                        mainWindow.Show();
                    }
                }
            };

            InitializeWallpaperState();
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal string AppearancePreferencesPath => _appearanceSettingsStore.PreferencesPath;

    private void ApplyInitialApplicationAppearance()
    {
        _applicationAppearanceWriteTime = File.GetLastWriteTimeUtc(_appearanceSettingsStore.PreferencesPath);
        _applicationAppearanceMinute = DateTime.Now.Minute;
        RequestedThemeVariant = _appearanceSettings.ThemeMode == ThemeMode.Automatic
            ? (SolarSchedule.ForLocalTimeZone(DateTimeOffset.Now).IsDaylight(DateTimeOffset.Now) ? ThemeVariant.Light : ThemeVariant.Dark)
            : ToThemeVariant(_appearanceSettings.ThemeMode);
        ApplyAccessibilityPreferences();
        ApplyAccentPreference();
        ApplyTypographyResources();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies shared appearance in an app process without starting desktop services.</summary>
    public void ReloadApplicationAppearance(bool force = false)
    {
        var writeTime = File.GetLastWriteTimeUtc(_appearanceSettingsStore.PreferencesPath);
        var minute = DateTime.Now.Minute;
        if (!force && writeTime == _applicationAppearanceWriteTime && minute == _applicationAppearanceMinute) return;
        var fileChanged = writeTime != _applicationAppearanceWriteTime;
        _applicationAppearanceWriteTime = writeTime;
        _applicationAppearanceMinute = minute;

        if (!force && !fileChanged && _appearanceSettings.ThemeMode != ThemeMode.Automatic)
        {
            return;
        }

        var previous = _appearanceSettings;
        var previousThemeVariant = RequestedThemeVariant;
        var loaded = (force || fileChanged) ? _appearanceSettingsStore.Load() : previous;
        _appearanceSettings = loaded;

        var targetThemeVariant = _appearanceSettings.ThemeMode == ThemeMode.Automatic
            ? (SolarSchedule.ForLocalTimeZone(DateTimeOffset.Now).IsDaylight(DateTimeOffset.Now) ? ThemeVariant.Light : ThemeVariant.Dark)
            : ToThemeVariant(_appearanceSettings.ThemeMode);
        var themeChanged = RequestedThemeVariant != targetThemeVariant;
        if (themeChanged)
        {
            RequestedThemeVariant = targetThemeVariant;
        }

        var accessibilityChanged = themeChanged
            || previous.HighContrast != loaded.HighContrast
            || previous.ReduceMotion != loaded.ReduceMotion;
        var accentChanged = themeChanged
            || previous.HighContrast != loaded.HighContrast
            || previous.AccentColor != loaded.AccentColor;
        var languageChanged = !string.Equals(
            SystemLanguages.Resolve(previous.LanguageTag).Tag,
            SystemLanguages.Resolve(loaded.LanguageTag).Tag,
            StringComparison.OrdinalIgnoreCase);
        var typographyChanged = !string.Equals(previous.SystemFontFamily, loaded.SystemFontFamily, StringComparison.Ordinal)
            || previous.TextScalePercent != loaded.TextScalePercent;
        var profilePictureChanged = !string.Equals(previous.ProfilePictureUri, loaded.ProfilePictureUri, StringComparison.Ordinal);
        var taskbarSizeChanged = previous.TaskbarSize != loaded.TaskbarSize;
        var wallpaperChanged = previous.UseSolidWallpaper != loaded.UseSolidWallpaper
            || !string.Equals(previous.SolidWallpaperColor, loaded.SolidWallpaperColor, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previous.WallpaperUri, loaded.WallpaperUri, StringComparison.Ordinal);
        var lockScreenChanged = (previous.LockScreen ?? new()).Normalize() != (loaded.LockScreen ?? new()).Normalize()
            || previous.DeptEffectEnabled != loaded.DeptEffectEnabled
            || previous.DeptEffectAutomatic != loaded.DeptEffectAutomatic;
        var barVisibilityChanged = previous.TaskbarAutoHide != loaded.TaskbarAutoHide
            || previous.SystemBarAutoHide != loaded.SystemBarAutoHide
            || previous.MediaWidgetAutoHide != loaded.MediaWidgetAutoHide
            || !string.Equals(previous.CursorThemeId, loaded.CursorThemeId, StringComparison.Ordinal)
            || previous.CursorSize != loaded.CursorSize;

        if (accessibilityChanged) ApplyAccessibilityPreferences();
        if (accentChanged) ApplyAccentPreference();
        if (languageChanged) ApplyLanguageResources();
        if (typographyChanged) TypographyPolicy.Apply(Resources, _appearanceSettings.SystemFontFamily, _appearanceSettings.TextScalePercent);
        if (profilePictureChanged || typographyChanged) ApplyProfilePictureResources();
        if (taskbarSizeChanged || typographyChanged) ApplyTaskbarSizeResources();

        if (accessibilityChanged || accentChanged || languageChanged || typographyChanged ||
            profilePictureChanged || taskbarSizeChanged || wallpaperChanged || lockScreenChanged || barVisibilityChanged ||
            previous.ThemeMode != loaded.ThemeMode)
        {
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Production shell startup is intentionally a hard gate. A normal
    /// Avalonia Wayland window is an xdg_toplevel, even when fullscreen, and
    /// must never be used as a substitute for a desktop/panel layer surface.
    /// The maintained backend plugs into this point before any Window exists.
    /// </summary>
    private void StartNativeShellSession(IClassicDesktopStyleApplicationLifetime desktop, Task<bool> layerShellProbeTask)
    {
        Console.Error.WriteLine(
            $"[daisy-shell] Starting native layer-shell session on {Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "<unset>"}.");
        // The probe was started early in OnFrameworkInitializationCompleted and ran
        // concurrently with theme/decoration setup. By the time we reach here it is
        // almost always already complete — no UI thread blocking.
        // Use a bounded wait to avoid indefinite deadlock if the compositor stalls.
        var protocolAvailable = layerShellProbeTask.IsCompleted
            ? layerShellProbeTask.Result
            : layerShellProbeTask.Wait(TimeSpan.FromSeconds(3)) && layerShellProbeTask.Result;
        if (!protocolAvailable)
        {
            Console.Error.WriteLine(
                "DaisyOS cannot start the native shell: KWin did not advertise zwlr_layer_shell_v1. " +
                "The session will not fall back to a fullscreen application.");
            desktop.Shutdown(1);
            return;
        }

        var backend = new AvaloniaLayerShellWindowBackend();
        _layerShellBackend = backend;
        if (!backend.IsAvailable)
        {
            Console.Error.WriteLine(
                "DaisyOS cannot start the native shell: the pinned DaisyOS.Avalonia.Wayland layer-shell backend is not installed.");
            desktop.Shutdown(1);
            return;
        }

        // Initialize persistent wallpaper state before ShellView is attached.
        // WallpaperLayer begins its first asynchronous load when it maps, not
        // when the desktop layer's Opened event later fires.
        InitializeWallpaperState();

        // This is intentionally a background layer, never a fullscreen
        // xdg_toplevel. KWin remains free to manage every application window
        // above it. The next surface-host pass moves chrome/overlays to their
        // dedicated Top and Overlay surfaces without changing shell state.
        var shell = new ShellView();
        var panelContent = shell.DetachDesktopChromeForNativePanel();
        var launcherContent = shell.DetachLauncherForNativeOverlay();
        var widgetLibrary = shell.Desktop.DetachWidgetLibraryForNativeOverlay();
        var systemFlyoutContent = shell.SystemBar.DetachFlyoutsForNativeOverlay();
        Control? launcherAppContextMenu = null;
        if (systemFlyoutContent is Panel flyoutPanel)
        {
            if (shell.Launcher.DetachPowerMenuForNativeOverlay() is { } launcherPowerMenu)
            {
                flyoutPanel.Children.Add(launcherPowerMenu);
            }
            if (shell.Launcher.DetachAppContextMenuForNativeOverlay() is { } launcherAppMenu)
            {
                launcherAppContextMenu = launcherAppMenu;
                flyoutPanel.Children.Add(launcherAppMenu);
            }
            if (shell.Taskbar.DetachContextMenuForNativeOverlay() is { } taskbarMenu)
            {
                flyoutPanel.Children.Add(taskbarMenu);
            }
            if (shell.MediaWidget.DetachContextMenuForNativeOverlay() is { } mediaMenu)
            {
                flyoutPanel.Children.Add(mediaMenu);
            }
            flyoutPanel.Children.Add(shell.Desktop.DetachWidgetContextMenuForNativeOverlay());
        }
        _gamingOverlay = new();
        _gamingOverlay.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty)
                Volatile.Write(ref _gameTelemetryVisible, _gamingOverlay.IsVisible ? 1 : 0);
        };
        _gamingOverlaySurface = backend.CreateWindow(
            LayerShellSurfaceOptions.GamingOverlay(), _gamingOverlay);
        _blurEffects.Add(new KWinBlur(_gamingOverlaySurface, _gamingOverlay, () =>
            _gamingOverlay.IsVisible ? new Control[] { _gamingOverlay.FindControl<Border>("MaterialFrame")! } : []));
        _volumeIndicator = new();
        _volumeIndicatorSurface = backend.CreateWindow(LayerShellSurfaceOptions.VolumeIndicator(), _volumeIndicator);
        _blurEffects.Add(new KWinBlur(_volumeIndicatorSurface, _volumeIndicator,
            () => _volumeIndicator.IsVisible ? new Control[] { _volumeIndicator.MaterialSurface } : []));
        _volumeKeys = new Services.VolumeKeyController(new DaisyOS.System.Audio.LinuxAudioService(new SafeCommandRunner()));
        _volumeKeys.Changed += (volume, muted, input) =>
        {
            if (IsLockScreenActive) return;
            if (!input) SessionState.LastKnownVolume = volume;
            // Measure visible content on the first native map, rather than mapping
            // an empty hidden root and leaving its material without a viewport.
            _volumeIndicator.IsVisible = true;
            _volumeIndicatorSurface.Show();
            _volumeIndicatorSurface.UpdateLayout();
            LayerShellWindow.SetInputPassthrough(_volumeIndicatorSurface, true);
            _volumeIndicator.ShowLevel(volume, muted, input);
        };
        var toastContent = shell.DetachNotificationToastsForNativeOverlay();
        var consoleContent = shell.DetachConsoleForNativeOverlay();
        var metricsContent = shell.DetachMetricsForNativeOverlay();
        _localSendWindow = new LocalSendWindow();
        var wallpaperSurface = backend.CreateWindow(
            LayerShellSurfaceOptions.Desktop() with
            {
                Namespace = "daisyos.wallpaper",
                InputPassthrough = true
            }, shell.DetachWallpaperForNativeBackground());
        var desktopSurface = backend.CreateWindow(LayerShellSurfaceOptions.DesktopItems(), shell);
        var panelSurface = backend.CreateWindow(
            LayerShellSurfaceOptions.PrimaryPanel(
                ShellSurfacePlan.PrimaryPanelExclusiveZone,
                surfaceHeight: ShellSurfacePlan.PrimaryPanelSurfaceHeight),
            panelContent);
        _nativePanelSurface = panelSurface;
        var bottomEdgeSensorSurface = backend.CreateWindow(
            LayerShellSurfaceOptions.BottomEdgeSensor(),
            new Border { Background = Brushes.Transparent });
        _nativeBottomEdgeSensorSurface = bottomEdgeSensorSurface;
        // The mapped panel captures the full bottom strip when either bar is
        // visible. It must reveal the other bar even while it covers the sensor.
        BindBottomEdgeReveal(panelSurface, shell);
        BindBottomEdgeReveal(bottomEdgeSensorSurface, shell);
        var launcherSurface = backend.CreateWindow(
            LayerShellSurfaceOptions.LauncherOverlay() with
            {
                InputPassthrough = true,
                KeyboardInteractivity = LayerShellKeyboardInteractivity.None
            }, launcherContent);
        _nativeLauncherSurface = launcherSurface;
        // The confirmation hint is mapped on first use. Its surface is
        // deliberately keyboard-less and input-transparent so a fullscreen
        // game keeps focus while the first Meta press is evaluated.
        _launcherHint = new Views.Components.Launcher.LauncherHint();
        _launcherHintSurface = backend.CreateWindow(
            LayerShellSurfaceOptions.LauncherHint(), _launcherHint);
        var systemFlyoutSurface = backend.CreateWindow(
            LayerShellSurfaceOptions.SystemFlyoutOverlay() with
            {
                InputPassthrough = true,
                KeyboardInteractivity = LayerShellKeyboardInteractivity.None
            }, systemFlyoutContent);
        _nativeSystemFlyoutSurface = systemFlyoutSurface;
        // A distinct surface lets KWin blur desktop widgets as well as wallpaper.
        var widgetLibrarySurface = backend.CreateWindow(
            LayerShellSurfaceOptions.TopTransient("daisyos.overlay.widget-library") with
            {
                InputPassthrough = true,
                KeyboardInteractivity = LayerShellKeyboardInteractivity.None
            }, new Grid { Children = { widgetLibrary } });
        widgetLibrary.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty || change.Property == InputElement.IsHitTestVisibleProperty)
            {
                SetNativeSurfaceVisible(widgetLibrarySurface, widgetLibrary.IsVisible, captureKeyboard: true);
                LayerShellWindow.SetInputPassthrough(widgetLibrarySurface,
                    !widgetLibrary.IsVisible || !widgetLibrary.IsHitTestVisible);
            }
        };
        _blurEffects.Add(new KWinBlur(widgetLibrarySurface, widgetLibrarySurface, () =>
            new[] { widgetLibrary.FindControl<Control>("LibraryFrame") }.OfType<Control>()));
        ConfigureTransientSurfaceDismissal(widgetLibrarySurface,
            () => widgetLibrary.IsOpen,
            source => IsWithin(source, widgetLibrary.FindControl<Control>("LibraryFrame")!),
            widgetLibrary.Dismiss);
        var toastSurface = backend.CreateWindow(
            LayerShellSurfaceOptions.NotificationOverlay(), toastContent);
        var consoleSurface = backend.CreateWindow(
            LayerShellSurfaceOptions.TopTransient("daisyos.overlay.console") with
            {
                InputPassthrough = true,
                KeyboardInteractivity = LayerShellKeyboardInteractivity.None
            }, consoleContent);
        var metricsSurface = backend.CreateWindow(
            LayerShellSurfaceOptions.TopTransient("daisyos.overlay.metrics") with
            {
                InputPassthrough = true,
                KeyboardInteractivity = LayerShellKeyboardInteractivity.None
            }, metricsContent);
        var localSendSurface = backend.CreateWindow(
            LayerShellSurfaceOptions.Overlay("daisyos.overlay.localsend") with
            {
                InputPassthrough = true,
                KeyboardInteractivity = LayerShellKeyboardInteractivity.None
            }, _localSendWindow);
        _nativeLocalSendSurface = localSendSurface;
        var screenshotContent = shell.DetachScreenshotForNativeOverlay();
        var screenshotBackdropSurface = backend.CreateWindow(
            LayerShellSurfaceOptions.PassiveOverlay("daisyos.overlay.screenshot-backdrop") with
            {
                Role = LayerShellSurfaceRole.Panel,
                ExclusiveZone = LayerShellSurfaceOptions.IgnoreOtherExclusiveZones
            }, screenshotContent.DetachFrozenBackdrop());
        _nativeScreenshotBackdropSurface = screenshotBackdropSurface;
        var screenshotSurface = backend.CreateWindow(
            LayerShellSurfaceOptions.Overlay("daisyos.overlay.screenshot") with
            {
                ExclusiveZone = LayerShellSurfaceOptions.IgnoreOtherExclusiveZones,
                InputPassthrough = true,
                KeyboardInteractivity = LayerShellKeyboardInteractivity.None
            }, screenshotContent);
        _nativeScreenshotSurface = screenshotSurface;
        _nativeShellSurfaces.Add(wallpaperSurface);
        _nativeShellSurfaces.Add(desktopSurface);
        _nativeShellSurfaces.Add(panelSurface);
        _nativeShellSurfaces.Add(bottomEdgeSensorSurface);
        _nativeShellSurfaces.Add(launcherSurface);
        if (_launcherHintSurface is not null) _nativeShellSurfaces.Add(_launcherHintSurface);
        _nativeShellSurfaces.Add(systemFlyoutSurface);
        _nativeShellSurfaces.Add(widgetLibrarySurface);
        var tooltipSurface = backend.CreateWindow(
            LayerShellSurfaceOptions.BottomTooltipOverlay(), shell.DetachBottomTooltipsForNativeOverlay());
        _nativeShellSurfaces.Add(tooltipSurface);
        _nativeShellSurfaces.Add(toastSurface);
        _nativeShellSurfaces.Add(consoleSurface);
        _nativeShellSurfaces.Add(metricsSurface);
        _nativeShellSurfaces.Add(localSendSurface);
        _nativeShellSurfaces.Add(screenshotBackdropSurface);
        _nativeShellSurfaces.Add(screenshotSurface);
        _nativeShellSurfaces.Add(_gamingOverlaySurface);
        _nativeShellSurfaces.Add(_volumeIndicatorSurface);

        foreach (var surface in _nativeShellSurfaces)
        {
            surface.AddHandler(InputElement.PointerPressedEvent, (_, _) => _recentPrefetch.Interaction(), RoutingStrategies.Tunnel, handledEventsToo: true);
            surface.AddHandler(InputElement.PointerMovedEvent, (_, _) => _recentPrefetch.Interaction(), RoutingStrategies.Tunnel, handledEventsToo: true);
            surface.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => _recentPrefetch.Interaction(), RoutingStrategies.Tunnel, handledEventsToo: true);
            surface.AddHandler(InputElement.KeyDownEvent, (_, _) => _recentPrefetch.Interaction(), RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        // Publish the visible material frames themselves. Using the surrounding
        // UserControls gave every panel a generic 16px mask, which disagreed
        // with the 18px RoundedXL frame and exposed stair-stepped blur pixels
        // when a saturated backdrop reached a corner.
        _blurEffects.Add(new KWinBlur(panelSurface, panelSurface, () =>
            new[]
            {
                shell.MediaWidget.FindControl<Border>("MediaFrame"),
                shell.Taskbar.FindControl<Border>("TaskbarBody"),
                shell.SystemBar.FindControl<Border>("SystemBarFrame")
            }.OfType<Control>()));
        // Layer-shell has no native xdg_popup role, so Avalonia hosts desktop
        // context menus in the window's sibling PopupOverlayLayer. Scan and
        // observe the complete surface tree, not only the DesktopView subtree.
        var desktopBlur = new KWinBlur(desktopSurface, desktopSurface, () =>
        {
            return desktopSurface.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Name is "OuterFrame" or "MenuBlurHost")
                .Cast<Control>()
                .ToList();
        });
        shell.Desktop.ContextMenuVisualChanged += (_, _) => desktopBlur.RefreshImmediately();
        // MenuFlyouts on a layer-shell window live in the surface's sibling
        // PopupOverlayLayer, not below LauncherView. Observe and scan the
        // complete surface so opening and closing a menu updates KWin's mask
        // in the same layout transaction as the popup itself.
        var launcherMaterial = (Control?)shell.Launcher.FindControl<Border>("LauncherRootBorder") ?? shell.Launcher;
        Control[] launcherGestureMaterials = [launcherMaterial];
        var launcherBlur = new KWinBlur(launcherSurface, launcherSurface, () =>
        {
            if (!shell.Launcher.IsEffectivelyVisible || shell.Launcher.Opacity <= 0.001 ||
                shell.Launcher.UseSolidBackdrop)
                return [];
            // Gestures close both menus before tracking. Avoid walking every
            // tile's visual tree for each opacity sample of the fixed backdrop.
            if (shell.Launcher.IsRevealInProgress) return launcherGestureMaterials;
            var regions = launcherSurface.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Name == "MenuBlurHost")
                .Cast<Control>()
                .ToList();
            regions.Insert(0, launcherMaterial);
            return regions;
        });
        _launcherBlur = launcherBlur;
        shell.Launcher.SetBackgroundWorkSuspended(SessionState.GamingMode || HasFullscreenWindow());
        shell.Launcher.OpenStateChanged += (_, _) => launcherBlur.Invalidate();
        shell.Launcher.BackdropStyleChanged += (_, _) => launcherBlur.RefreshImmediately();
        _blurEffects.Add(new KWinBlur(systemFlyoutSurface, systemFlyoutSurface, () =>
            new[] { shell.SystemBar.QuickSettingsMaterialFrame,
                shell.SystemBar.NotificationMaterialFrame, shell.SystemBar.CalendarMaterialFrame, shell.SystemBar.AppTrayMaterialFrame,
                launcherAppContextMenu,
                shell.Taskbar.FindControl<Control>("TaskbarContextMenuHost"),
                shell.MediaWidget.FindControl<Control>("MediaContextMenuHost"),
                shell.Desktop.FindControl<Control>("DesktopWidgetContextMenuHost") }.OfType<Control>()
                .Concat(KWinBlur.PopupMaterials(systemFlyoutSurface))));
        _blurEffects.Add(new KWinBlur(toastSurface, toastContent, () =>
            toastContent.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Classes.Contains("ToastCard"))));
        _blurEffects.Add(new KWinBlur(localSendSurface, _localSendWindow, () =>
            _localSendWindow.FindControl<Border>("ShareDialogCard") is { } card
                ? new Control[] { card }
                : []));
        _blurEffects.Add(new KWinBlur(screenshotSurface, screenshotContent, () => screenshotContent.MaterialRegions));
        screenshotContent.Closed += (_, _) =>
        {
            if (_nativeScreenshotSurface is { } surface)
            {
                SetNativeSurfaceVisible(surface, false, captureKeyboard: true);
            }
        };
        screenshotContent.CaptureInputChanged += (_, captureInput) =>
            SetNativeSurfaceVisible(screenshotSurface, captureInput, captureKeyboard: true);

        ConfigureTransientSurfaceDismissal(
            launcherSurface,
            () => shell.IsLauncherVisible,
            source => shell.Launcher.IsWithinInteractiveSurface(source),
            () =>
            {
                if (shell.Launcher.IsPowerMenuVisible || shell.Launcher.IsAppContextMenuVisible)
                {
                    shell.Launcher.ClosePowerMenu();
                    shell.Launcher.CloseAppContextMenu();
                    return;
                }
                HideLauncher();
            });
        var desktopMenus = _nativeDesktopMenus = new DesktopContextMenuOverlay(shell.Desktop, systemFlyoutContent, visible =>
        {
            shell.Desktop.IsNativeContextMenuOpen = visible;
            SetNativeSurfaceVisible(systemFlyoutSurface, visible, captureKeyboard: true);
        });
        systemFlyoutSurface.Deactivated += (_, _) => desktopMenus.Close();
        ConfigureTransientSurfaceDismissal(
            systemFlyoutSurface,
            () => shell.Launcher.IsPowerMenuVisible || shell.Launcher.IsAppContextMenuVisible || shell.SystemBar.IsQuickSettingsVisible || shell.SystemBar.IsCalendarPanelVisible || shell.SystemBar.IsNotificationPanelVisible || shell.SystemBar.IsAppTrayVisible || shell.Taskbar.IsContextMenuVisible || shell.MediaWidget.IsContextMenuVisible || shell.Desktop.IsWidgetContextMenuVisible || desktopMenus.IsOpen,
            source => IsWithinVisibleFlyout(source, systemFlyoutContent) ||
                source.GetSelfAndVisualAncestors().OfType<Border>().Any(border => border.Name == "MenuBlurHost"),
            () => { desktopMenus.Close(); DismissTransientShellSurfaces(); });
        ConfigureTransientSurfaceDismissal(
            metricsSurface,
            () => shell.IsMetricsPanelVisible,
            source => IsWithin(source, metricsContent),
            () => shell.HideMetricsPanel());
        ConfigureTransientSurfaceDismissal(
            localSendSurface,
            () => _localSendWindow?.IsOpen == true,
            source => _localSendWindow?.IsWithinShareDialog(source) == true,
            () => _localSendWindow?.Dismiss());

        LauncherVisibilityChanged += (_, visible) => SetNativeSurfaceVisible(launcherSurface, visible, captureKeyboard: true);
        shell.Launcher.PowerMenuVisibilityChanged += (_, visible) => SetNativeSurfaceVisible(systemFlyoutSurface, visible, captureKeyboard: true);
        shell.Launcher.AppContextMenuVisibilityChanged += (_, visible) => SetNativeSurfaceVisible(systemFlyoutSurface, visible, captureKeyboard: true);
        shell.SystemBar.FlyoutVisibilityChanged += (_, visible) => SetNativeSurfaceVisible(systemFlyoutSurface, visible, captureKeyboard: true);
        shell.Taskbar.ContextMenuVisibilityChanged += (_, visible) => SetNativeSurfaceVisible(systemFlyoutSurface, visible, captureKeyboard: true);
        shell.MediaWidget.ContextMenuVisibilityChanged += (_, visible) => SetNativeSurfaceVisible(systemFlyoutSurface, visible, captureKeyboard: true);
        shell.Desktop.WidgetContextMenuVisibilityChanged += (_, visible) => SetNativeSurfaceVisible(systemFlyoutSurface, visible, captureKeyboard: true);
        shell.ConsoleSurfaceVisibilityChanged += (_, visible) => SetNativeSurfaceVisible(consoleSurface, visible, captureKeyboard: true);
        shell.MetricsSurfaceVisibilityChanged += (_, visible) => SetNativeSurfaceVisible(metricsSurface, visible, captureKeyboard: true);
        _localSendWindow.DismissRequested += (_, _) => SetNativeSurfaceVisible(localSendSurface, false, captureKeyboard: true);
        // A share choice is transient shell UI. Pointer presses outside the
        // card are handled by ConfigureTransientSurfaceDismissal above; this
        // covers the complementary case where focus moves to another KWin
        // client through a keyboard shortcut or compositor action.
        localSendSurface.Deactivated += (_, _) =>
        {
            if (_localSendWindow?.IsOpen == true)
                _localSendWindow.Dismiss();
        };
        toastContent.ActiveToastVisibilityChanged += (_, visible) =>
            LayerShellWindow.SetInputPassthrough(toastSurface, !visible);
        desktop.MainWindow = desktopSurface;
        desktopSurface.Opened += (_, _) =>
        {
            ConfigureShellOverlays(shell);
            StartCompositorBridge(shell);
            _ = RecordInitialShellFrameAsync(desktopSurface);
        };
        wallpaperSurface.Show();
        StartNativeDisplayTracking(wallpaperSurface);
        desktopSurface.Show();
        panelSurface.Show();
        bottomEdgeSensorSurface.Show();
        toastSurface.Show();
        tooltipSurface.Show();
        LayerShellWindow.SetInputPassthrough(tooltipSurface, true);
        launcherSurface.Show();
        systemFlyoutSurface.Show();
        // Map infrequently used full-output overlays on first use. Mapping
        // even transparent windows allocates full-sized graphics buffers.

        // Do not rely on construction-time options for mapped empty surfaces:
        // issue an explicit empty input region once every native layer exists.
        SetNativeSurfaceVisible(launcherSurface, shell.IsLauncherVisible, captureKeyboard: true);
        SetNativeSurfaceVisible(systemFlyoutSurface, false, captureKeyboard: true);
        SetNativeSurfaceVisible(consoleSurface, false, captureKeyboard: true);
        SetNativeSurfaceVisible(metricsSurface, shell.IsMetricsPanelVisible, captureKeyboard: true);
        SetNativeSurfaceVisible(localSendSurface, false, captureKeyboard: true);
        LayerShellWindow.SetInputPassthrough(toastSurface, !toastContent.HasActiveToasts);
    }

    private static async Task RecordInitialShellFrameAsync(TopLevel topLevel)
    {
        try
        {
            var animationFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            topLevel.RequestAnimationFrame(_ => animationFrame.TrySetResult());
            await animationFrame.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var visual = ElementComposition.GetElementVisual(topLevel)
                ?? throw new InvalidOperationException("The shell desktop surface has no composition visual.");
            await visual.Compositor.RequestCompositionBatchCommitAsync().Rendered.WaitAsync(TimeSpan.FromSeconds(10));
            Console.Error.WriteLine(
                $"[daisy-shell-ready] pid={Environment.ProcessId} utc={DateTimeOffset.UtcNow:O} stage=initial-desktop-rendered");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[daisy-shell-ready] pid={Environment.ProcessId} stage=failed error={ex.Message}");
        }
    }

    private static void SetNativeSurfaceVisible(Window surface, bool visible, bool captureKeyboard)
    {
        // A layer-shell role is long-lived. Hiding the Window destroys it and
        // forces KWin to configure a new surface on every open, which produces
        // flicker and eventually leaves transient UI unmapped. The visual
        // child controls their own IsVisible state; switch only the Wayland
        // input region here so an invisible full-output surface never blocks
        // the desktop or applications below it. Before first use, leave
        // infrequent overlays unmapped so they allocate no render buffers.
        LayerShellWindow.SetInputPassthrough(surface, !visible);
        LayerShellWindow.SetKeyboardInteractivity(
            surface,
            visible && captureKeyboard
                ? ZwlrLayerSurfaceV1.KeyboardInteractivity.Exclusive
                : ZwlrLayerSurfaceV1.KeyboardInteractivity.None);
        if (visible && !surface.IsVisible)
            surface.Show();
    }

    /// <summary>
    /// Native layer surfaces are distinct Wayland toplevels, so shell-root
    /// pointer routing cannot see a click that lands outside a popover. Give
    /// each modal surface its own full-work-area dismissal boundary instead.
    /// The first outside press deliberately only closes the transient, as in
    /// GTK/KDE menus; it never also activates the item below it.
    /// </summary>
    private void ConfigureTransientSurfaceDismissal(
        Window surface,
        Func<bool> isOpen,
        Func<Visual, bool> isInsideVisibleContent,
        Action dismiss)
    {
        surface.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!isOpen() || e.Source is not Visual source || isInsideVisibleContent(source))
                return;

            e.Handled = true;
            dismiss();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        surface.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (!isOpen())
                return;

            if (e.Key == Key.Escape)
            {
                // Handling this key event also prevents the Wayland backend from
                // treating Escape's control character as TextBox input.
                e.Handled = true;
                dismiss();
                return;
            }

            if ((e.KeyModifiers.HasFlag(KeyModifiers.Meta) && e.Key == Key.L) ||
                (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.L))
            {
                e.Handled = true;
                LockSession();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private static bool IsWithin(Visual source, Visual target) =>
        ReferenceEquals(source, target) || source.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, target));

    private static bool IsWithinVisibleFlyout(Visual source, Control overlay) =>
        overlay is Panel panel && panel.Children
            .OfType<Control>()
            .Any(child => child.IsVisible && IsWithin(source, child));

    private void InitializeWallpaperState()
    {
        if (_wallpaperService is not null)
        {
            return;
        }

        try
        {
            var wallpaperPath = GetCurrentWallpaperSource();
            _wallpaperService = new WallpaperService(wallpaperPath);
            _wallpaperService.WallpaperChanged += (_, changedWallpaperUri) =>
            {
                WallpaperChanged?.Invoke(this, changedWallpaperUri);
                _ = RefreshWallpaperLabelContrastAsync(changedWallpaperUri);
            };
            _ = RefreshWallpaperLabelContrastAsync(wallpaperPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize wallpaper state: {ex.Message}");
        }
    }

    private async Task RefreshWallpaperLabelContrastAsync(string? wallpaperUri)
    {
        if (string.IsNullOrWhiteSpace(wallpaperUri)) return;

        try
        {
            Color labelColor;
            bool isDark;

            if (WallpaperSource.TryGetSolidColor(wallpaperUri, out var solidColor))
            {
                labelColor = WallpaperLabelContrast.ForWallpaper(solidColor);
                isDark = WallpaperLabelContrast.IsDarkLabel(solidColor);
            }
            else
            {
                var avgColor = await Task.Run(() => ExtractAverageWallpaperColor(wallpaperUri)).ConfigureAwait(false);
                labelColor = WallpaperLabelContrast.ForWallpaper(avgColor);
                isDark = WallpaperLabelContrast.IsDarkLabel(avgColor);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyDesktopLabelContrast(labelColor, isDark);
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to refresh wallpaper label contrast: {ex.Message}");
        }
    }

    private void ApplyDesktopLabelContrast(Color labelColor, bool isDark)
    {
        if (Resources is null) return;

        var brush = new SolidColorBrush(labelColor);
        Resources["DesktopItemLabelBrush"] = brush;
        Resources["DesktopLabelBrush"] = brush;

        if (isDark)
        {
            Resources["DesktopItemLabelShadow"] = null;
            Resources["DesktopLabelShadow"] = null;
        }
        else
        {
            var shadow = new DropShadowEffect
            {
                OffsetX = 0,
                OffsetY = 1,
                BlurRadius = 2,
                Color = Colors.Black,
                Opacity = 0.65
            };
            Resources["DesktopItemLabelShadow"] = shadow;
            Resources["DesktopLabelShadow"] = shadow;
        }
    }

    private static Color ExtractAverageWallpaperColor(string wallpaperUri)
    {
        try
        {
            using var stream = OpenWallpaperReadStream(wallpaperUri);
            if (stream is null) return Colors.Gray;

            using var bitmap = SkiaSharp.SKBitmap.Decode(stream);
            if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
                return Colors.Gray;

            int width = bitmap.Width;
            int height = bitmap.Height;
            int stepX = Math.Max(1, width / 32);
            int stepY = Math.Max(1, height / 32);

            long totalR = 0, totalG = 0, totalB = 0;
            int count = 0;

            for (int y = 0; y < height; y += stepY)
            {
                for (int x = 0; x < width; x += stepX)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.Alpha >= 24)
                    {
                        totalR += pixel.Red;
                        totalG += pixel.Green;
                        totalB += pixel.Blue;
                        count++;
                    }
                }
            }

            if (count == 0) return Colors.Gray;
            return Color.FromRgb((byte)(totalR / count), (byte)(totalG / count), (byte)(totalB / count));
        }
        catch
        {
            return Colors.Gray;
        }
    }

    private static Stream? OpenWallpaperReadStream(string wallpaperUri)
    {
        try
        {
            if (Uri.TryCreate(wallpaperUri, UriKind.Absolute, out var uri) && uri.Scheme == "avares")
            {
                return AssetLoader.Open(uri);
            }

            var resolved = WallpaperSource.ResolveWallpaperPath(wallpaperUri);
            if (File.Exists(resolved))
            {
                return File.OpenRead(resolved);
            }
        }
        catch
        {
            // Ignore open errors
        }

        return null;
    }

    /// <summary>Applies the requested appearance and regenerates its wallpaper-derived accent roles.</summary>
    public Task SetShellThemeAsync(ThemeVariant theme) =>
        SetThemeModeAsync(theme == ThemeVariant.Light ? ThemeMode.Light : ThemeMode.Dark);

    public async Task SetThemeModeAsync(ThemeMode mode)
    {
        if (_useDesktopSettings)
        {
            _appearanceSettings.ThemeMode = mode;
            RequestedThemeVariant = mode == ThemeMode.Automatic
                ? (SolarSchedule.ForLocalTimeZone(DateTimeOffset.Now).IsDaylight(DateTimeOffset.Now) ? ThemeVariant.Light : ThemeVariant.Dark)
                : ToThemeVariant(mode);
            ApplyAccessibilityPreferences();
            ApplyAccentPreference();
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
            await ApplyDesktopSettingAsync("theme", mode.ToString());
            return;
        }
        _appearanceSettings.ThemeMode = mode;
        if (mode == ThemeMode.Automatic)
        {
            _automaticThemeScheduler.Start();
        }
        else
        {
            _automaticThemeScheduler.Stop();
            await ApplyThemeVariantAsync(ToThemeVariant(mode));
        }

        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetAutomaticThemeScheduleDescription() =>
        _automaticThemeScheduler.GetScheduleDescription(DateTimeOffset.Now);

    public async Task SetAccentColorAsync(AccentColor accentColor)
    {
        if (_useDesktopSettings)
        {
            _appearanceSettings.AccentColor = accentColor;
            ApplyAccentPreference();
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
            await ApplyDesktopSettingAsync("accent", accentColor.ToString());
            return;
        }
        _appearanceSettings.AccentColor = accentColor;
        ApplyAccentPreference();
        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetHighContrastAsync(bool enabled)
    {
        if (_useDesktopSettings)
        {
            _appearanceSettings.HighContrast = enabled;
            ApplyAccessibilityPreferences();
            ApplyAccentPreference();
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
            await ApplyDesktopSettingAsync("contrast", enabled.ToString());
            return;
        }
        _appearanceSettings.HighContrast = enabled;
        ApplyAccessibilityPreferences();
        ApplyAccentPreference();
        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public LockScreenAppearance CurrentLockScreenAppearance => (_appearanceSettings.LockScreen ?? new()).Normalize();

    public async Task SetLockScreenAppearanceAsync(LockScreenAppearance preferences)
    {
        preferences = preferences.Normalize();
        if (_useDesktopSettings)
        {
            _appearanceSettings.LockScreen = preferences;
            var json = global::System.Text.Json.JsonSerializer.Serialize(preferences,
                Services.Serialization.DaisyShellJsonContext.Default.LockScreenAppearance);
            await ApplyDesktopSettingAsync("lock-screen", json);
            return;
        }
        _appearanceSettings.LockScreen = preferences;
        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool DeptEffectEnabled => _appearanceSettings.DeptEffectEnabled;
    public bool DeptEffectAutomatic => _appearanceSettings.DeptEffectAutomatic;

    public async Task SetDeptEffectAsync(bool enabled, bool automatic)
    {
        if (_useDesktopSettings)
        {
            _appearanceSettings.DeptEffectEnabled = enabled;
            _appearanceSettings.DeptEffectAutomatic = automatic;
            _appearanceSettings.DeptEffectStrength = 15;
            await ApplyDesktopSettingAsync("dept-effect", enabled.ToString(), automatic.ToString());
            return;
        }
        _appearanceSettings.DeptEffectEnabled = enabled;
        _appearanceSettings.DeptEffectAutomatic = automatic;
        _appearanceSettings.DeptEffectStrength = 15; // Retire legacy manual strength values.
        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetReduceMotionAsync(bool enabled)
    {
        if (_useDesktopSettings)
        {
            _appearanceSettings.ReduceMotion = enabled;
            ApplyAccessibilityPreferences();
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
            await ApplyDesktopSettingAsync("motion", enabled.ToString());
            return;
        }
        _appearanceSettings.ReduceMotion = enabled;
        ApplyAccessibilityPreferences();
        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetTaskbarSizeAsync(int size)
    {
        size = Math.Clamp(size, 0, 2);
        if (_useDesktopSettings)
        {
            _appearanceSettings.TaskbarSize = size;
            ApplyTaskbarSizeResources();
            await ApplyDesktopSettingAsync("taskbar-size", size.ToString());
            return;
        }
        _appearanceSettings.TaskbarSize = size;
        ApplyTaskbarSizeResources();
        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyTaskbarSizeResources()
    {
        var height = CurrentTaskbarSize switch { 0 => 44d, 2 => 55d, _ => 52d };
        Resources["TaskbarScale"] = height / 52d;
        Resources["TaskbarMenuInset"] = new Thickness(0, 0, 0, height + 3);
    }

    public async Task SetTaskbarAutoHideAsync(bool enabled)
    {
        if (_useDesktopSettings)
        {
            _appearanceSettings.TaskbarAutoHide = enabled;
            await ApplyDesktopSettingAsync("taskbar-auto-hide", enabled.ToString());
            return;
        }
        _appearanceSettings.TaskbarAutoHide = enabled;
        _shellView?.SetBottomBarAutoHide(_appearanceSettings.TaskbarAutoHide, _appearanceSettings.SystemBarAutoHide, _appearanceSettings.MediaWidgetAutoHide);
        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetSystemBarAutoHideAsync(bool enabled)
    {
        if (_useDesktopSettings)
        {
            _appearanceSettings.SystemBarAutoHide = enabled;
            await ApplyDesktopSettingAsync("systembar-auto-hide", enabled.ToString());
            return;
        }
        _appearanceSettings.SystemBarAutoHide = enabled;
        _shellView?.SetBottomBarAutoHide(_appearanceSettings.TaskbarAutoHide, _appearanceSettings.SystemBarAutoHide, _appearanceSettings.MediaWidgetAutoHide);
        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetMediaWidgetAutoHideAsync(bool enabled)
    {
        if (_useDesktopSettings)
        {
            _appearanceSettings.MediaWidgetAutoHide = enabled;
            await ApplyDesktopSettingAsync("media-widget-auto-hide", enabled.ToString());
            return;
        }
        _appearanceSettings.MediaWidgetAutoHide = enabled;
        _shellView?.SetBottomBarAutoHide(_appearanceSettings.TaskbarAutoHide, _appearanceSettings.SystemBarAutoHide, enabled);
        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> SetCursorAsync(string themeId, int size)
    {
        if (_useDesktopSettings) { return await ApplyDesktopSettingAsync("cursor", themeId, size.ToString()); }
        var result = await _personalizationService.ApplyCursorAsync(themeId, size);
        if (!result.Succeeded) { Feedback.Show(Loc.Get(result.Error ?? "The cursor could not be changed.")); return false; }
        _appearanceSettings.CursorThemeId = themeId;
        _appearanceSettings.CursorSize = size;
        Environment.SetEnvironmentVariable("XCURSOR_THEME", themeId);
        Environment.SetEnvironmentVariable("XCURSOR_SIZE", size.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<bool> SetTypographyAsync(string family, int scalePercent)
    {
        if (_useDesktopSettings) { return await ApplyDesktopSettingAsync("typography", family, scalePercent.ToString()); }
        var result = await _personalizationService.ApplyTypographyAsync(family, scalePercent);
        if (!result.Succeeded) { Feedback.Show(result.Error ?? "Typography could not be changed."); return false; }
        _appearanceSettings.SystemFontFamily = family;
        _appearanceSettings.TextScalePercent = scalePercent;
        ApplyTypographyResources();
        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<FontInstallResult> InstallSystemFontAsync(string path)
    {
        var result = await _personalizationService.InstallFontAsync(path);
        if (!result.Succeeded) Feedback.Show(result.Error ?? "The font could not be installed.");
        return result;
    }

    private Avalonia.Media.Imaging.Bitmap? _profilePicture;
    private string? _loadedProfilePicturePath;

    public async Task SetProfilePictureAsync(string? source)
    {
        // Settings runs as a separate process. Keep the shell as the owner of
        // the shared profile image so launcher, lock screen, and other shell
        // surfaces all update from the same persisted file and live resource.
        if (_useDesktopSettings)
        {
            await ApplyDesktopSettingAsync("profile-picture", source ?? string.Empty);
            return;
        }

        var previous = _appearanceSettings.ProfilePictureUri;
        var imported = source is null ? string.Empty : await ProfilePictureStore.ImportAsync(source, AppearancePreferencesPath);
        _appearanceSettings.ProfilePictureUri = imported;
        try { await _appearanceSettingsStore.SaveAsync(_appearanceSettings); }
        catch
        {
            _appearanceSettings.ProfilePictureUri = previous;
            ProfilePictureStore.RemoveOwned(imported, AppearancePreferencesPath);
            throw;
        }
        ApplyProfilePictureResources();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
        ProfilePictureStore.RemoveOwned(previous, AppearancePreferencesPath);
    }

    private void ApplyProfilePictureResources()
    {
        var path = _appearanceSettings.ProfilePictureUri;
        if (_loadedProfilePicturePath == path) return;
        Avalonia.Media.Imaging.Bitmap? picture = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                picture = new Avalonia.Media.Imaging.Bitmap(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        var previous = _profilePicture;
        _profilePicture = picture;
        _loadedProfilePicturePath = path;
        Resources["ProfilePictureBrush"] = picture is null
            ? Avalonia.Media.Brushes.Transparent
            : new Avalonia.Media.ImageBrush(picture) { Stretch = Avalonia.Media.Stretch.UniformToFill };
        Resources["ProfilePictureMissing"] = picture is null;
        Resources["ProfilePicturePresent"] = picture is not null;
        // Let controls consume the new resource before releasing the old image.
        if (previous is not null) Avalonia.Threading.Dispatcher.UIThread.Post(previous.Dispose, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void ApplyTypographyResources()
    {
        TypographyPolicy.Apply(Resources, _appearanceSettings.SystemFontFamily, _appearanceSettings.TextScalePercent);
        ApplyProfilePictureResources();
        ApplyTaskbarSizeResources();
    }

    private void ApplyLanguageResources()
    {
        Loc.Apply(Resources, CurrentLanguageTag);
        LinuxLanguageService.ApplyProcessEnvironment(SystemLanguages.Resolve(CurrentLanguageTag));
    }

    public async Task<bool> SetLanguageAsync(string tag)
    {
        if (SystemLanguages.Find(tag) is not { } language) return false;
        if (_useDesktopSettings) return await ApplyDesktopSettingAsync("language", language.Tag);
        var previous = _appearanceSettings.LanguageTag;
        _appearanceSettings.LanguageTag = language.Tag;
        try { await PersistAppearanceAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _appearanceSettings.LanguageTag = previous;
            Feedback.Show(Loc.Get("The language preference could not be saved."));
            return false;
        }
        if (OperatingSystem.IsLinux())
        {
            var result = await new LinuxLanguageService(new SafeCommandRunner()).ApplyAsync(language.Tag);
            if (!result.Succeeded)
            {
                _appearanceSettings.LanguageTag = previous;
                await PersistAppearanceAsync();
                Feedback.Show(Loc.Get(result.Error ?? "The language could not be changed."));
                return false;
            }
        }
        ApplyLanguageResources();
        ApplyTypographyResources();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private async Task ApplyThemeVariantAsync(ThemeVariant theme)
    {
        RequestedThemeVariant = theme;
        SynchronizeSystemTheme(theme);
        ApplyAccessibilityPreferences();
        ApplyAccentPreference();
        _ = RefreshWallpaperLabelContrastAsync(CurrentWallpaperUri);

        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// DaisyOS renders immediately, then mirrors the resolved light/dark
    /// preference to the host desktop without putting a process round-trip on
    /// the interaction path. A newer choice cancels an older in-flight sync.
    /// </summary>
    private void SynchronizeSystemTheme(ThemeVariant theme)
    {
        _systemThemeSyncCancellation?.Cancel();
        _systemThemeSyncCancellation?.Dispose();
        _systemThemeSyncCancellation = new CancellationTokenSource();
        var cancellationToken = _systemThemeSyncCancellation.Token;
        var mode = theme == ThemeVariant.Light ? ThemeMode.Light : ThemeMode.Dark;
        _ = SynchronizeSystemThemeAsync(mode, cancellationToken);
    }

    private Task ExportWindowDecorationThemeAsync()
    {
        return _windowDecorationThemeService.ExportAsync(this);
    }

    private async Task SynchronizeSystemThemeAsync(ThemeMode mode, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _systemThemeService.ApplyAsync(mode, cancellationToken);
            if (!result.Succeeded)
            {
                Console.Error.WriteLine("[daisy-theme] Could not update every host desktop theme provider.");
            }
        }
        catch (OperationCanceledException)
        {
            // A newer explicit or automatic theme change superseded this one.
        }
        catch (Exception ex)
        {
            // Host-theme synchronization is best effort. DaisyOS's own theme
            // must remain responsive even on a minimal Linux installation.
            Console.Error.WriteLine($"[daisy-theme] Failed to synchronize the system theme: {ex.Message}");
        }
    }

    /// <summary>Shows or hides the launcher within the shell window.</summary>
    public void ToggleLauncher() => RequestLauncher(LauncherRequestSource.Explicit);

    private void RequestLauncher(LauncherRequestSource source)
    {
        if (_shellView is null)
        {
            return;
        }

        var decision = _launcherGesture.Decide(
            source,
            _shellView.IsLauncherVisible,
            IsFullscreenAppActive(),
            DateTime.UtcNow);

        switch (decision)
        {
            case LauncherGestureDecision.Close:
                HideLauncher();
                break;
            case LauncherGestureDecision.Open:
                ShowLauncher();
                break;
            case LauncherGestureDecision.Arm:
                ShowLauncherHint();
                break;
        }
    }

    /// <summary>True when the focused compositor window is a fullscreen client.</summary>
    private bool IsFullscreenAppActive() =>
        _compositorWindows?.Windows.Any(window => window.IsActive && window.IsFullScreen &&
            (_nativePrimaryOutputName is null || window.OutputName is null || window.OutputName == _nativePrimaryOutputName)) == true;

    /// <summary>
    /// True when a fullscreen client is present on the shell output. Backdrop selection uses this
    /// rather than the active window: opening the launcher moves keyboard focus
    /// away from the game, but the game still deserves the opaque material.
    /// </summary>
    private bool HasFullscreenWindow() => _compositorWindows is KWinWindowBridge bridge
        ? bridge.IsFullscreenOnOutput(_nativePrimaryOutputName)
        : _compositorWindows?.Windows.Any(window => window.IsFullScreen && !window.IsMinimized && window.IsOnCurrentWorkspace &&
            (_nativePrimaryOutputName is null || window.OutputName is null || window.OutputName == _nativePrimaryOutputName)) == true;

    /// <summary>
    /// Hides the launcher. <paramref name="restoreFocus"/> is false when the
    /// caller is deliberately handing focus to another window, so returning to
    /// the pre-launcher client would steal that new focus.
    /// </summary>
    public void HideLauncher(bool immediate = false, bool restoreFocus = true)
    {
        if (_shellView is null)
        {
            return;
        }

        _launcherPinchOpening = null;
        _launcherPinchTimeout?.Stop();
        HideLauncherHint();
        _launcherGesture.Reset();

        var returnWindowId = restoreFocus && !_launcherLaunchedApp ? _launcherReturnWindowId : null;
        _launcherReturnWindowId = null;
        _launcherLaunchedApp = false;

        if (!(_shellView.IsLauncherVisible || _shellView.Launcher.IsOpen || _shellView.Launcher.IsVisible))
        {
            RestoreLauncherFocus(returnWindowId);
            return;
        }

        _shellView.HideLauncher(immediate, () =>
        {
            _launcherBlur?.RefreshImmediately();
        });

        // Begin exit first so Launcher.IsOpen becomes false synchronously; then
        // release the native keyboard grab, pointer capture, and bottom-bar
        // launcher hold immediately without waiting for the fade to finish.
        LauncherVisibilityChanged?.Invoke(this, false);
        if (_nativeLauncherSurface is not null)
        {
            SetNativeSurfaceVisible(_nativeLauncherSurface, false, captureKeyboard: true);
        }
        RestoreLauncherFocus(returnWindowId);
    }

    private void ShowLauncher(bool interactive = false)
    {
        if (_shellView is null)
        {
            return;
        }

        HideQuickSettings();
        HideLauncherHint(immediate: true);
        _launcherGesture.Reset();
        // Capture the pre-launcher focus target before the overlay takes the
        // keyboard. The KWin snapshot may no longer report it once the layer
        // surface owns input.
        _launcherReturnWindowId = _compositorWindows?.Windows
            .FirstOrDefault(window => window.IsActive)?.Id;
        _launcherLaunchedApp = false;
        _shellView.Launcher.SetBackgroundWorkSuspended(SessionState.GamingMode || HasFullscreenWindow());
        _shellView.Launcher.SetSolidBackdrop(SessionState.GamingMode || HasFullscreenWindow());
        _shellView.ShowLauncher(interactive);
        LauncherVisibilityChanged?.Invoke(this, true);
    }

    private bool? _launcherPinchOpening;
    private double _launcherPinchStartProgress;
    private DispatcherTimer? _launcherPinchTimeout;

    private void HandleLauncherPinch(LauncherPinchUpdate update)
    {
        if (_shellView is null || IsLockScreenActive) return;
        var launcher = _shellView.Launcher;
        if (_launcherPinchOpening is null)
        {
            if (update.Finished || update.Progress <= 0) return;
            if (update.Opening == _shellView.IsLauncherVisible && launcher.RevealSettlingTarget != !update.Opening) return;
            _launcherPinchOpening = update.Opening;
            if (update.Opening && !_shellView.IsLauncherVisible) ShowLauncher(interactive: true);
            _launcherPinchStartProgress = launcher.BeginInteractiveReveal(update.Opening);
        }
        if (_launcherPinchOpening != update.Opening) return;
        launcher.UpdateInteractiveReveal(_launcherPinchStartProgress +
            ((update.Opening ? 1 : 0) - _launcherPinchStartProgress) * update.Progress);
        if (_launcherPinchTimeout is null)
        {
            // Recover if the compositor effect disappears or the bus fails mid-gesture.
            _launcherPinchTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _launcherPinchTimeout.Tick += OnLauncherPinchTimeout;
        }
        _launcherPinchTimeout.Stop();
        if (!update.Finished)
        {
            _launcherPinchTimeout.Start();
            return;
        }
        FinishLauncherPinch(update.Commit ? update.Opening : !update.Opening,
            ((update.Opening ? 1 : 0) - _launcherPinchStartProgress) * update.Velocity);
    }

    private void OnLauncherPinchTimeout(object? sender, EventArgs e)
    {
        if (_launcherPinchOpening is { } opening) FinishLauncherPinch(!opening);
    }

    private void FinishLauncherPinch(bool visible, double velocity = 0)
    {
        _launcherPinchTimeout?.Stop();
        _launcherPinchOpening = null;
        _shellView?.Launcher.EndInteractiveReveal(visible, () =>
        {
            if (visible) _shellView?.Launcher.FocusSearch();
            else HideLauncher(immediate: true);
        }, velocity);
    }

    private void RestoreLauncherFocus(string? windowId)
    {
        if (string.IsNullOrEmpty(windowId) || _compositorWindows is null)
        {
            return;
        }

        var window = _compositorWindows.Windows.FirstOrDefault(candidate => candidate.Id == windowId);
        if (window is null)
        {
            return;
        }

        _ = _compositorWindows.ActivateOrMinimizeAsync(window.Id, wasActive: false);
    }

    private void ShowLauncherHint()
    {
        if (_launcherHint is null)
        {
            return;
        }

        _launcherHint.ShowHint();
        if (_launcherHintSurface is { } surface)
        {
            if (!surface.IsVisible)
            {
                surface.Show();
                LayerShellWindow.SetInputPassthrough(surface, true);
                LayerShellWindow.SetKeyboardInteractivity(
                    surface,
                    ZwlrLayerSurfaceV1.KeyboardInteractivity.None);
            }
        }

        _launcherHintTimer ??= new DispatcherTimer
        {
            Interval = LauncherGesturePolicy.ConfirmationWindow
        };
        _launcherHintTimer.Stop();
        _launcherHintTimer.Tick -= OnLauncherHintTimeout;
        _launcherHintTimer.Tick += OnLauncherHintTimeout;
        _launcherHintTimer.Start();
    }

    private void OnLauncherHintTimeout(object? sender, EventArgs e)
    {
        _launcherHintTimer?.Stop();
        _launcherGesture.Reset();
        HideLauncherHint();
    }

    private void HideLauncherHint(bool immediate = false)
    {
        _launcherHintTimer?.Stop();
        if (immediate) _launcherHint?.HideImmediately();
        else _launcherHint?.Dismiss();
    }

    private void ConfigureShellOverlays(ShellView shell)
    {
        _shellView = shell;
        shell.BottomChromeStateChanged += (_, _) => ApplyNativeBottomChromeState(shell);
        shell.SetBottomBarAutoHide(_appearanceSettings.TaskbarAutoHide, _appearanceSettings.SystemBarAutoHide, _appearanceSettings.MediaWidgetAutoHide);
        shell.Taskbar.ApplyOrder(SessionState.DockOrder);
        shell.Taskbar.OrderChanged += (_, order) => SessionState.SetDockOrder(order);
        shell.Taskbar.StartButtonClicked += (_, _) => ToggleLauncher();
        shell.Taskbar.AppIconClicked += (_, appId) =>
            LaunchTaskbarApp(appId, shell.Taskbar.TakeWasActiveAtActivation(appId));
        shell.Taskbar.ForceQuitRequested += (_, appId) => RequestForceQuit(appId);
        shell.Taskbar.AppInfoRequested += (_, appId) => ShowSettings("InstalledApps");
        shell.Taskbar.CloseAppRequested += (_, appId) => RequestClose(appId);
        foreach (var appId in NativeAppIds)
        {
            shell.Taskbar.SetAppWindowState(appId, _nativeWindowTracker.GetState(appId));
        }
        shell.SystemBar.QuickSettingsRequested += (_, _) => ToggleQuickSettings();
        shell.SystemBar.QuickSettingsDismissRequested += (_, _) => HideQuickSettings();
        shell.SystemBar.ConsoleModeRequested += (_, _) => shell.RequestConsoleMode();
        shell.Launcher.AppLaunchRequested += (_, _) =>
        {
            _launcherLaunchedApp = true;
            HideLauncher();
        };
        LauncherVisibilityChanged += (_, isVisible) => shell.SetLauncherOpen(isVisible);
    }

    internal static void BindBottomEdgeReveal(Window surface, ShellView shell)
    {
        void Reveal(PointerEventArgs args)
        {
            var point = args.GetPosition(surface);
            if (point.Y >= surface.Bounds.Height - 2)
                shell.RevealBottomBarAt(point.X, surface.Bounds.Width);
        }
        surface.AddHandler(InputElement.PointerMovedEvent, (_, args) => Reveal(args),
            RoutingStrategies.Bubble, handledEventsToo: true);
        surface.AddHandler(InputElement.PointerEnteredEvent, (_, args) => Reveal(args),
            RoutingStrategies.Direct, handledEventsToo: true);
    }

    private void ApplyNativeBottomChromeState(ShellView shell)
    {
        if (_nativePanelSurface is { } panelSurface)
        {
            LayerShellWindow.SetExclusiveZone(
                panelSurface,
                shell.IsExternalFullscreenActive || shell.CanReleaseBottomWorkArea
                    ? 0
                    : ShellSurfacePlan.PrimaryPanelExclusiveZone);
            LayerShellWindow.SetInputPassthrough(panelSurface, shell.IsBottomPanelInputPassthrough);
        }
        if (_nativeBottomEdgeSensorSurface is { } sensorSurface)
            LayerShellWindow.SetInputPassthrough(sensorSurface, !shell.IsBottomEdgeSensorEnabled);
    }

    private void StartCompositorBridge(ShellView shell)
    {
        if (!IsNativeShellSession || _compositorWindows is not null)
        {
            return;
        }

        var kwinBridge = new KWinWindowBridge();
        _compositorWindows = kwinBridge;
        _launchFeedback?.Dispose();
        _launchFeedback = new LaunchFeedbackController(shell.Taskbar, kwinBridge);
        _ = Task.Run(() => GetExternalAppLookup(_sharedLauncherService.Value));
        kwinBridge.ScreenshotRequested += (_, action) => Dispatcher.UIThread.Post(() => _ = HandleScreenshotRequestAsync(action));
        kwinBridge.LockRequested += (_, _) => Dispatcher.UIThread.Post(() => LockSession());
        var pinchDispatcher = new LauncherPinchDispatcher(
            callback => Dispatcher.UIThread.Post(callback, DispatcherPriority.BeforeRender), HandleLauncherPinch);
        kwinBridge.LauncherPinchChanged += (_, update) => pinchDispatcher.Push(update);
        kwinBridge.OverviewRequested += (_, _) => Dispatcher.UIThread.Post(() => ToggleOverview());
        kwinBridge.MediaActionRequested += (_, action) => Dispatcher.UIThread.Post(() => _ = HandleMediaActionAsync(action));
        kwinBridge.ShellActionRequested += (_, action) => Dispatcher.UIThread.Post(() => HandleShellAction(action));
        kwinBridge.WindowsChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(shell, _shellView))
            {
                Volatile.Write(ref _gameTelemetryWindowPid,
                    kwinBridge.Windows.FirstOrDefault(window => window.IsActive)?.ProcessId ?? 0);
                _recentPrefetch.SetSuspended(SessionState.GamingMode || kwinBridge.HasFullscreenWindow);
                shell.Launcher.SetBackgroundWorkSuspended(SessionState.GamingMode || kwinBridge.HasFullscreenWindow);
                foreach (var window in kwinBridge.Windows.Where(window => window.IsActive)) _recentPrefetch.Observe(window);
                shell.Taskbar.SetCompositorWindows(kwinBridge.Windows);
                CompositorWindowsChanged?.Invoke(this, EventArgs.Empty);
                DesktopOcclusionChanged?.Invoke(this, EventArgs.Empty);
                ReportUnresponsiveApplications(kwinBridge.Windows);
                var isFullscreen = kwinBridge.IsFullscreenOnOutput(_nativePrimaryOutputName);
                var desktopOccluded = kwinBridge.IsCoveredOnOutput(_nativePrimaryOutputName);
                SetExternalDesktopOcclusion(desktopOccluded);
                var hasOpenExternal = kwinBridge.Windows.Any(window => !window.IsMinimized && window.IsOnCurrentWorkspace);
                if (_hasAnyOpenExternalWindow != hasOpenExternal)
                {
                    _hasAnyOpenExternalWindow = hasOpenExternal;
                    DesktopOcclusionChanged?.Invoke(this, EventArgs.Empty);
                }
                shell.SetExternalFullscreenActive(isFullscreen);
                if (shell.IsLauncherVisible)
                {
                    shell.Launcher.SetSolidBackdrop(
                        SessionState.GamingMode || HasFullscreenWindow());
                }
                ApplyNativeBottomChromeState(shell);
            }
        });
        _ = kwinBridge.StartAsync();
    }

    private static Task HandleMediaActionAsync(string action) => action switch
    {
        "play-pause" => LinuxMediaSessionService.Shared.PlayPauseAsync(),
        "next" => LinuxMediaSessionService.Shared.NextAsync(),
        "previous" => LinuxMediaSessionService.Shared.PreviousAsync(),
        _ => Task.CompletedTask
    };

    private void HandleShellAction(string action)
    {
        if (action.StartsWith("volume-", StringComparison.Ordinal) || action == "microphone-mute")
        {
            if (_volumeKeys is not null) _ = _volumeKeys.HandleAsync(action);
            return;
        }
        if (action.StartsWith("files-share:", StringComparison.Ordinal))
        {
            try { ShowLocalSend(global::System.Text.Json.JsonSerializer.Deserialize(action[12..], Services.Serialization.DaisyShellJsonContext.Default.StringArray)); }
            catch (global::System.Text.Json.JsonException) { Feedback.Show(Loc.Get("Couldn’t read the file sharing request.")); }
            return;
        }
        if (action.StartsWith("app-feedback:", StringComparison.Ordinal))
        {
            Feedback.Show(action[13..]);
            return;
        }
        if (action.StartsWith("activate-app:", StringComparison.Ordinal))
        {
            var id = "org.daisyos." + action[13..];
            var window = _compositorWindows?.Windows.FirstOrDefault(window => window.AppId == id || window.AppId == id + ".desktop");
            if (window is not null) _ = _compositorWindows!.ActivateOrMinimizeAsync(window.Id, wasActive: false);
            return;
        }
        switch (action)
        {
            case "activate-files":
                var files = _compositorWindows?.Windows.FirstOrDefault(window => window.AppId is "files" or "org.daisyos.files" or "org.daisyos.files.desktop");
                if (files is not null) _ = _compositorWindows!.ActivateOrMinimizeAsync(files.Id, wasActive: false);
                break;
            case "gaming-overlay":
                if (IsLockScreenActive) break;
                _gamingOverlay?.Toggle();
                if (_gamingOverlay?.IsVisible == true && _gamingOverlaySurface is { } gamingSurface)
                {
                    gamingSurface.Show();
                    LayerShellWindow.SetInputPassthrough(gamingSurface, true);
                }
                break;
            case "launcher-meta":
                RequestLauncher(LauncherRequestSource.Meta);
                break;
            case "launcher":
                RequestLauncher(LauncherRequestSource.Explicit);
                break;
            case "trash":
                _ = ShowTrashAsync();
                break;
            case "files":
                OpenFilesWindow();
                break;
            case "display-settings":
                ShowSettings("Displays");
                break;
            case "settings":
                ShowSettings();
                break;
            case "notifications":
                if (_shellView?.SystemBar.IsNotificationPanelVisible == true)
                {
                    _shellView.SystemBar.HideNotificationPanel();
                }
                else
                {
                    _shellView?.SystemBar.ShowNotificationPanel();
                }
                break;
            case "app-tray":
                if (_shellView?.SystemBar.IsAppTrayVisible == true) _shellView.SystemBar.HideAppTrayPanel();
                else _shellView?.SystemBar.ShowAppTrayPanel();
                break;
            case "clipboard":
                ShowClipboardHistory();
                break;
            case "new-note":
                ShowNewNote();
                break;
            case "task-manager":
                ShowTaskManager();
                break;
        }
    }

    public void ShowScreenshotTool() => _ = ShowScreenshotToolAsync();

    public async Task ShowScreenshotToolAsync(ScreenshotCaptureMode mode = ScreenshotCaptureMode.Area)
    {
        if (IsLockScreenActive || _shellView is null || _isScreenshotOpening) return;
        if (_shellView.Screenshot.IsOpen) return;

        _isScreenshotOpening = true;
        try
        {
            // Capture the screen snapshot before making the screenshot overlay surface visible.
            // This captures the exact instant Print Screen was pressed, cleanly and without any overlay chrome.
            var timing = global::System.Diagnostics.Stopwatch.StartNew();
            var snapshotPath = await _screenshotService.CaptureScreenSnapshotAsync();
            var capturedMs = timing.Elapsed.TotalMilliseconds;
            if (IsLockScreenActive)
            {
                DaisyScreenshotService.DeleteFile(snapshotPath);
                return;
            }
            if (!await _shellView.Screenshot.OpenAsync(snapshotPath, mode)) return;
            if (!_shellView.Screenshot.IsOpen || IsLockScreenActive)
            {
                _shellView.Screenshot.Close();
                return;
            }
            var preparedMs = timing.Elapsed.TotalMilliseconds;
            _nativeScreenshotBackdropSurface?.Show();
            if (_nativeScreenshotSurface is { } screenshotSurface)
                SetNativeSurfaceVisible(screenshotSurface, true, captureKeyboard: true);
            _shellView.Screenshot.Focus();
            if (TopLevel.GetTopLevel(_shellView.Screenshot) is { } topLevel)
                topLevel.RequestAnimationFrame(_ => global::System.Console.Error.WriteLine(
                    $"[screenshot-startup] capture={capturedMs:F0}ms decode={preparedMs - capturedMs:F0}ms render-ready={timing.Elapsed.TotalMilliseconds:F0}ms"));
        }
        finally
        {
            _isScreenshotOpening = false;
        }
    }

    private Task HandleScreenshotRequestAsync(string action)
    {
        // The same global shortcut is also a quick way to dismiss the tool.
        if (_shellView?.Screenshot.IsOpen == true)
        {
            _shellView.Screenshot.Close();
            return Task.CompletedTask;
        }
        return ShowScreenshotToolAsync(action switch
        {
            "screen" => ScreenshotCaptureMode.Screen,
            "window" => ScreenshotCaptureMode.Window,
            _ => ScreenshotCaptureMode.Area
        });
    }

    public void ToggleOverview()
    {
        if (_shellView is null || _compositorWindows is not { } compositor) return;
        DismissTransientShellSurfaces();
        _ = compositor.ToggleOverviewEffectAsync();
    }

    private void StartNotificationDaemonService()
    {
        if (_notificationDaemonService is not null)
        {
            return;
        }

        var service = new DaisyOS.Shell.Services.Notifications.FreedesktopNotificationDaemonService(Notifications);
        _notificationDaemonService = service;
        _ = service.StartAsync();
    }

    private void StartLocalSendService()
    {
        // Do all construction and listener startup away from Avalonia's
        // initialization path. A transfer service is useful, but it must never
        // be part of the desktop's availability boundary.
        _localSendStartTask = Task.Run(async () =>
        {
            try
            {
                var service = new LocalSendService();
                service.ReceivingStateChanged += (_, _) => Dispatcher.UIThread.Post(() =>
                    LocalSendReceivingChanged?.Invoke(this, service.IsReceivingEnabled));
                service.IncomingTransferRequested += (_, transfer) => Dispatcher.UIThread.Post(() =>
                {
                    Notifications.Add(new NotificationItem(
                        $"{transfer.Sender.Alias} wants to send files",
                        transfer.Summary,
                        DateTimeOffset.Now,
                        SourceAppId: "LocalSend",
                        SystemIconGlyph: "local-send",
                        Category: NotificationCategory.Network,
                        // Keep the consent prompt on screen until it is
                        // answered or the peer's two-minute request expires.
                        Urgency: NotificationUrgency.High,
                        ActionLabel: "Accept",
                        ActionId: $"localsend-accept:{transfer.Id}",
                        SecondaryActionLabel: "Decline",
                        SecondaryActionId: $"localsend-decline:{transfer.Id}",
                        PreviewImage: GetLocalSendInlineImagePreview(transfer)));
                });
                service.TransferProgressChanged += (_, progress) =>
                {
                    if (progress.Status == "Completed" && progress.IsIncoming && !string.IsNullOrWhiteSpace(progress.DestinationPath))
                    {
                        Dispatcher.UIThread.Post(() => Notifications.Add(new NotificationItem(
                            "File received", Path.GetFileName(progress.DestinationPath), DateTimeOffset.Now,
                            SourceAppId: "LocalSend", SystemIconGlyph: "local-send", Category: NotificationCategory.Network,
                            Urgency: NotificationUrgency.High,
                            ActionLabel: "Show in Files", ActionId: $"localsend-reveal:{progress.DestinationPath}",
                            PreviewImage: IsNotificationPreviewImage(progress.DestinationPath) ? progress.DestinationPath : null,
                            TimeoutSeconds: 15)));
                    }
                };
                await service.StartAsync().ConfigureAwait(false);
                _localSendService = service;
                _localSendUnavailable = false;
                Dispatcher.UIThread.Post(() =>
                {
                    _localSendWindow?.AttachService(service);
                    LocalSendReceivingChanged?.Invoke(this, false);
                });
            }
            catch (Exception ex)
            {
                // Keep diagnostics useful without dumping an optional-service
                // stack trace into the normal desktop startup log.
                Console.Error.WriteLine($"[localsend] LocalSend is unavailable: {ex.GetType().Name}: {ex.Message}");
                _localSendUnavailable = true;
                Dispatcher.UIThread.Post(() => _localSendWindow?.AttachService(null, unavailable: true));
                Dispatcher.UIThread.Post(() => Feedback.Show(Loc.Get("LocalSend couldn’t start. The desktop is still available.")));
            }
        });
    }

    // LocalSend transfers have not been downloaded when the consent prompt is
    // shown. Only render a sender-provided image preview; never invent a
    // thumbnail by reading a file before the user accepts it.
    private static string? GetLocalSendInlineImagePreview(LocalSendIncomingTransfer transfer) =>
        transfer.Files.Count == 1 && transfer.Files[0].PreviewImage is { } preview &&
        preview.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            ? preview
            : null;

    private static bool IsNotificationPreviewImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp";

    /// <summary>
    /// Opens a short, deliberate LocalSend receiving window. Discovery stays
    /// available for sending, but incoming uploads are rejected outside this
    /// user-controlled ten-minute window.
    /// </summary>
    public void SetLocalSendReceivingEnabled(bool enabled)
    {
        var service = _localSendService;
        if (service is null && _localSendStartTask is { IsCompleted: false })
        {
            _ = Task.Run(async () =>
            {
                try { await _localSendStartTask.ConfigureAwait(false); } catch { }
                Dispatcher.UIThread.Post(() => SetLocalSendReceivingEnabled(enabled));
            });
            return;
        }

        if (service is null || _localSendUnavailable || !service.IsRunning)
        {
            Feedback.Show(Loc.Get("LocalSend is still starting."));
            LocalSendReceivingChanged?.Invoke(this, false);
            return;
        }

        if (enabled)
        {
            service.EnableReceivingFor(TimeSpan.FromMinutes(10));
            Feedback.Show(Loc.Get("LocalSend receiving is on for 10 minutes."));
        }
        else
        {
            service.DisableReceiving();
            Feedback.Show(Loc.Get("LocalSend receiving is off."));
        }
    }

    private void OnGamingPrefetchPolicyChanged(object? sender, EventArgs e)
    {
        _shellView?.Launcher.SetBackgroundWorkSuspended(SessionState.GamingMode || HasFullscreenWindow());
        _recentPrefetch.SetSuspended(SessionState.GamingMode ||
            (_compositorWindows is KWinWindowBridge bridge && bridge.HasFullscreenWindow));
        if (_shellView?.IsLauncherVisible == true)
        {
            _shellView.Launcher.SetSolidBackdrop(
                SessionState.GamingMode || HasFullscreenWindow());
        }
        if (_lastGamingModeForTrim != SessionState.GamingMode)
        {
            _lastGamingModeForTrim = SessionState.GamingMode;
            if (SessionState.GamingMode) RequestHiddenShellMemoryTrim();
        }
    }

    private void SetExternalDesktopOcclusion(bool isOccluded)
    {
        if (_externalDesktopOccluded == isOccluded) return;
        _externalDesktopOccluded = isOccluded;
        DesktopOcclusionChanged?.Invoke(this, EventArgs.Empty);
        if (isOccluded) RequestHiddenShellMemoryTrim();
    }

    private bool _lastGamingModeForTrim;
    private ShellMemoryTrimmer? _memoryTrimmer;

    /// <summary>Releases decoded images held only by hidden shell views. UI thread only.</summary>
    private void ReclaimHiddenShellCaches()
    {
        _shellView?.Launcher.ReclaimHiddenIcons();
        if (_appStoreWindow is null && _appStoreSession is { HasActiveOperations: false } store)
        {
            _appStoreSession = null;
            store.Dispose();
        }
    }

    /// <summary>True while no part of the shell is visible: a game or fullscreen app covers it, it's locked, or the display is off.</summary>
    private bool IsShellHiddenFromUser() =>
        Volatile.Read(ref _displayIsOff) || SessionState.GamingMode || _externalDesktopOccluded || _lockScreenWindow is not null;

    private void StartMemoryTrimmer()
    {
        if (!OperatingSystem.IsLinux()) return;
        _memoryTrimmer = new ShellMemoryTrimmer(
            // Read from a timer thread; these are simple flag reads.
            () => IsShellHiddenFromUser() && ReleaseMemoryWhenHiddenEnabled(),
            () => Dispatcher.UIThread.Invoke(ReclaimHiddenShellCaches));
    }

    private bool ReleaseMemoryWhenHiddenEnabled() => Volatile.Read(ref _releaseMemoryWhenHidden);
    private bool _releaseMemoryWhenHidden = true;

    private void RequestHiddenShellMemoryTrim()
    {
        if (ReleaseMemoryWhenHiddenEnabled()) _memoryTrimmer?.RequestTrim();
    }

    private void ToggleQuickSettings()
    {
        if (_shellView?.SystemBar.IsQuickSettingsVisible == true)
        {
            HideQuickSettings();
            return;
        }

        if (_shellView is null)
        {
            return;
        }

        HideLauncher(restoreFocus: false);
        _shellView.SystemBar.ShowQuickSettingsPanel();
    }

    private void HideQuickSettings()
    {
        _shellView?.SystemBar.HideQuickSettingsPanel();
    }

    public bool DismissTransientShellSurfaces()
    {
        if (_nativeDesktopMenus?.IsOpen == true)
        {
            _nativeDesktopMenus.Close();
            return true;
        }

        if (_shellView?.Launcher.IsAppContextMenuVisible == true)
        {
            _shellView.Launcher.CloseAppContextMenu();
            return true;
        }

        if (_shellView?.Desktop.IsWidgetContextMenuVisible == true)
        {
            _shellView.Desktop.HideWidgetContextMenu();
            return true;
        }

        if (_shellView?.MediaWidget.IsContextMenuVisible == true)
        {
            _shellView.MediaWidget.HideContextMenu();
            return true;
        }

        if (_shellView?.Taskbar.IsContextMenuVisible == true)
        {
            _shellView.Taskbar.HideContextMenu();
            return true;
        }

        if (_shellView?.Desktop.IsWidgetLibraryOpen == true)
        {
            _shellView.Desktop.DismissWidgetLibrary();
            return true;
        }

        if (_shellView?.IsMetricsPanelVisible == true)
        {
            _shellView.HideMetricsPanel();
            return true;
        }

        if (_shellView?.SystemBar.IsQuickSettingsVisible == true)
        {
            HideQuickSettings();
            return true;
        }

        if (_shellView?.SystemBar.IsAppTrayVisible == true)
        {
            _shellView.SystemBar.HideAppTrayPanel();
            return true;
        }

        if (_shellView?.SystemBar.IsCalendarPanelVisible == true)
        {
            _shellView.SystemBar.HideCalendarPanel();
            return true;
        }

        if (_shellView?.SystemBar.IsNotificationPanelVisible == true)
        {
            _shellView.SystemBar.HideNotificationPanel();
            return true;
        }

        if (_shellView?.IsLauncherVisible == true)
        {
            HideLauncher();
            return true;
        }

        return false;
    }

    public async Task<bool> TryActivateExternalApplicationAsync(string appId)
    {
        // Launcher activation never minimizes an already active window.
        if (_compositorWindows is null || _shellView?.Taskbar.TryGetCompositorWindowId(appId, out var id) != true) return false;
        await _compositorWindows.ActivateOrMinimizeAsync(id, false);
        return true;
    }

    private void LaunchTaskbarApp(string appId, bool? wasActiveAtActivation = null)
    {
        // A physical double tap must still mean one taskbar activation. Without
        // this gate the second release can arrive before KWin publishes the
        // first window, launching a duplicate, or immediately minimize the
        // window that the first tap just focused.
        var activationTime = DateTimeOffset.UtcNow;
        if (_recentTaskbarActivations.TryGetValue(appId, out var previousActivation)
            && (activationTime - previousActivation).TotalMilliseconds < 550)
        {
            return;
        }
        _recentTaskbarActivations[appId] = activationTime;

        HideLauncher(restoreFocus: false);
        _shellView?.Taskbar.HideContextMenu();

        // An owned window has an immediate, local activation state. Handle an
        // active owned app before consulting KWin so a taskbar click cannot be
        // mistaken for a restore while the compositor snapshot is catching up.
        // This preserves the familiar focused -> minimize contract.
        if (NativeAppIds.Contains(appId, StringComparer.Ordinal)
            && _nativeWindowTracker.GetState(appId) == NativeAppWindowState.Active
            && _nativeWindowTracker.ToggleFromTaskbar(appId))
        {
            return;
        }

        if (_shellView?.Taskbar.TryGetCompositorWindowId(appId, out var compositorWindowId) == true
            && _compositorWindows is not null)
        {
            var wasActive = wasActiveAtActivation ?? _compositorWindows.Windows
                .FirstOrDefault(w => string.Equals(w.Id, compositorWindowId, StringComparison.Ordinal))
                ?.IsActive == true;
            _ = _compositorWindows.ActivateOrMinimizeAsync(compositorWindowId, wasActive);
            return;
        }

        if (appId.StartsWith("kwin:", StringComparison.Ordinal) && _compositorWindows is not null)
        {
            var rawId = appId[5..];
            var wasActive = wasActiveAtActivation ?? _compositorWindows.Windows
                .FirstOrDefault(w => string.Equals(w.Id, rawId, StringComparison.Ordinal))
                ?.IsActive == true;
            _ = _compositorWindows.ActivateOrMinimizeAsync(rawId, wasActive);
            return;
        }

        if (_nativeWindowTracker.ToggleFromTaskbar(appId))
        {
            return;
        }

        if (TryLaunchSystemApp(appId))
        {
            return;
        }

        _ = LaunchApplicationAsync(appId, _shellView?.Taskbar.TakeActivationToken(appId));
    }

    /// <summary>
    /// Attempts to launch a built-in DaisyOS system app if the specified ID matches.
    /// </summary>
    public bool TryLaunchSystemApp(string appId)
    {
        var normalized = appId?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^8];
        }
        if (normalized.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["org.daisyos.".Length..];
        }

        if (normalized is not ("settings" or "appstore") && DesktopApplicationMaskStore.IsMasked(normalized))
        {
            return false;
        }

        switch (normalized)
        {
            case "settings":
                ShowSettings();
                return true;
            case "mail":
                LaunchMail();
                return true;
            case "notes":
                ShowNotes();
                return true;
            case "trash":
                _ = ShowTrashAsync();
                return true;
            case "files":
                ShowFiles((string?)null);
                return true;
            case "calculator":
                ShowCalculator();
                return true;
            case "calendar":
                ShowCalendar();
                return true;
            case "appstore":
                ShowAppStore();
                return true;
            case "screenshot":
                ShowScreenshotTool();
                return true;
            case "diskmanager":
                ShowDiskManager();
                return true;
            case "appinstaller":
                ShowAppInstaller();
                return true;
            case "taskmanager":
            case "systemmonitor":
            case "system-monitor":
            case "diagnostics":
                ShowTaskManager();
                return true;
            case "uikit":
            case "ui-kit":
                ShowUIKit();
                return true;
            default:
                return false;
        }
    }

    private LaunchFeedbackController? _launchFeedback;
    private IReadOnlyList<AppEntry>? _externalCatalog;
    private Dictionary<string, AppEntry>? _externalLookup;
    private readonly object _externalLookupGate = new();
    private Dictionary<string, AppEntry> GetExternalAppLookup(DaisyOS.System.Processes.LinuxAppLauncherService service)
    {
        var catalog = service.GetAvailableApps();
        lock (_externalLookupGate)
        {
            if (!ReferenceEquals(catalog, _externalCatalog))
            { _externalLookup = BuildAppLookupIndex(catalog); _externalCatalog = catalog; }
            return _externalLookup!;
        }
    }

    // Shared by taskbar and desktop application activation. Existing windows are
    // raised from desktop shortcuts; only the taskbar toggles minimization.
    public async Task LaunchApplicationAsync(string appId, Task<string>? activation = null)
    {
        if (await TryActivateExternalApplicationAsync(appId)) return;
        if (TryLaunchSystemApp(appId)) return;
        await LaunchExternalApplicationAsync(appId, activation);
    }

    internal static string[] DesktopLaunchMetadata(string path)
    {
        var values = new List<string>();
        var mainGroup = false;
        foreach (var line in File.ReadLines(path))
        {
            var value = line.Trim();
            if (value.StartsWith('[')) { mainGroup = value == "[Desktop Entry]"; continue; }
            if (!mainGroup || value.StartsWith('#') || !value.Contains('=')) continue;
            var key = value[..value.IndexOf('=')];
            var label = key.Split('[')[0];
            if (label is "Name" or "GenericName" or "Comment" or "Icon" or "Keywords" or "Categories" or "MimeType" or "Version" or "Actions") continue;
            values.Add(value);
        }
        return values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    public async Task LaunchDesktopApplicationAsync(string path, Task<string>? activation = null)
    {
        var id = Path.GetFileName(path);
        var installed = await Task.Run(() => GetExternalAppLookup(_sharedLauncherService.Value));
        // Use the cached identity only when the shortcut launch metadata matches the installed
        // entry. Edited/custom desktop files retain their own Exec and metadata.
        var same = await Task.Run(() =>
        {
            var roots = new[] { Environment.GetEnvironmentVariable("XDG_DATA_HOME") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share") }
                .Concat((Environment.GetEnvironmentVariable("XDG_DATA_DIRS") ?? "/usr/local/share:/usr/share")
                    .Split(':', StringSplitOptions.RemoveEmptyEntries));
            var text = DesktopLaunchMetadata(path);
            roots = roots.Concat(new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "flatpak", "exports", "share"), "/var/lib/flatpak/exports/share" });
            foreach (var root in roots)
            {
                var candidate = Path.Combine(root, "applications", id);
                if (File.Exists(candidate)) return text.SequenceEqual(DesktopLaunchMetadata(candidate));
            }
            return false;
        });
        if (same && installed.ContainsKey(id)) { await LaunchApplicationAsync(id, activation); return; }
        var result = await _sharedLauncherService.Value.LaunchDesktopEntryFileAsync(path,
            activationToken: activation is null ? "" : await activation);
        if (!result.Succeeded) Feedback.Show(Loc.Get(result.Message));
    }

    private async Task LaunchExternalApplicationAsync(string appId, Task<string>? activation = null)
    {
        try
        {
            var launcherService = _sharedLauncherService.Value;
            var index = await Task.Run(() => GetExternalAppLookup(launcherService));

            // O(1) lookup by app ID, ID without suffix, name, executable basename, or icon name.
            // The index covers all six identity strategies that were previously checked via linear scan.
            if (index.TryGetValue(appId, out var app)
                || index.TryGetValue($"{appId}.desktop", out app))
            {
                // The launch daemon normally owns this lease. Its fallback path
                // must use the same native busy cursor as desktop activation too.
                var localLaunchId = !DaisyLaunchClient.Enabled
                    ? _launchFeedback?.BeginLocalLaunch(app.Id)
                    : null;
                var result = await launcherService.LaunchWithActivationAsync(app, activation is null ? "" : await activation);
                if (!result.Succeeded && localLaunchId is not null)
                    _launchFeedback?.LocalLaunchFailed(localLaunchId);
                if (!result.Succeeded) Feedback.Show(Loc.Get(result.Message));
                return;
            }

            // Ultimate fallback: try launching the raw appId as a command.
            Feedback.Show(Loc.Get("This desktop shortcut is no longer available."));
        }
        catch (Exception ex)
        {
            global::System.Console.Error.WriteLine($"[daisy-taskbar] Failed to launch external app '{appId}': {ex.Message}");
        }
    }

    public void RequestClose(string appId)
    {
        if (_nativeWindowTracker.Close(appId))
        {
            return;
        }

        if (_shellView?.Taskbar.TryGetCompositorWindowId(appId, out var windowId) == true && _compositorWindows is not null)
        {
            _ = _compositorWindows.CloseAsync(windowId);
        }
        else if (appId.StartsWith("kwin:", StringComparison.Ordinal) && _compositorWindows is not null)
        {
            _ = _compositorWindows.CloseAsync(appId[5..]);
        }
    }

    private void RequestForceQuit(string appId)
    {
        if (_shellView?.Taskbar.TryGetCompositorWindowId(appId, out var windowId) != true)
        {
            return;
        }

        _ = ForceQuitWindowAsync(windowId);
    }

    private void ReportUnresponsiveApplications(IEnumerable<CompositorWindowRecord> windows)
    {
        var current = windows.Where(window => window.IsUnresponsive).Select(window => window.Id).ToHashSet(StringComparer.Ordinal);
        _reportedUnresponsiveWindows.RemoveWhere(windowId => !current.Contains(windowId));
        foreach (var window in windows.Where(window => window.IsUnresponsive))
        {
            if (!_reportedUnresponsiveWindows.Add(window.Id))
            {
                continue;
            }

            Notifications.Add(new NotificationItem(
                $"{window.DisplayName} isn’t responding",
                "You can force quit it. Unsaved changes may be lost.",
                DateTimeOffset.Now,
                SourceAppId: window.DisplayName,
                SystemIconGlyph: "warning-circle",
                IsError: true,
                Category: NotificationCategory.Application,
                Urgency: NotificationUrgency.High,
                ActionLabel: "Force quit",
                ActionId: $"force-quit:{window.Id}"));
        }
    }

    private void OnNotificationActionTriggered(object? sender, NotificationItem notification)
    {
        const string airPodsPrefix = "airpods-connect:";
        if (notification.ActionId?.StartsWith(airPodsPrefix, StringComparison.Ordinal) == true)
        {
            if (AirPods is { } pods) _ = pods.ConnectAsync(notification.ActionId[airPodsPrefix.Length..]);
            return;
        }
        const string forceQuitPrefix = "force-quit:";
        if (notification.ActionId?.StartsWith(forceQuitPrefix, StringComparison.Ordinal) == true)
        {
            _ = ForceQuitWindowAsync(notification.ActionId[forceQuitPrefix.Length..]);
            return;
        }
        const string localSendAcceptPrefix = "localsend-accept:";
        if (notification.ActionId?.StartsWith(localSendAcceptPrefix, StringComparison.Ordinal) == true)
        {
            _ = DecideLocalSendIncomingAsync(notification.ActionId[localSendAcceptPrefix.Length..], accept: true);
            return;
        }
        const string localSendDeclinePrefix = "localsend-decline:";
        if (notification.ActionId?.StartsWith(localSendDeclinePrefix, StringComparison.Ordinal) == true)
        {
            _ = DecideLocalSendIncomingAsync(notification.ActionId[localSendDeclinePrefix.Length..], accept: false);
            return;
        }
        const string localSendRevealPrefix = "localsend-reveal:";
        if (notification.ActionId?.StartsWith(localSendRevealPrefix, StringComparison.Ordinal) == true)
        {
            ShowFiles(new FileManagerActivationRequest([notification.ActionId[localSendRevealPrefix.Length..]], true));
        }
    }

    private async Task DecideLocalSendIncomingAsync(string transferId, bool accept)
    {
        var service = _localSendService;
        if (service is null)
        {
            Feedback.Show(Loc.Get("LocalSend is unavailable right now."));
            return;
        }

        try
        {
            if (!await service.DecideIncomingAsync(transferId, accept).ConfigureAwait(false))
            {
                Dispatcher.UIThread.Post(() => Feedback.Show(Loc.Get("That LocalSend request has expired.")));
            }
            else if (!accept)
            {
                Dispatcher.UIThread.Post(() => Feedback.Show(Loc.Get("LocalSend transfer declined.")));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[localsend] Could not decide incoming transfer: {ex.GetType().Name}: {ex.Message}");
            Dispatcher.UIThread.Post(() => Feedback.Show(Loc.Get("LocalSend couldn’t process that request.")));
        }
    }

    private async Task ForceQuitWindowAsync(string windowId)
    {
        var service = _compositorWindows;
        if (service is null)
        {
            Feedback.Show(Loc.Get("This app can’t be force quit right now."));
            return;
        }

        var stopped = await service.ForceQuitAsync(windowId);
        Feedback.Show(stopped ? Loc.Get("App force quit.") : Loc.Get("Couldn’t force quit the app."));
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep a recoverable control or event-handler failure local to the
        // shell. Do not pretend process-corrupting failures are safe.
        if (e.Exception is OutOfMemoryException or AccessViolationException or BadImageFormatException)
        {
            return;
        }

        e.Handled = true;
        Console.Error.WriteLine($"[shell-recovery] Recovered from a UI exception: {e.Exception}");
        if (Interlocked.Exchange(ref _handlingDispatcherFailure, 1) != 0)
        {
            return;
        }

        try
        {
            Notifications.Add(new NotificationItem(
                "Something went wrong",
                "The desktop recovered. You can keep working.",
                DateTimeOffset.Now,
                SourceAppId: "System",
                SystemIconGlyph: "warning-circle",
                IsError: true,
                Category: NotificationCategory.System,
                Urgency: NotificationUrgency.High));
        }
        finally
        {
            Dispatcher.UIThread.Post(() => Interlocked.Exchange(ref _handlingDispatcherFailure, 0));
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Console.Error.WriteLine($"[shell-recovery] Observed a background task failure: {e.Exception}");
        e.SetObserved();
    }

    private static void OnProcessUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Console.Error.WriteLine($"[shell-recovery] Fatal shell failure: {e.ExceptionObject}");

    /// <summary>Opens one normal, compositor-managed instance of the DaisyOS Settings app.</summary>
    public async void ShowSettings(string? category = null)
    {
        HideLauncher(restoreFocus: false);
        var launchId = _launchFeedback?.BeginLocalLaunch("org.daisyos.settings");
        var result = await PackagedApplicationLauncher.LaunchAsync("Settings", category is null ? [] : ["--category=" + category]);
        if (!result.Succeeded && launchId is not null) _launchFeedback?.LocalLaunchFailed(launchId);
        if (!result.Succeeded) Feedback.Show(Loc.Get(result.Message));
    }

    public bool IsLayerShellSurface(Window? window) =>
        window is not null && IsNativeShellSession && _nativeShellSurfaces.Contains(window);

    public void PinApplicationToTaskbar(string applicationId)
    {
        if (_useDesktopSettings) { _ = ApplyDesktopSettingAsync("pin", applicationId); return; }
        if (string.IsNullOrWhiteSpace(applicationId)) return;
        var normalized = applicationId.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
            ? applicationId[..^8]
            : applicationId;
        var order = SessionState.DockOrder.ToList();
        if (!order.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            order.Add(normalized);
            SessionState.SetDockOrder(order);
        }
        _shellView?.Taskbar.ApplyOrder(SessionState.DockOrder);
    }

    public void NotifyApplicationRemoved(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId)) return;
        if (_useDesktopSettings)
        {
            _ = ApplyDesktopSettingAsync("app-removed", applicationId);
            return;
        }

        var shortId = DesktopApplicationMaskStore.NormalizeShortId(applicationId);
        var desktopId = DesktopApplicationMaskStore.NormalizeDesktopId(applicationId);
        var strippedId = desktopId[..^8];

        var order = SessionState.DockOrder
            .Where(id => !string.Equals(id, applicationId, StringComparison.OrdinalIgnoreCase)
                      && !string.Equals(id, shortId, StringComparison.OrdinalIgnoreCase)
                      && !string.Equals(id, desktopId, StringComparison.OrdinalIgnoreCase)
                      && !string.Equals(id, strippedId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (order.Count != SessionState.DockOrder.Count)
        {
            SessionState.SetDockOrder(order);
        }

        RequestClose(shortId);
        RequestClose(strippedId);
        _shellView?.Taskbar.ApplyOrder(SessionState.DockOrder);
        RefreshApplicationCatalogs();
    }

    public void NotifyApplicationRestored(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId)) return;
        if (_useDesktopSettings)
        {
            _ = ApplyDesktopSettingAsync("app-restored", applicationId);
            return;
        }

        RefreshApplicationCatalogs();
    }

    private void RefreshApplicationCatalogs()
    {
        if (_sharedLauncherService.IsValueCreated)
        {
            _sharedLauncherService.Value.Refresh();
            lock (_externalLookupGate)
            {
                _externalCatalog = null;
                _externalLookup = null;
            }
        }

        if (_shellView is not null)
        {
            _ = _shellView.Launcher.ViewModel.ReloadApplicationsAsync();
        }
    }

    /// <summary>Opens one instance of the native DaisyOS Notes app.</summary>
    public void ShowNotes() => LaunchNotes(false);

    private async void LaunchMail()
    {
        HideLauncher(restoreFocus: false);
        var launchId = _launchFeedback?.BeginLocalLaunch("org.daisyos.mail");
        var result = await PackagedApplicationLauncher.LaunchAsync("Mail");
        if (!result.Succeeded && launchId is not null) _launchFeedback?.LocalLaunchFailed(launchId);
        if (!result.Succeeded) Feedback.Show(Loc.Get(result.Message));
    }

    /// <summary>Opens Notes and creates a new note in its own application process.</summary>
    public void ShowNewNote() => LaunchNotes(true);

    private async void LaunchNotes(bool newNote)
    {
        HideLauncher(restoreFocus: false);
        var launchId = _launchFeedback?.BeginLocalLaunch("org.daisyos.notes");
        var result = await PackagedApplicationLauncher.LaunchAsync("Notes", newNote ? ["--new-note"] : []);
        if (!result.Succeeded && launchId is not null) _launchFeedback?.LocalLaunchFailed(launchId);
        if (!result.Succeeded) Feedback.Show(Loc.Get(result.Message));
    }

    /// <summary>Opens one instance of the native DaisyOS Calendar app.</summary>
    public void ShowCalendar()
    {
        HideLauncher(restoreFocus: false);
        if (_nativeWindowTracker.RestoreAndActivate("calendar"))
        {
            return;
        }

        if (_calendarWindow is { IsVisible: false } cached)
        {
            _nativeWindowTracker.Register("calendar", cached);
            ShowNormalApplicationWindow(cached);
            cached.Activate();
            return;
        }
        _calendarWindow = new CalendarWindow();
        var calendarModel = _calendarWindow.DataContext as DaisyOS.Shell.Apps.Calendar.CalendarViewModel;
        _lightweightWindows.Track(_calendarWindow, () => calendarModel?.SetActive(false),
            () => calendarModel?.SetActive(true), () => _nativeWindowTracker.Unregister("calendar"));
        SetNativeApplicationId(_calendarWindow, "calendar");
        _nativeWindowTracker.Register("calendar", _calendarWindow);
        _calendarWindow.Closed += (_, _) => _calendarWindow = null;

        ShowNormalApplicationWindow(_calendarWindow);
    }

    /// <summary>Opens one instance of the native DaisyOS Calculator app.</summary>
    public void ShowCalculator()
    {
        HideLauncher(restoreFocus: false);
        if (_nativeWindowTracker.RestoreAndActivate("calculator"))
        {
            return;
        }

        if (_calculatorWindow is { IsVisible: false } cached)
        {
            _nativeWindowTracker.Register("calculator", cached);
            ShowNormalApplicationWindow(cached);
            cached.Activate();
            return;
        }
        _calculatorWindow = new CalculatorWindow();
        _lightweightWindows.Track(_calculatorWindow, () => { }, () => { }, () => _nativeWindowTracker.Unregister("calculator"));
        SetNativeApplicationId(_calculatorWindow, "calculator");
        _nativeWindowTracker.Register("calculator", _calculatorWindow);
        _calculatorWindow.Closed += (_, _) => _calculatorWindow = null;

        ShowNormalApplicationWindow(_calculatorWindow);
    }

    /// <summary>Opens one instance of the native DaisyOS App Store.</summary>
    public void ShowAppStore()
    {
        HideLauncher(restoreFocus: false);
        if (_nativeWindowTracker.RestoreAndActivate("appstore"))
        {
            return;
        }

        if (_appStoreWindow is { IsVisible: false } cached)
        {
            _nativeWindowTracker.Register("appstore", cached);
            ShowNormalApplicationWindow(cached);
            cached.Activate();
            return;
        }

        _appStoreSession ??= new DaisyOS.Shell.Apps.AppStore.AppStoreViewModel();
        _appStoreWindow = new DaisyOS.Shell.Apps.AppStore.AppStoreWindow(_appStoreSession, ownsViewModel: false);
        _lightweightWindows.Track(_appStoreWindow, () => { }, () => { }, () => _nativeWindowTracker.Unregister("appstore"));
        SetNativeApplicationId(_appStoreWindow, "appstore");
        _nativeWindowTracker.Register("appstore", _appStoreWindow);
        _appStoreWindow.Closed += (_, _) => _appStoreWindow = null;

        ShowNormalApplicationWindow(_appStoreWindow);
    }

    /// <summary>Opens one instance of Disk Manager in its own application process.</summary>
    public async void ShowDiskManager()
    {
        HideLauncher(restoreFocus: false);
        var launchId = _launchFeedback?.BeginLocalLaunch("org.daisyos.diskmanager");
        var result = await PackagedApplicationLauncher.LaunchAsync("DiskManager");
        if (!result.Succeeded && launchId is not null) _launchFeedback?.LocalLaunchFailed(launchId);
        if (!result.Succeeded) Feedback.Show(Loc.Get(result.Message));
    }

    /// <summary>Opens one instance of the native DaisyOS System Monitor.</summary>
    public void ShowTaskManager()
    {
        HideLauncher(restoreFocus: false);
        if (_nativeWindowTracker.RestoreAndActivate("taskmanager"))
        {
            return;
        }

        if (_taskManagerWindow is { IsVisible: false } cached)
        {
            _nativeWindowTracker.Register("taskmanager", cached);
            ShowNormalApplicationWindow(cached);
            cached.Activate();
            return;
        }

        try
        {
            _taskManagerWindow = new DaisyOS.Shell.Apps.System.Diagnostics.TaskManagerWindow(_compositorWindows);
            var taskManagerModel = _taskManagerWindow.DataContext as DaisyOS.Shell.Apps.System.Diagnostics.TaskManagerViewModel;
            _lightweightWindows.Track(_taskManagerWindow, () => taskManagerModel?.SetActive(false),
                () => taskManagerModel?.SetActive(true), () => _nativeWindowTracker.Unregister("taskmanager"));
            SetNativeApplicationId(_taskManagerWindow, "taskmanager");
            _taskManagerWindow.Closed += (_, _) => _taskManagerWindow = null;

            ShowNormalApplicationWindow(_taskManagerWindow);
            _nativeWindowTracker.Register("taskmanager", _taskManagerWindow);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[system-monitor] Window creation failed: {ex}");
            _nativeWindowTracker.Unregister("taskmanager");
            _taskManagerWindow?.Close();
            _taskManagerWindow = null;
            Feedback.Show(Loc.Get("System Monitor could not be opened."));
        }
    }

    /// <summary>Opens the live catalog of reusable DaisyOS interface components.</summary>
    public void ShowUIKit()
    {
        HideLauncher(restoreFocus: false);
        if (_nativeWindowTracker.RestoreAndActivate("uikit"))
        {
            return;
        }

        if (_uiKitWindow is { IsVisible: false } cached)
        {
            _nativeWindowTracker.Register("uikit", cached);
            ShowNormalApplicationWindow(cached);
            cached.Activate();
            return;
        }

        _uiKitWindow = new UIKitWindow();
        _lightweightWindows.Track(_uiKitWindow, () => { }, () => { }, () => _nativeWindowTracker.Unregister("uikit"));
        SetNativeApplicationId(_uiKitWindow, "uikit");
        _nativeWindowTracker.Register("uikit", _uiKitWindow);
        _uiKitWindow.Closed += (_, _) => _uiKitWindow = null;
        ShowNormalApplicationWindow(_uiKitWindow);
    }

    /// <summary>Opens the floating Clipboard History overlay (Super + V).</summary>
    public void ShowClipboardHistory()
    {
        if (_clipboardHistoryWindow is not null)
        {
            _clipboardHistoryWindow.Activate();
            _clipboardHistoryWindow.RefreshList();
            return;
        }

        _clipboardHistoryWindow = new DaisyOS.Shell.Views.Components.Clipboard.ClipboardHistoryWindow();
        _clipboardHistoryWindow.Closed += (_, _) => _clipboardHistoryWindow = null;
        _clipboardHistoryWindow.Show();
    }

    public async Task ShowTrashAsync()
    {
        var trash = UserTrashPaths.Files;
        await Task.Run(() => Directory.CreateDirectory(trash));
        ShowFiles(trash);
    }

    public void ShowFiles(string? path = null) => ShowFiles(string.IsNullOrWhiteSpace(path) ? (FileManagerActivationRequest?)null : new FileManagerActivationRequest([path], false));



    public void ShowLocalSend(IEnumerable<string>? paths = null)
    {
        if (ShareRequested is { } share)
        {
            share(paths);
            return;
        }
        HideLauncher(restoreFocus: false);
        if (IsNativeShellSession && _localSendWindow is not null && _nativeLocalSendSurface is not null)
        {
            _localSendWindow.Open(_localSendService, _localSendUnavailable, paths);
            SetNativeSurfaceVisible(_nativeLocalSendSurface, true, captureKeyboard: true);
            return;
        }

        // The windowed prototype has no layer-shell host. Retain a small,
        // undecorated fallback there so sharing remains testable, while the
        // native session never takes this branch.
        if (_windowedLocalSendHost is null)
        {
            _localSendWindow = new LocalSendWindow();
            _windowedLocalSendHost = Loc.Bind(new Window
            {
                Title = "Share with LocalSend",
                Width = 500,
                Height = 468,
                MinWidth = 500,
                MaxWidth = 500,
                MinHeight = 468,
                MaxHeight = 468,
                WindowDecorations = WindowDecorations.None,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true,
                Background = Brushes.Transparent,
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                Content = _localSendWindow
            }, Window.TitleProperty, "Share with LocalSend");
            _localSendWindow.DismissRequested += (_, _) => _windowedLocalSendHost?.Close();
            _windowedLocalSendHost.Closed += (_, _) =>
            {
                _windowedLocalSendHost = null;
                _localSendWindow = null;
            };
        }

        _localSendWindow!.Open(_localSendService, _localSendUnavailable, paths);
        _windowedLocalSendHost!.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _windowedLocalSendHost.Show();
    }

    private async void ShowFiles(FileManagerActivationRequest? activation)
    {
        HideLauncher(restoreFocus: false);
        var launchId = _launchFeedback?.BeginLocalLaunch("org.daisyos.files");
        var result = await PackagedApplicationLauncher.LaunchFilesAsync(activation);
        if (!result.Succeeded && launchId is not null) _launchFeedback?.LocalLaunchFailed(launchId);
        if (!result.Succeeded) Feedback.Show(Loc.Get(result.Message));
    }

    public void OpenFilesWindow() => ShowFiles((string?)null);

    public async void ShowAppInstaller(string? appImagePath = null)
    {
        HideLauncher(restoreFocus: false);
        var launchId = _launchFeedback?.BeginLocalLaunch("org.daisyos.appinstaller");
        var result = await PackagedApplicationLauncher.LaunchAsync("AppInstaller", appImagePath is null ? [] : [appImagePath]);
        if (!result.Succeeded && launchId is not null) _launchFeedback?.LocalLaunchFailed(launchId);
        if (!result.Succeeded) Feedback.Show(Loc.Get(result.Message));
    }

    private LockScreenWindow? _lockScreenWindow;
    private readonly List<Window> _secondaryLockScreens = new();
    private PowerIdleService? _powerIdleService;
    private bool _displayIsOff;
    private bool _onBatteryPower;
    private Timer? _powerSourcePoll;

    /// <summary>True when on battery and the user wants live wallpapers paused to save power.</summary>
    public bool IsLiveWallpaperPausedForBattery =>
        Volatile.Read(ref _onBatteryPower) &&
        PowerPreferences.ParseBool(GetPreference(PowerPreferences.PauseLiveWallpaperOnBatteryKey, "true"), true);

    /// <summary>True when the device is running on battery power.</summary>
    public bool IsOnBattery => Volatile.Read(ref _onBatteryPower);

    /// <summary>Raised on the UI thread when the AC/battery state or a related power preference changes.</summary>
    public event EventHandler? PowerSourceChanged;

    private void StartIdleLockService()
    {
        _releaseMemoryWhenHidden = PowerPreferences.ParseBool(GetPreference(PowerPreferences.ReleaseMemoryWhenGamingKey, "true"), true);
        StartMemoryTrimmer();
        StartPowerSourceMonitor();

        try
        {
            var runner = new SafeCommandRunner();
            _powerIdleService = new PowerIdleService(
                lockSession: () => Dispatcher.UIThread.Post(() => LockSession()),
                screenOff: () =>
                {
                    Volatile.Write(ref _displayIsOff, true);
                    _ = runner.RunAsync("kscreen-doctor", ["--dpms", "off"], TimeSpan.FromSeconds(3));
                    RequestHiddenShellMemoryTrim();
                },
                screenOn: () =>
                {
                    Volatile.Write(ref _displayIsOff, false);
                    // KWin wakes outputs on input by itself; this covers older builds.
                    _ = runner.RunAsync("kscreen-doctor", ["--dpms", "on"], TimeSpan.FromSeconds(3));
                },
                suspend: () => Dispatcher.UIThread.Post(() => _ = SuspendSessionAsync()),
                // Never blank the screen or sleep in the middle of a game.
                suppressDisruptiveActions: () => SessionState.GamingMode);
            _powerIdleService.UpdateConfiguration(PowerIdleConfiguration.FromPreferences(GetPreference));
            PreferenceChanged += (_, pref) =>
            {
                if (!PowerPreferences.IsPowerKey(pref.Key)) return;
                _powerIdleService.UpdateConfiguration(PowerIdleConfiguration.FromPreferences(GetPreference));
                _releaseMemoryWhenHidden = PowerPreferences.ParseBool(GetPreference(PowerPreferences.ReleaseMemoryWhenGamingKey, "true"), true);
                if (pref.Key == PowerPreferences.PauseLiveWallpaperOnBatteryKey)
                    PowerSourceChanged?.Invoke(this, EventArgs.Empty);
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[daisy-idle] Could not start PowerIdleService: {ex.Message}");
        }
    }

    private void StartPowerSourceMonitor()
    {
        if (!OperatingSystem.IsLinux()) return;
        _onBatteryPower = PowerSource.IsOnBattery();
        _powerSourcePoll = new Timer(_ =>
        {
            var onBattery = PowerSource.IsOnBattery();
            if (onBattery == Volatile.Read(ref _onBatteryPower)) return;
            Volatile.Write(ref _onBatteryPower, onBattery);
            Dispatcher.UIThread.Post(() => PowerSourceChanged?.Invoke(this, EventArgs.Empty));
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>Suspends the computer, locking first when the user asked for it.</summary>
    public async Task SuspendSessionAsync()
    {
        var lockFirst = _powerIdleService?.Configuration.LockOnSuspend
            ?? PowerPreferences.ParseBool(GetPreference(PowerPreferences.LockOnSuspendKey, "true"), true);
        if (lockFirst && _lockScreenWindow is null)
        {
            await LockSessionAsync();
            // Let the lock surface map before the system freezes, so the
            // desktop never flashes on resume.
            await Task.Delay(400);
        }
        var result = await new LinuxPowerService(new SafeCommandRunner()).SuspendAsync();
        if (!result.Succeeded) Feedback.Show(Loc.Get("Couldn’t put the computer to sleep."));
    }

    public void DismissAllTransientShellSurfaces()
    {
        HideLauncher(immediate: true, restoreFocus: false);
        HideLauncherHint(immediate: true);

        if (_shellView is not null)
        {
            _shellView.Launcher.ClosePowerMenu();
            _shellView.Launcher.CloseAppContextMenu();
            _shellView.Desktop.HideWidgetContextMenu();
            _shellView.MediaWidget.HideContextMenu();
            _shellView.Taskbar.HideContextMenu();
            _shellView.HideMetricsPanel();
            _shellView.SystemBar.HideQuickSettingsPanel(immediate: true);
            _shellView.SystemBar.HideCalendarPanel(immediate: true);
            _shellView.SystemBar.HideAppTrayPanel(immediate: true);
            _shellView.SystemBar.HideNotificationPanel(immediate: true);
        }

        _launcherBlur?.RefreshImmediately();

        if (_nativeLauncherSurface is not null)
        {
            SetNativeSurfaceVisible(_nativeLauncherSurface, false, captureKeyboard: true);
        }

        _nativeDesktopMenus?.Close();
        if (_nativeSystemFlyoutSurface is not null)
        {
            SetNativeSurfaceVisible(_nativeSystemFlyoutSurface, false, captureKeyboard: true);
        }

        if (_localSendWindow?.IsOpen == true)
        {
            _localSendWindow.Dismiss();
        }
    }

    public void LockSession() => _ = LockSessionAsync();

    private Task? _continuityLockTask;
    private Task LockSessionAsync() => _continuityLockTask is { IsCompleted: false }
        ? _continuityLockTask : _continuityLockTask = LockSessionCoreAsync();

    private async Task LockSessionCoreAsync()
    {
        if (_continuitySession is not null) await _continuitySession.SetLockedAsync(true);
        _shellView?.Screenshot.Close();
        if (_gamingOverlay?.IsVisible == true) _gamingOverlay.Toggle();
        if (_volumeIndicator is { } indicator) _ = indicator.DismissAsync();
        DismissAllTransientShellSurfaces();

        if (_lockScreenWindow is not null)
        {
            _lockScreenWindow.Activate();
            return;
        }

        var wallpaperSource = GetCurrentWallpaperSource();
        var initialWallpaper = _nativeShellSurfaces.Select(surface => surface.Content)
            .OfType<DaisyOS.Shell.Views.Components.Wallpaper.WallpaperLayer>().FirstOrDefault();
        if (IsNativeShellSession && _layerShellBackend is not null)
        {
            var primaryScreen = _nativeShellSurfaces.FirstOrDefault()?.Screens?.Primary;
            var primaryOutput = _nativePrimaryOutputName ?? primaryScreen?.DisplayName;
            var primaryOptions = LayerShellSurfaceOptions.LockScreen(primaryOutput, isSecondary: false);
            _lockScreenWindow = LockScreenWindow.CreateNative(_layerShellBackend, primaryOptions, wallpaperSource, initialWallpaper: initialWallpaper);
        }
        else
        {
            _lockScreenWindow = new LockScreenWindow(wallpaperSource);
        }

        _lockScreenWindow.Dismissing += () =>
        {
            foreach (var win in _secondaryLockScreens)
            {
                if (win is LockScreenWindow lsw)
                {
                    _ = lsw.DismissAsync();
                }
            }
        };
        _lockScreenWindow.Closed += (_, _) =>
        {
            _lockScreenWindow = null;
            if (_continuitySession is not null) _ = _continuitySession.SetLockedAsync(false);
            _shellView?.SetLockScreenActive(false);
            if (_shellView is not null) ApplyNativeBottomChromeState(_shellView);
            DesktopOcclusionChanged?.Invoke(this, EventArgs.Empty);
            foreach (var win in _secondaryLockScreens)
            {
                try { win.Close(); } catch (Exception ex) { global::System.Diagnostics.Debug.WriteLine($"Failed to close lock screen: {ex.Message}"); }
            }
            _secondaryLockScreens.Clear();
            _secondaryLockScreenOutputNames.Clear();
            _powerIdleService?.ResumeLockMonitoring();
        };
        _powerIdleService?.PauseLockMonitoring();
        RequestHiddenShellMemoryTrim();
        _shellView?.SetLockScreenActive(true);
        if (_shellView is not null) ApplyNativeBottomChromeState(_shellView);
        _lockScreenWindow.Show();
        DesktopOcclusionChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var screens = _lockScreenWindow.Screens.All;
            if (screens.Count > 1)
            {
                var primary = screens.FirstOrDefault(screen => screen.DisplayName == _nativePrimaryOutputName)
                    ?? _lockScreenWindow.Screens.ScreenFromWindow(_lockScreenWindow)
                    ?? _lockScreenWindow.Screens.Primary ?? screens.FirstOrDefault();
                foreach (var scr in screens)
                {
                    if (ReferenceEquals(scr, primary)) continue;

                    LockScreenWindow secondary;
                    if (IsNativeShellSession && _layerShellBackend is not null)
                    {
                        var secOptions = LayerShellSurfaceOptions.LockScreen(scr.DisplayName, isSecondary: true);
                        secondary = LockScreenWindow.CreateNative(_layerShellBackend, secOptions, wallpaperSource, isSecondary: true, initialWallpaper: initialWallpaper);
                    }
                    else
                    {
                        secondary = new LockScreenWindow(wallpaperSource, isSecondary: true)
                        {
                            Position = scr.Bounds.Position,
                            Width = scr.Bounds.Width,
                            Height = scr.Bounds.Height
                        };
                    }
                    _secondaryLockScreens.Add(secondary);
                    if (scr.DisplayName is { } outputName) _secondaryLockScreenOutputNames[secondary] = outputName;
                    secondary.Show();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LockSession] Secondary screens setup: {ex.Message}");
        }
    }

    /// <summary>
    /// Layer-shell surfaces are not xdg_toplevel parents.  In the native
    /// session an owned <see cref="Window.Show(Window)"/> call therefore
    /// creates an invalid relationship from a normal app to the desktop
    /// surface.  Let KWin manage the app as an independent xdg_toplevel.
    /// The windowed prototype retains its normal owner relationship.
    /// </summary>
    private void ShowNormalApplicationWindow(Window window)
    {
        DaisyOS.Shell.Services.Diagnostics.ResponsivenessTrace.Mark("app.window.show", window.GetType().Name);
        NativeAppWindowSizing.Apply(window);
        window.AddHandler(InputElement.PointerPressedEvent, (_, _) => _recentPrefetch.Interaction(), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.KeyDownEvent, (_, _) => _recentPrefetch.Interaction(), RoutingStrategies.Tunnel, handledEventsToo: true);
        if (IsNativeShellSession)
        {
            window.Show();
            return;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { IsVisible: true } mainWindow })
        {
            window.Show(mainWindow);
            return;
        }

        window.Show();
    }

    private static void SetNativeApplicationId(Window window, string appId)
    {
        if (IsNativeShellSession)
            WaylandWindow.SetAppId(window, $"org.daisyos.{appId}");
    }

    public async Task SetWallpaperAsync(string wallpaperUri)
    {
        if (_useDesktopSettings) { await ApplyDesktopSettingAsync("wallpaper", wallpaperUri); return; }
        var safeWallpaperUri = GetSafeWallpaperUri(wallpaperUri);
        if (!string.Equals(safeWallpaperUri, wallpaperUri, StringComparison.Ordinal))
        {
            throw new FileNotFoundException("The selected wallpaper could not be found.", wallpaperUri);
        }

        if (WallpaperFileTypes.IsVideo(safeWallpaperUri)
            && !safeWallpaperUri.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
            && !await VideoWallpaperService.CanPlayAsync(WallpaperSource.ResolveWallpaperPath(safeWallpaperUri), TimeSpan.FromSeconds(2)))
        {
            Feedback.Show(Loc.Get("This video can’t be used as a wallpaper. Your current wallpaper is unchanged."));
            return;
        }

        if ((WallpaperFileTypes.IsVideo(safeWallpaperUri) || WallpaperFileTypes.IsHeic(safeWallpaperUri)) &&
            _shellView?.ShellWallpaper is { } desktopWallpaper)
        {
            var presented = await desktopWallpaper.TryPresentWallpaperAsync(safeWallpaperUri,
                TimeSpan.FromSeconds(WallpaperFileTypes.IsHeic(safeWallpaperUri) ? 30 : 10));
            if (!presented)
            {
                Feedback.Show(WallpaperFileTypes.IsHeic(safeWallpaperUri)
                    ? Loc.Get("This image can’t be used as a wallpaper. Your current wallpaper is still active.")
                    : Loc.Get("The video opened, but the desktop could not present its first frame. Your wallpaper is unchanged."));
                return;
            }
        }

        _appearanceSettings.WallpaperUri = safeWallpaperUri;
        _appearanceSettings.UseSolidWallpaper = false;
        if (_wallpaperService is not null)
        {
            await _wallpaperService.SetWallpaperAsync(safeWallpaperUri);
        }

        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens the shared browser with the file types requested by the caller.</summary>
    public async Task<IReadOnlyList<string>?> PickFilesAsync(TopLevel? requester, DaisyFilePickerOptions options)
    {
        try
        {
            var picker = new DaisyFilePickerWindow(options);
            SetNativeApplicationId(picker, "file-picker");
            if (requester is Window owner)
                return await picker.ShowDialog<IReadOnlyList<string>?>(owner);

            var completion = new TaskCompletionSource<IReadOnlyList<string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            picker.Closed += (_, _) => completion.TrySetResult(picker.WasConfirmed ? picker.SelectedPaths : null);
            ShowNormalApplicationWindow(picker);
            return await completion.Task;
        }
        catch (Exception ex)
        {
            Feedback.Show(Loc.Get("The file picker couldn’t be opened. Try again."));
            Console.Error.WriteLine($"[file-picker] {ex.Message}");
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> PickImageFilesAsync(
        TopLevel? requester,
        bool allowMultiple,
        IReadOnlyList<string>? initialSelection = null,
        bool allowFolderContents = false,
        bool allowHeic = false)
    {
        var existing = initialSelection?.Where(File.Exists).ToArray() ?? [];
        return await PickFilesAsync(requester, new DaisyFilePickerOptions(
            allowMultiple ? "Choose photos" : "Choose background photo", allowMultiple, allowFolderContents,
            WallpaperFileTypes.ImagePatterns.Where(pattern => allowHeic || !WallpaperFileTypes.IsHeic(pattern))
                .Select(pattern => pattern.TrimStart('*')).ToArray(),
            existing, existing.Select(Path.GetDirectoryName).FirstOrDefault(Directory.Exists), "photo", "photos")) ?? existing;
    }

    public async Task<string?> PickFontFileAsync(TopLevel? requester) =>
        (await PickFilesAsync(requester, new DaisyFilePickerOptions("Add a system font", false, false,
            [".ttf", ".otf", ".ttc"])))?.FirstOrDefault();

    public async Task SetSolidWallpaperAsync(Color color)
    {
        if (_useDesktopSettings)
        {
            var solidSource = WallpaperSource.FromSolidColor(color);
            _appearanceSettings.SolidWallpaperColor = solidSource["solid:".Length..];
            _appearanceSettings.UseSolidWallpaper = true;
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
            await ApplyDesktopSettingAsync("solid-wallpaper", color.ToString());
            return;
        }
        var source = WallpaperSource.FromSolidColor(color);
        _appearanceSettings.SolidWallpaperColor = source["solid:".Length..];
        _appearanceSettings.UseSolidWallpaper = true;
        if (_wallpaperService is not null)
        {
            await _wallpaperService.SetWallpaperAsync(source);
        }

        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UseImageWallpaperAsync()
    {
        if (_useDesktopSettings)
        {
            _appearanceSettings.UseSolidWallpaper = false;
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
            await ApplyDesktopSettingAsync("image-wallpaper");
            return;
        }
        var source = GetSafeWallpaperUri(_appearanceSettings.WallpaperUri);
        if (WallpaperSource.TryGetSolidColor(source, out _) && TryGetDefaultImageWallpaper() is { } defaultImage)
        {
            source = defaultImage;
            _appearanceSettings.WallpaperUri = defaultImage;
        }
        _appearanceSettings.UseSolidWallpaper = false;
        if (_wallpaperService is not null)
        {
            await _wallpaperService.SetWallpaperAsync(source);
        }

        await PersistAppearanceAsync();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string? TryGetDefaultImageWallpaper()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var installed = Path.Combine(home, ".config", "DaisyOS", "wallpaper.jpg");
        if (File.Exists(installed)) return installed;

        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var repository = output.Parent?.Parent?.Parent?.Parent?.Parent;
        var development = repository is null ? null : Path.Combine(repository.FullName,
            "os", "archiso", "DaisyOS", "airootfs", "etc", "skel", ".config", "DaisyOS", "wallpaper.jpg");
        return development is not null && File.Exists(development) ? development : null;
    }

    private static ThemeVariant ToThemeVariant(ThemeMode mode) =>
        mode == ThemeMode.Light ? ThemeVariant.Light : ThemeVariant.Dark;

    private static string GetSafeWallpaperUri(string? wallpaperUri)
    {
        if (WallpaperSource.TryGetSolidColor(wallpaperUri, out _)) return wallpaperUri!;
        var path = WallpaperSource.ResolveWallpaperPath(wallpaperUri);
        return File.Exists(path) ? path : ShellSettings.DefaultWallpaperUri;
    }

    private string GetCurrentWallpaperSource() =>
        _appearanceSettings.UseSolidWallpaper
            ? WallpaperSource.FromSolidColor(GetSolidWallpaperColor())
            : GetSafeWallpaperUri(_appearanceSettings.WallpaperUri);

    private Color GetSolidWallpaperColor() =>
        Color.TryParse(_appearanceSettings.SolidWallpaperColor, out var color)
            ? Color.FromRgb(color.R, color.G, color.B)
            : Color.Parse("#2E3440");

    private async Task PersistAppearanceAsync()
    {
        try
        {
            await _appearanceSettingsStore.SaveAsync(_appearanceSettings);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save appearance preferences: {ex.Message}");
        }
    }

    private void ApplyAccentPreference()
    {
        var accent = GetThemeColor(_appearanceSettings.AccentColor switch
        {
            AccentColor.Blue or AccentColor.Default => "AccentOptionDefaultColor",
            AccentColor.Purple => "AccentOptionPurpleColor",
            AccentColor.Pink => "AccentOptionPinkColor",
            AccentColor.Red => "AccentOptionRedColor",
            AccentColor.Orange => "AccentOptionOrangeColor",
            AccentColor.Green => "AccentOptionGreenColor",
            AccentColor.Gray => "AccentOptionGrayColor",
            AccentColor.White => "AccentOptionWhiteColor",
            _ => "AccentOptionDefaultColor"
        });
        var defaultBlue = _appearanceSettings.AccentColor is AccentColor.Default or AccentColor.Blue;
        var onAccent = defaultBlue ? Color.Parse("#FF111113") : GetHighestContrastContent(accent);
        var interactionTarget = defaultBlue
            ? ActualThemeVariant == ThemeVariant.Light ? Colors.Black : Colors.White
            : onAccent;

        if (Resources is null) return;
        var actionBrush = new SolidColorBrush(accent);
        var hoverBrush = new SolidColorBrush(Blend(accent, interactionTarget, defaultBlue ? 0.08 : 0.12));
        var pressedBrush = new SolidColorBrush(Blend(accent, interactionTarget, defaultBlue ? 0.14 : 0.22));
        var focusBrush = new SolidColorBrush(accent);

        Resources["ActionPrimaryColor"] = accent;
        Resources["ActionPrimaryBrush"] = actionBrush;
        Resources["ActionPrimaryHoverBrush"] = hoverBrush;
        Resources["ActionPrimaryPressedBrush"] = pressedBrush;
        // Primary labels stay white regardless of accent contrast or theme.
        Resources["ContentOnPrimaryActionColor"] = Colors.White;
        Resources["ContentOnPrimaryActionBrush"] = new SolidColorBrush(Colors.White);
        Resources["ContentOnActionBrush"] = new SolidColorBrush(GetHighestContrastContent(accent));
        Resources["FocusRingBrush"] = focusBrush;

        Resources["AccentPrimaryBrush"] = actionBrush;
        Resources["AccentHoverBrush"] = hoverBrush;
        Resources["AccentPressedBrush"] = pressedBrush;
        Resources["FocusStrokeBrush"] = focusBrush;
    }

    private Color GetThemeColor(string key)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var value) && value is Color foundColor)
            return foundColor;

        if (TryGetThemeColorRecursive(Resources, key, ActualThemeVariant, out var dictColor))
            return dictColor;

        var isLight = ActualThemeVariant == ThemeVariant.Light;
        return key switch
        {
            "AccentOptionDefaultColor" => isLight ? Color.Parse("#FF1598FF") : Color.Parse("#FF148BE8"),
            "AccentOptionBlueColor" => isLight ? Color.Parse("#FF1598FF") : Color.Parse("#FF148BE8"),
            "AccentOptionPurpleColor" => isLight ? Color.Parse("#FF7652D9") : Color.Parse("#FFBB9CFF"),
            "AccentOptionPinkColor" => isLight ? Color.Parse("#FFD94F8D") : Color.Parse("#FFFF9AC6"),
            "AccentOptionRedColor" => isLight ? Color.Parse("#FFD84A43") : Color.Parse("#FFFF9B91"),
            "AccentOptionOrangeColor" => isLight ? Color.Parse("#FFD97822") : Color.Parse("#FFFFB87B"),
            "AccentOptionGreenColor" => isLight ? Color.Parse("#FF2B8A52") : Color.Parse("#FF7DD9A0"),
            "AccentOptionGrayColor" => isLight ? Color.Parse("#FF777B84") : Color.Parse("#FFC4C7CD"),
            "AccentOptionWhiteColor" => isLight ? Color.Parse("#FFFFFFFF") : Color.Parse("#FFF5F5F7"),
            _ => isLight ? Color.Parse("#FF1598FF") : Color.Parse("#FF148BE8")
        };
    }

    private static bool TryGetThemeColorRecursive(IResourceDictionary? dictionary, string key, ThemeVariant? theme, out Color color)
    {
        color = default;
        if (dictionary is null) return false;

        if (theme is not null && dictionary.ThemeDictionaries.TryGetValue(theme, out var themeDictObj) && themeDictObj is IResourceDictionary themeDict)
        {
            if (themeDict.TryGetResource(key, null, out var val) && val is Color c)
            {
                color = c;
                return true;
            }
        }

        if (dictionary.TryGetResource(key, theme, out var directVal) && directVal is Color directColor)
        {
            color = directColor;
            return true;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (merged is IResourceDictionary mergedDict && TryGetThemeColorRecursive(mergedDict, key, theme, out color))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyAccessibilityPreferences()
    {
        if (Resources is null) return;
        if (_appearanceSettings.ReduceMotion) Services.Motion.GuiMotion.SettleActiveAnimations();
        _ = ExportWindowDecorationThemeAsync();

        foreach (var (key, milliseconds) in new[] { ("MotionDurationHover", 50), ("MotionDurationFast", 100), ("MotionDurationBase", 160),
                     ("MotionDurationSlow", 220), ("MotionDurationShellEnter", 220), ("MotionDurationShellExit", 140), ("MotionDurationEnter", 280), ("MotionDurationExit", 140), ("MotionDelayBrief", 80) })
            Resources[key] = TimeSpan.FromMilliseconds(_appearanceSettings.ReduceMotion ? 0 : milliseconds);

        // Replace the control-level transition resources, including their durations.
        // Nested transition objects do not have a reliable dynamic-resource host.
        var toggleDuration = TimeSpan.FromMilliseconds(_appearanceSettings.ReduceMotion ? 0 : 100);
        Resources["ToggleThumbTransitions"] = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.DoubleTransition { Property = Canvas.LeftProperty, Duration = toggleDuration,
                Easing = new Avalonia.Animation.Easings.CubicEaseOut() }
        };
        Resources["ToggleTrackTransitions"] = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.BrushTransition { Property = Border.BackgroundProperty, Duration = toggleDuration,
                Easing = new Avalonia.Animation.Easings.CubicEaseInOut() }
        };

        var keys = new[] { "SurfaceBaseBrush", "SurfaceRaisedBrush", "SurfaceSunkenBrush", "SurfaceInputBrush", "SurfaceInputHoverBrush", "StrokeDefaultBrush", "StrokeStrongBrush", "ContentPrimaryBrush", "ContentSecondaryBrush", "ContentTertiaryBrush", "ContentDisabledBrush" };
        foreach (var key in keys) Resources.Remove(key);
        if (!_appearanceSettings.HighContrast) return;

        var light = ActualThemeVariant == ThemeVariant.Light;
        var foreground = light ? Colors.Black : Colors.White;
        var background = light ? Colors.White : Colors.Black;
        Resources["SurfaceBaseBrush"] = new SolidColorBrush(background);
        Resources["SurfaceRaisedBrush"] = new SolidColorBrush(background);
        Resources["SurfaceSunkenBrush"] = new SolidColorBrush(light ? Color.Parse("#FFE6E6E6") : Color.Parse("#FF1F1F1F"));
        Resources["SurfaceInputBrush"] = new SolidColorBrush(light ? Color.Parse("#FFFFFFFF") : Color.Parse("#FF242424"));
        Resources["SurfaceInputHoverBrush"] = new SolidColorBrush(light ? Color.Parse("#FFF4F4F4") : Color.Parse("#FF2C2C2C"));
        Resources["StrokeDefaultBrush"] = new SolidColorBrush(foreground);
        Resources["StrokeStrongBrush"] = new SolidColorBrush(foreground);
        Resources["ContentPrimaryBrush"] = new SolidColorBrush(foreground);
        Resources["ContentSecondaryBrush"] = new SolidColorBrush(foreground);
        Resources["ContentTertiaryBrush"] = new SolidColorBrush(foreground);
        Resources["ContentDisabledBrush"] = new SolidColorBrush(foreground);
    }

    private static Color Blend(Color from, Color to, double amount) => Color.FromArgb(
        (byte)Math.Round(from.A + ((to.A - from.A) * amount)),
        (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
        (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
        (byte)Math.Round(from.B + ((to.B - from.B) * amount)));

    private static Color GetHighestContrastContent(Color background)
    {
        // Pick by actual WCAG contrast instead of a luminance threshold: pastel
        // dark-theme accents need dark labels, saturated light-theme accents read
        // better with dark labels too. White only wins on genuinely dark fills.
        var dark = Color.Parse("#FF111113");
        return ContrastRatio(background, dark) >= ContrastRatio(background, Colors.White) ? dark : Colors.White;
    }

    private static double ContrastRatio(Color first, Color second)
    {
        var light = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
        var dark = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
        return (light + 0.05) / (dark + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linearize(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linearize(color.R)) +
               (0.7152 * Linearize(color.G)) +
               (0.0722 * Linearize(color.B));
    }

    private static readonly string[] NativeAppIds = ["mail", "notes", "calendar", "calculator", "settings", "files", "uikit", "appstore", "taskmanager", "diskmanager", "appinstaller"];

}
