using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.Core.Models.Gaming;
using DaisyOS.System.Controllers;
using DaisyOS.System.Processes;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Console;
using DaisyOS.Shell.Views.Components.Desktop;
using DaisyOS.Shell.Views.Components.Launcher;
using DaisyOS.Shell.Views.Components.Notifications;
using DaisyOS.Shell.Views.Components.Screenshot;
using DaisyOS.Shell.Views.Components.SystemBar;
using DaisyOS.Shell.Views.Components.Taskbar;
using DaisyOS.Shell.Views.Components.Widgets;
using DaisyOS.Shell.Views.Windows;
using DaisyOS.Shell.Services.Diagnostics;
using DaisyOS.Shell.Services.Motion;
using DaisyOS.Shell.Services.Shell;
using DaisyOS.Shell.Controls;

namespace DaisyOS.Shell.Views
{
    public partial class ShellView : UserControl
    {
        private readonly DispatcherTimer _feedbackTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        private readonly DispatcherTimer _controllerPollTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        private readonly DispatcherTimer _taskbarAutoHideTimer = new() { Interval = TimeSpan.FromMilliseconds(1100) };
        private readonly DispatcherTimer _systemBarAutoHideTimer = new() { Interval = TimeSpan.FromMilliseconds(1100) };
        private static readonly TimeSpan BlackFadeDuration = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan ConsoleLoadingDuration = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan DesktopRestoreDuration = TimeSpan.FromMilliseconds(150);
        private readonly IControllerService _controllerService;
        private readonly IControllerInputService _controllerInputService;
        private CancellationTokenSource? _controllerCancellation;
        private bool _controllerConnected;
        private bool _physicalControllerConnected;
        private bool _consoleModeRequested;
        private bool _checkingController;
        private bool _consoleMode;
        private bool _consoleTransitionInProgress;
        private bool _lastInputWasController;
        private bool _usesNativeConsoleSurface;
        private bool _taskbarAutoHide;
        private bool _systemBarAutoHide;
        private bool _mediaAutoHide;
        private bool _mediaRevealed;
        private readonly DispatcherTimer _mediaAutoHideTimer = new() { Interval = TimeSpan.FromMilliseconds(1100) };
        private readonly BottomChromeTooltips _bottomTooltips = new();
        private bool _taskbarRevealed;
        private bool _systemBarRevealed;
        private bool _externalFullscreenActive;
        private bool _lockScreenActive;
        private bool _bottomBarVisibilityInitialized;

        public ShellView()
            : this(new LinuxControllerService(), new LinuxControllerInputService())
        {
        }

        public ShellView(IControllerService controllerService)
            : this(controllerService, new LinuxControllerInputService())
        {
        }

        public ShellView(IControllerService controllerService, IControllerInputService controllerInputService)
        {
            _controllerService = controllerService ?? throw new ArgumentNullException(nameof(controllerService));
            _controllerInputService = controllerInputService ?? throw new ArgumentNullException(nameof(controllerInputService));
            InitializeComponent();
            DesktopExperience.Children.Add(_bottomTooltips);
            _bottomTooltips.Attach(TaskbarContent, TaskbarContent.HasHoverPreview);
            _bottomTooltips.Attach(SystemBarContent);
            _bottomTooltips.Attach(MediaWidgetContent);
            MediaWidgetContent.PointerEntered += (_, _) => _mediaAutoHideTimer.Stop();
            MediaWidgetContent.PointerExited += (_, _) => ScheduleMediaAutoHide();
            MediaWidgetContent.ContextMenuVisibilityChanged += (_, visible) =>
            {
                if (visible) _mediaAutoHideTimer.Stop();
                else ScheduleMediaAutoHide();
            };
            _mediaAutoHideTimer.Tick += (_, _) => HideMediaIfUnhovered();
            ConsoleSettings.SetInputService(_controllerInputService);
            AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            TaskbarContent.PointerEntered += (_, _) => _taskbarAutoHideTimer.Stop();
            TaskbarContent.PointerExited += (_, _) => ScheduleTaskbarAutoHide();
            SystemBarContent.PointerEntered += (_, _) => _systemBarAutoHideTimer.Stop();
            SystemBarContent.PointerExited += (_, _) => ScheduleSystemBarAutoHide();
            TaskbarContent.ContextMenuVisibilityChanged += (_, visible) =>
            {
                if (visible) _taskbarAutoHideTimer.Stop();
                else ScheduleTaskbarAutoHide();
            };
            SystemBarContent.FlyoutVisibilityChanged += (_, visible) =>
            {
                if (visible)
                {
                    _systemBarRevealed = true;
                    _systemBarAutoHideTimer.Stop();
                    ApplyBottomBarVisibility();
                }
                else ScheduleSystemBarAutoHide();
            };
            _taskbarAutoHideTimer.Tick += (_, _) => HideTaskbarIfUnhovered();
            _systemBarAutoHideTimer.Tick += (_, _) => HideSystemBarIfUnhovered();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            _controllerInputService.NavigationRequested += OnControllerNavigationRequested;
            ConsoleHome.SelectionChanged += OnConsoleSelectionChanged;
            ConsoleHome.SettingsRequested += (_, _) => ConsoleSettings.ShowOverlay("Audio");
            ConsoleHome.GameLaunchRequested += (_, game) => LaunchConsoleGame(game);
            ConsoleSettings.Closed += (_, _) => Dispatcher.UIThread.Post(ConsoleHome.FocusInitialDestination, DispatcherPriority.Input);
            ConsoleSettings.ReturnToDesktopRequested += async (_, _) =>
            {
                _consoleModeRequested = false;
                _controllerConnected = false;
                await ReconcileConsoleModeAsync();
            };

            if (Application.Current is App feedbackApp)
            {
                feedbackApp.Feedback.MessageShown += OnFeedbackMessageShown;
                Unloaded += (_, _) => feedbackApp.Feedback.MessageShown -= OnFeedbackMessageShown;
            }
            _feedbackTimer.Tick += (_, _) =>
            {
                _feedbackTimer.Stop();
                if (this.FindControl<Control>("FeedbackHost") is { } host) host.IsVisible = false;
                if (this.FindControl<Control>("ConsoleFeedbackHost") is { } consoleHost) consoleHost.IsVisible = false;
            };
            _controllerPollTimer.Tick += async (_, _) => await RefreshControllerStateAsync();
            RegisterShellComponents();
            MetricsPanel.InitializeMonitor(_metricsMonitor);
            MetricsPanel.PanelVisibilityChanged += (_, visible) => MetricsSurfaceVisibilityChanged?.Invoke(this, visible);

            Desktop.ViewModel.BottomMediaWidgetVisibilityChanged += (_, visible) =>
            {
                if (!visible) { _mediaRevealed = false; _mediaAutoHideTimer.Stop(); }
                ApplyBottomBarVisibility();
            };
            SetBottomMediaWidgetVisible(Desktop.ViewModel.BottomMediaWidgetVisible, immediate: true);
        }

        private void SetBottomMediaWidgetVisible(bool visible, bool immediate = false)
        {
            _ = GuiMotion.SetVisibleAsync(MediaWidgetContent,
                MediaWidgetContent.FindControl<Border>("MediaFrame")!, visible,
                NotifyBottomChromeStateChanged, immediate, GuiMotion.ShellSlideDistance,
                GuiMotion.ShellSlideDuration(this, visible));
        }

        private readonly ShellMetricsMonitor _metricsMonitor = new();

        public TaskbarView Taskbar => TaskbarContent;
        public SystemBarView SystemBar => SystemBarContent;
        public DaisyOS.Shell.Views.Components.Widgets.MediaWidget MediaWidget => MediaWidgetContent;
        public event EventHandler? BottomChromeStateChanged;
        public bool CanReleaseBottomWorkArea => _taskbarAutoHide && _systemBarAutoHide &&
            (_mediaAutoHide || !Desktop.ViewModel.BottomMediaWidgetVisible);
        public bool IsBottomPanelInputPassthrough =>
            !TaskbarContent.IsVisible && !SystemBarContent.IsVisible && !MediaWidgetContent.IsVisible;
        public bool IsBottomEdgeSensorEnabled => !_lockScreenActive && (_taskbarAutoHide || _systemBarAutoHide ||
            (!_externalFullscreenActive && _mediaAutoHide && Desktop.ViewModel.BottomMediaWidgetVisible));
        public bool IsExternalFullscreenActive => _externalFullscreenActive;

        private bool IsTaskbarVisibleByPreference => !_taskbarAutoHide || _taskbarRevealed || IsLauncherVisible;

        /// <summary>
        /// Hide chrome on entering fullscreen, while keeping auto-hidden bars
        /// available through their independent edge reveal targets.
        /// </summary>
        public void SetExternalFullscreenActive(bool isFullScreen)
        {
            if (_externalFullscreenActive == isFullScreen) return;
            _externalFullscreenActive = isFullScreen;
            if (isFullScreen)
            {
                _taskbarAutoHideTimer.Stop();
                _systemBarAutoHideTimer.Stop();
                _taskbarRevealed = false;
                _systemBarRevealed = false;
                _mediaRevealed = false;
                _mediaAutoHideTimer.Stop();
                _bottomTooltips.Dismiss();
            }
            ApplyBottomBarVisibility(immediate: isFullScreen);
            if (isFullScreen)
            {
                HideLauncher();
                SystemBar.HideQuickSettingsPanel();
                SystemBar.HideCalendarPanel();
                SystemBar.HideAppTrayPanel();
                SystemBar.HideNotificationPanel();
            }
        }

        /// <summary>Suppresses desktop chrome while the session lock surface is active.</summary>
        public void SetLockScreenActive(bool isActive)
        {
            if (_lockScreenActive == isActive) return;
            _lockScreenActive = isActive;
            if (isActive)
            {
                _taskbarAutoHideTimer.Stop();
                _systemBarAutoHideTimer.Stop();
                _mediaAutoHideTimer.Stop();
                _taskbarRevealed = false;
                _systemBarRevealed = false;
                _mediaRevealed = false;
                _bottomTooltips.Dismiss();
            }
            ApplyBottomBarVisibility(immediate: true);
        }

        public void SetBottomBarAutoHide(bool taskbarAutoHide, bool systemBarAutoHide, bool mediaAutoHide = false)
        {
            var initialConfiguration = !_bottomBarVisibilityInitialized;
            _taskbarAutoHide = taskbarAutoHide;
            _systemBarAutoHide = systemBarAutoHide;
            _mediaAutoHide = mediaAutoHide;
            _mediaRevealed = false;
            _mediaAutoHideTimer.Stop();
            _taskbarRevealed = false;
            _systemBarRevealed = false;
            _taskbarAutoHideTimer.Stop();
            _systemBarAutoHideTimer.Stop();
            ApplyBottomBarVisibility(immediate: initialConfiguration);
            _bottomBarVisibilityInitialized = true;
        }

        public void RevealBottomBarAt(double pointerX, double outputWidth)
        {
            if (_lockScreenActive || !IsBottomEdgeSensorEnabled || outputWidth <= 0) return;
            var position = pointerX / outputWidth;
            if (_mediaAutoHide && Desktop.ViewModel.BottomMediaWidgetVisible && position < 0.30)
            {
                if (!_mediaRevealed)
                {
                    _mediaRevealed = true;
                    ApplyBottomBarVisibility();
                }
                ScheduleMediaAutoHide();
            }
            if (_taskbarAutoHide && position >= 0.30 && position <= 0.72)
                RevealTaskbarTemporarily();
            if (_systemBarAutoHide && position >= 0.80)
                RevealSystemBarTemporarily();
        }

        public void SetLauncherOpen(bool isOpen)
        {
            TaskbarContent.SetLauncherOpen(isOpen);
            if (isOpen)
            {
                _taskbarRevealed = true;
                _taskbarAutoHideTimer.Stop();
                ApplyBottomBarVisibility();
            }
            else ScheduleTaskbarAutoHide();
        }

        private void RevealTaskbarTemporarily()
        {
            if (!_taskbarAutoHide) return;
            if (!_taskbarRevealed)
            {
                _taskbarRevealed = true;
                ApplyBottomBarVisibility();
            }
            ScheduleTaskbarAutoHide();
        }

        private void RevealSystemBarTemporarily()
        {
            if (!_systemBarAutoHide) return;
            if (!_systemBarRevealed)
            {
                _systemBarRevealed = true;
                ApplyBottomBarVisibility();
            }
            ScheduleSystemBarAutoHide();
        }

        private void ApplyBottomBarVisibility(bool immediate = false)
        {
            var taskbarVisible = !_lockScreenActive && (!_externalFullscreenActive || _taskbarAutoHide) && IsTaskbarVisibleByPreference;
            var systemBarVisible = !_lockScreenActive && (!_externalFullscreenActive || _systemBarAutoHide) && (!_systemBarAutoHide || _systemBarRevealed);
            _ = GuiMotion.SetVisibleAsync(
                TaskbarContent,
                TaskbarContent.FindControl<Border>("TaskbarBody")!,
                taskbarVisible,
                NotifyBottomChromeStateChanged,
                immediate,
                GuiMotion.ShellSlideDistance,
                GuiMotion.ShellSlideDuration(this, taskbarVisible), fadeOnExit: false, fadeOnEnter: false);
            _ = GuiMotion.SetVisibleAsync(
                SystemBarContent,
                SystemBarContent.FindControl<Border>("SystemBarFrame")!,
                systemBarVisible,
                NotifyBottomChromeStateChanged,
                immediate,
                GuiMotion.ShellSlideDistance,
                GuiMotion.ShellSlideDuration(this, systemBarVisible), fadeOnExit: false, fadeOnEnter: false);
            SetBottomMediaWidgetVisible(!_lockScreenActive && !_externalFullscreenActive && Desktop.ViewModel.BottomMediaWidgetVisible &&
                (!_mediaAutoHide || _mediaRevealed), immediate);
            NotifyBottomChromeStateChanged();
        }

        private void NotifyBottomChromeStateChanged() => BottomChromeStateChanged?.Invoke(this, EventArgs.Empty);

        private void ScheduleMediaAutoHide()
        {
            _mediaAutoHideTimer.Stop();
            if (_mediaAutoHide && _mediaRevealed && !_externalFullscreenActive && !MediaWidgetContent.IsContextMenuVisible)
                _mediaAutoHideTimer.Start();
        }

        private void HideMediaIfUnhovered()
        {
            _mediaAutoHideTimer.Stop();
            if (!_mediaAutoHide || !_mediaRevealed || _externalFullscreenActive ||
                MediaWidgetContent.IsPointerOver || MediaWidgetContent.IsContextMenuVisible) return;
            _mediaRevealed = false;
            ApplyBottomBarVisibility();
        }

        private void ScheduleTaskbarAutoHide()
        {
            _taskbarAutoHideTimer.Stop();
            if (_taskbarAutoHide && _taskbarRevealed && !IsLauncherVisible && !TaskbarContent.IsContextMenuVisible)
                _taskbarAutoHideTimer.Start();
        }

        private void ScheduleSystemBarAutoHide()
        {
            _systemBarAutoHideTimer.Stop();
            if (_systemBarAutoHide && _systemBarRevealed && !SystemBar.IsQuickSettingsVisible &&
                !SystemBar.IsNotificationPanelVisible && !SystemBar.IsCalendarPanelVisible && !SystemBar.IsAppTrayVisible)
                _systemBarAutoHideTimer.Start();
        }

        private void HideTaskbarIfUnhovered()
        {
            _taskbarAutoHideTimer.Stop();
            if (!_taskbarAutoHide || !_taskbarRevealed || IsLauncherVisible ||
                TaskbarContent.IsPointerOver || TaskbarContent.IsContextMenuVisible) return;
            if (DaisyOS.Shell.Services.Compositor.TaskbarPreviewState.IsShowing)
            {
                ScheduleTaskbarAutoHide();
                return;
            }
            _taskbarRevealed = false;
            ApplyBottomBarVisibility();
        }

        private void HideSystemBarIfUnhovered()
        {
            _systemBarAutoHideTimer.Stop();
            if (!_systemBarAutoHide || !_systemBarRevealed || SystemBarContent.IsPointerOver ||
                SystemBar.IsQuickSettingsVisible || SystemBar.IsNotificationPanelVisible || SystemBar.IsCalendarPanelVisible || SystemBar.IsAppTrayVisible) return;
            _systemBarRevealed = false;
            ApplyBottomBarVisibility();
        }
        public LauncherView Launcher => LauncherContent;
        public bool IsLauncherVisible => LauncherContent.IsOpen;
        public bool IsMetricsPanelVisible => MetricsPanel.IsPanelOpen;
        public event EventHandler<bool>? ConsoleSurfaceVisibilityChanged;
        public event EventHandler<bool>? MetricsSurfaceVisibilityChanged;

        /// <summary>
        /// Separates wallpaper from desktop content so KWin can sample it
        /// beneath translucent desktop widgets.
        /// </summary>
        public Control DetachWallpaperForNativeBackground()
        {
            if (ShellWallpaper.Parent is Panel parent)
                parent.Children.Remove(ShellWallpaper);
            return ShellWallpaper;
        }

        public Control DetachBottomTooltipsForNativeOverlay()
        {
            if (_bottomTooltips.Parent is Panel parent) parent.Children.Remove(_bottomTooltips);
            return _bottomTooltips;
        }

        /// <summary>Moves bottom chrome into the native overlay panel above fullscreen clients.</summary>
        public Control DetachDesktopChromeForNativePanel()
        {
            TaskbarContent.IsNativePanelHosted = true;
            var panel = new Grid { IsHitTestVisible = true, Background = Brushes.Transparent };
            MoveToNativePanel(MediaWidgetContent, panel);
            MoveToNativePanel(TaskbarContent, panel);
            MoveToNativePanel(SystemBarContent, panel);
            return panel;
        }

        /// <summary>Moves the launcher into a modal, native overlay surface.</summary>
        public Control DetachLauncherForNativeOverlay()
        {
            if (LauncherContent.Parent is Panel parent)
            {
                parent.Children.Remove(LauncherContent);
            }

            // The launcher is an application-like centered card. Its full-output
            // host keeps that center independent of the bottom panel geometry.
            var overlay = new Grid();
            LauncherContent.Margin = default;
            LauncherContent.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            LauncherContent.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            overlay.Children.Add(LauncherContent);
            return overlay;
        }

        /// <summary>
        /// Toasts need to sit over normal KWin windows as well as the desktop.
        /// Their host is rendered on a passive overlay surface, whose Wayland
        /// input region is empty so it cannot intercept desktop or app input.
        /// </summary>
        public NotificationToastHost DetachNotificationToastsForNativeOverlay()
        {
            if (NotificationToastOverlay.Parent is Panel parent)
            {
                parent.Children.Remove(NotificationToastOverlay);
            }

            return NotificationToastOverlay;
        }

        /// <summary>
        /// Console mode is a full-output, temporary shell experience. Keep its
        /// controls together so controller focus and the transition animation
        /// cross exactly one native overlay boundary.
        /// </summary>
        public Control DetachConsoleForNativeOverlay()
        {
            _usesNativeConsoleSurface = true;
            var overlay = new Grid();
            if (Content is not Grid root)
                return overlay;

            foreach (var control in new Control[]
                     {
                         ConsoleHome,
                         this.FindControl<Control>("ConsoleFeedbackHost")!,
                         ConsoleSettings,
                         ConsoleTransition
                     })
            {
                root.Children.Remove(control);
                overlay.Children.Add(control);
            }

            return overlay;
        }

        /// <summary>Moves the diagnostics panel into its own temporary overlay.</summary>
        public Control DetachMetricsForNativeOverlay()
        {
            if (MetricsPanel.Parent is Panel parent)
            {
                parent.Children.Remove(MetricsPanel);
            }

            return MetricsPanel;
        }

        public ScreenshotOverlay Screenshot => ScreenshotOverlayContent;

        /// <summary>Moves the screenshot tool into its own interactive overlay.</summary>
        public ScreenshotOverlay DetachScreenshotForNativeOverlay()
        {
            if (ScreenshotOverlayContent.Parent is Panel parent)
            {
                parent.Children.Remove(ScreenshotOverlayContent);
            }

            return ScreenshotOverlayContent;
        }

        private static void MoveToPanel(Control control, Panel panel)
        {
            if (control.Parent is Panel oldParent)
            {
                oldParent.Children.Remove(control);
            }

            panel.Children.Add(control);
        }

        /// <summary>
        /// The native bottom surface contains transparent shadow bleed above
        /// its reserved 58px strip. Keep every 52px component inside the same
        /// explicit 3px top/bottom safe area. This method is the final native
        /// placement boundary, so it must not erase the XAML safe margins.
        /// </summary>
        private static void MoveToNativePanel(Control control, Panel panel)
        {
            MoveToPanel(control, panel);
            var margin = control.Margin;
            control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            control.Margin = new Thickness(
                margin.Left,
                ShellSurfacePlan.PrimaryPanelContentSafeInset,
                margin.Right,
                ShellSurfacePlan.PrimaryPanelContentSafeInset);
        }

        public void ToggleMetricsPanel() => MetricsPanel.TogglePanel();
        public void ShowMetricsPanel() => MetricsPanel.ShowPanel();
        public void HideMetricsPanel() => MetricsPanel.HidePanel();

        private void RegisterShellComponents()
        {
            _metricsMonitor.RegisterComponent(
                "taskbar", "Taskbar", "dock", "Navigation",
                () => TaskbarContent,
                activeCheck: () => true,
                customCpuWeightProvider: () => 1.2);

            _metricsMonitor.RegisterComponent(
                "systembar", "System Bar", "ad_units", "Navigation",
                () => SystemBarContent,
                activeCheck: () => true,
                customCpuWeightProvider: () => 1.1);

            _metricsMonitor.RegisterComponent(
                "launcher", "App Launcher", "apps", "Overlay",
                () => LauncherContent,
                activeCheck: () => LauncherContent.IsVisible,
                customCpuWeightProvider: () => LauncherContent.IsVisible ? 1.5 : 0.1);

            _metricsMonitor.RegisterComponent(
                "desktop", "Desktop Environment", "desktop_windows", "Desktop",
                () => DesktopExperience,
                activeCheck: () => !_consoleMode,
                customCpuWeightProvider: () => _consoleMode ? 0.2 : 1.3);

            _metricsMonitor.RegisterComponent(
                "wallpaper", "Wallpaper Engine", "image", "Background",
                () => ShellWallpaper,
                activeCheck: () => true,
                customCpuWeightProvider: () => 0.8);

            _metricsMonitor.RegisterComponent(
                "media_widget", "Media Widget", "play_circle", "Widgets",
                () => MediaWidgetContent,
                activeCheck: () => MediaWidgetContent.IsVisible,
                customCpuWeightProvider: () => 0.7);

            _metricsMonitor.RegisterComponent(
                "notifications", "Notification Center", "notifications", "Overlay",
                () => NotificationToastOverlay,
                activeCheck: () => true,
                customCpuWeightProvider: () => 0.5);

            _metricsMonitor.RegisterComponent(
                "console_home", "Console Mode UI", "sports_esports", "Console Mode",
                () => ConsoleHome,
                activeCheck: () => _consoleMode,
                customCpuWeightProvider: () => _consoleMode ? 1.8 : 0.05);

            _metricsMonitor.RegisterComponent(
                "console_settings", "Console Settings", "settings_suggest", "Console Mode",
                () => ConsoleSettings,
                activeCheck: () => ConsoleSettings.IsVisible,
                customCpuWeightProvider: () => ConsoleSettings.IsVisible ? 1.4 : 0.05);
        }

        /// <summary>Enters the controller-first shell only after the user explicitly asks for it.</summary>
        public void RequestConsoleMode()
        {
            if (!_physicalControllerConnected)
            {
                (Application.Current as App)?.Feedback.Show(Loc.Get("Connect a controller before opening console mode."));
                return;
            }

            _consoleModeRequested = true;
            _controllerConnected = true;
            _ = ReconcileConsoleModeAsync();
        }

        private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _controllerCancellation = new CancellationTokenSource();
            _controllerPollTimer.Start();
            _ = RefreshControllerStateAsync();
        }

        private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _controllerPollTimer.Stop();
            _mediaAutoHideTimer.Stop();
            _taskbarAutoHideTimer.Stop();
            _systemBarAutoHideTimer.Stop();
            _bottomTooltips.Dismiss();
            _controllerCancellation?.Cancel();
            _controllerCancellation?.Dispose();
            _controllerCancellation = null;
            _controllerInputService.NavigationRequested -= OnControllerNavigationRequested;
            ConsoleHome.SelectionChanged -= OnConsoleSelectionChanged;
            _controllerInputService.Stop();
            _ = _controllerInputService.DisposeAsync();
        }

        private readonly List<Control> _consoleControls = new();
        private readonly List<Control> _desktopControls = new();
        private bool _isFirstLoad = true;

        private void InitializeResourceOptimization()
        {
            if (!_isFirstLoad) return;
            _isFirstLoad = false;

            if (_usesNativeConsoleSurface)
            {
                return;
            }

            // Cache controls so they can be removed/added from the visual tree
            _desktopControls.Add(DesktopExperience);
            _consoleControls.Clear();
            _consoleControls.Add(ConsoleHome);
            _consoleControls.Add(this.FindControl<Control>("ConsoleFeedbackHost")!);
            _consoleControls.Add(ConsoleSettings);

            if (this.Content is Grid rootGrid)
            {
                foreach (var c in _consoleControls) rootGrid.Children.Remove(c);
            }
        }

        private async Task RefreshControllerStateAsync()
        {
            if (_checkingController || _controllerCancellation is null)
            {
                return;
            }

            _checkingController = true;
            try
            {
                var status = await _controllerService.GetConnectionStatusAsync(_controllerCancellation.Token);
                await Dispatcher.UIThread.InvokeAsync(() => ApplyControllerStatusAsync(status));
            }
            catch (OperationCanceledException)
            {
                // Shell shutdown supersedes a pending probe.
            }
            catch (Exception ex)
            {
                // Hardware probing is best effort. Preserve the current shell mode if it fails.
                Console.WriteLine($"Failed to read controller state: {ex.Message}");
            }
            finally
            {
                _checkingController = false;
            }
        }

        private async Task ApplyControllerStatusAsync(ControllerConnectionStatus status)
        {
            _physicalControllerConnected = status.IsConnected;
            if (status.IsConnected)
            {
                ConsoleHome.ControllerName = status.Name ?? "Game controller";
                ConsoleHome.SetControllerLayout(status.Name);
                ConsoleSettings.SetControllerLayout(status.Name);
                _controllerInputService.Start(status);
            }
            else
            {
                _controllerInputService.Stop();
                _consoleModeRequested = false;
            }

            _controllerConnected = status.IsConnected && _consoleModeRequested;

            await ReconcileConsoleModeAsync();
        }

        private async Task ReconcileConsoleModeAsync()
        {
            if (_consoleTransitionInProgress)
            {
                return;
            }

            _consoleTransitionInProgress = true;
            try
            {
                InitializeResourceOptimization();
                var rootGrid = this.Content as Grid;

                if (_consoleMode != _controllerConnected)
                {
                    var enteringConsoleMode = _controllerConnected;
                    ConsoleTransitionTitle.Text = enteringConsoleMode ? Loc.Get("Switching to console mode") : Loc.Get("Returning to desktop");
                    ConsoleTransitionDetail.Text = enteringConsoleMode
                        ? Loc.Format("{0} connected", ConsoleHome.ControllerName)
                        : _physicalControllerConnected ? Loc.Get("Console mode closed") : Loc.Get("Controller disconnected");
                    ConsoleTransition.IsVisible = true;
                    ConsoleSurfaceVisibilityChanged?.Invoke(this, true);
                    await Task.Delay(24);
                    ConsoleTransition.Opacity = 1;
                    await Task.Delay(BlackFadeDuration);

                    if (enteringConsoleMode)
                    {
                        (Application.Current as App)?.DismissTransientShellSurfaces();
                        if (rootGrid != null)
                        {
                            foreach (var c in _desktopControls) rootGrid.Children.Remove(c);
                            foreach (var c in _consoleControls) if (!rootGrid.Children.Contains(c)) rootGrid.Children.Add(c);
                        }
                        ConsoleHome.IsVisible = true;
                        ShellWallpaper.SetConsoleParallaxEnabled(true);
                        Dispatcher.UIThread.Post(ConsoleHome.FocusInitialDestination, DispatcherPriority.Input);
                        await Task.Delay(ConsoleLoadingDuration);
                    }
                    else
                    {
                        ConsoleSettings.HideOverlay();
                        ConsoleHome.IsVisible = false;
                        ShellWallpaper.SetConsoleParallaxEnabled(false);
                        if (rootGrid != null)
                        {
                            foreach (var c in _consoleControls) rootGrid.Children.Remove(c);
                            foreach (var c in _desktopControls) if (!rootGrid.Children.Contains(c)) rootGrid.Children.Add(c);
                        }
                        await Task.Delay(DesktopRestoreDuration);
                    }

                    // Do not expose a mode which was superseded while its transition was running.
                    // Revert the covered surface to the known mode, then the loop can reconcile
                    // any still-pending change without flashing either experience.
                    if (enteringConsoleMode != _controllerConnected)
                    {
                        ConsoleHome.IsVisible = _consoleMode;
                        if (rootGrid != null)
                        {
                            if (_consoleMode)
                            {
                                foreach (var c in _desktopControls) rootGrid.Children.Remove(c);
                                foreach (var c in _consoleControls) if (!rootGrid.Children.Contains(c)) rootGrid.Children.Add(c);
                            }
                            else
                            {
                                foreach (var c in _consoleControls) rootGrid.Children.Remove(c);
                                foreach (var c in _desktopControls) if (!rootGrid.Children.Contains(c)) rootGrid.Children.Add(c);
                            }
                        }
                        ShellWallpaper.SetConsoleParallaxEnabled(_consoleMode);
                    }
                    else
                    {
                        _consoleMode = enteringConsoleMode;
                        if (_consoleMode)
                        {
                            ShellWallpaper.SetConsoleNavigationParallax(ConsoleHome.ParallaxPosition);
                            if (_lastInputWasController)
                            {
                                Cursor = new Cursor(StandardCursorType.None);
                            }
                        }
                        else
                        {
                            Cursor = Cursor.Default;
                        }
                    }

                    ConsoleTransition.Opacity = 0;
                    await Task.Delay(BlackFadeDuration);
                    ConsoleTransition.IsVisible = false;
                    if (!_consoleMode)
                    {
                        ConsoleSurfaceVisibilityChanged?.Invoke(this, false);
                    }
                }
            }
            finally
            {
                _consoleTransitionInProgress = false;

                // A probe can complete between the final condition and this assignment.
                if (_consoleMode != _controllerConnected)
                {
                    _ = ReconcileConsoleModeAsync();
                }
            }
        }

        public void ShowLauncher()
        {
            DaisyOS.Shell.Services.Diagnostics.ResponsivenessTrace.Begin("launcher");
            LauncherContent.AnimateIn();
            LauncherContent.FocusSearch();
        }

        public void HideLauncher(bool immediate = false, Action? completed = null)
        {
            if (immediate)
            {
                LauncherContent.CloseImmediate();
                completed?.Invoke();
            }
            else
            {
                LauncherContent.AnimateOut(() =>
                {
                    LauncherContent.IsVisible = false;
                    completed?.Invoke();
                });
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            var position = e.GetPosition(this);
            if (position.Y >= Bounds.Height - 2)
                RevealBottomBarAt(position.X, Bounds.Width);
            if (_lastInputWasController)
            {
                _lastInputWasController = false;
                Cursor = Cursor.Default;
            }
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_lastInputWasController)
            {
                _lastInputWasController = false;
                Cursor = Cursor.Default;
            }

            if (e.Source is not Visual source)
            {
                return;
            }

            // The taskbar/System Bar toggles must see their own press before they decide
            // whether to open or close an overlay. Everything else is an outside click.
            if (IsLauncherVisible && !IsWithin(source, LauncherContent) && !IsWithin(source, TaskbarContent))
            {
                (Application.Current as App)?.HideLauncher();
            }

            if ((SystemBar.IsQuickSettingsVisible || SystemBar.IsNotificationPanelVisible || SystemBar.IsCalendarPanelVisible || SystemBar.IsAppTrayVisible) && !IsWithin(source, SystemBarContent))
            {
                (Application.Current as App)?.DismissTransientShellSurfaces();
            }
        }

        private void OnControllerNavigationRequested(object? sender, ControllerNavigationAction action)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_lastInputWasController)
                {
                    _lastInputWasController = true;
                    if (_consoleMode)
                    {
                        Cursor = new Cursor(StandardCursorType.None);
                    }
                }

                if (_consoleMode)
                {
                    if (_quickMenuWindow != null && _quickMenuWindow.IsVisible)
                    {
                        _quickMenuWindow.Overlay.Navigate(action);
                    }
                    else if (ConsoleSettings.IsVisible)
                    {
                        ConsoleSettings.Navigate(action);
                    }
                    else if (action == ControllerNavigationAction.OpenConsoleHold)
                    {
                        ShowQuickMenu();
                    }
                    else if (action == ControllerNavigationAction.OpenConsole)
                    {
                        if (_quickMenuWindow != null && _quickMenuWindow.IsVisible)
                        {
                            _quickMenuWindow.Overlay.HideQuickMenu();
                        }
                    }
                    else
                    {
                        ConsoleHome.Navigate(action);
                    }
                }
                else if (action == ControllerNavigationAction.OpenConsole)
                {
                    RequestConsoleMode();
                }
            }, DispatcherPriority.Input);
        }

        private void OnConsoleSelectionChanged(object? sender, double position)
        {
            if (_consoleMode)
            {
                ShellWallpaper.SetConsoleNavigationParallax(position);
            }
        }

        private async void LaunchConsoleGame(ConsoleGameItemViewModel vm)
        {
            var game = vm.Game;
            var launcher = new SafeProcessLauncher();
            var result = game.Source == GameStoreSource.Steam && !string.IsNullOrWhiteSpace(game.SourceId)
                ? launcher.Launch("xdg-open", [$"steam://rungameid/{game.SourceId}"])
                : !string.IsNullOrWhiteSpace(game.DesktopFilePath)
                    ? launcher.Launch("gtk-launch", [Path.GetFileNameWithoutExtension(game.DesktopFilePath)])
                    : !string.IsNullOrWhiteSpace(game.ExecutablePath)
                        ? launcher.Launch(game.ExecutablePath)
                        : new AppLaunchResult(false, $"{game.Title} has no launch target.");

            if (!result.Succeeded)
            {
                (Application.Current as App)?.Feedback.Show(Loc.Format("Couldn’t launch {0}.", game.Title));
                return;
            }

            vm.IsLaunching = true;

            if (_consoleMode)
            {
                var text = this.FindControl<TextBlock>("ConsoleFeedbackText");
                var host = this.FindControl<Control>("ConsoleFeedbackHost");
                if (text != null && host != null)
                {
                    text.Text = Loc.Format("Launching {0}…", game.Title);
                    host.IsVisible = true;
                }
            }
            else
            {
                (Application.Current as App)?.Feedback.Show(Loc.Format("Launching {0}…", game.Title));
            }

            await global::System.Threading.Tasks.Task.Delay(50);

            vm.IsLaunching = false;
            vm.IsRunning = true;
            ShellWallpaper.SetGameRunningState(true);
            ConsoleHome.UpdateSelectedGameSpotlight(vm);

            if (_consoleMode)
            {
                var host = this.FindControl<Control>("ConsoleFeedbackHost");
                if (host != null) host.IsVisible = false;
            }
        }

        private static bool IsWithin(Visual source, Visual container) =>
            ReferenceEquals(source, container) || source.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, container));

        private void OnFeedbackMessageShown(object? sender, string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var textName = _consoleMode ? "ConsoleFeedbackText" : "FeedbackText";
                var hostName = _consoleMode ? "ConsoleFeedbackHost" : "FeedbackHost";
                if (this.FindControl<TextBlock>(textName) is { } text) text.Text = message;
                if (this.FindControl<Control>(hostName) is { } host) host.IsVisible = true;
                _feedbackTimer.Stop();
                _feedbackTimer.Start();
            });
        }

        private ConsoleQuickMenuWindow? _quickMenuWindow;

        private void ShowQuickMenu()
        {
            if (_quickMenuWindow != null) return;

            _quickMenuWindow = new ConsoleQuickMenuWindow();
            
            _quickMenuWindow.Closed += (_, _) =>
            {
                _quickMenuWindow = null;
            };

            _quickMenuWindow.Overlay.Closed += (_, _) =>
            {
                _quickMenuWindow?.Close();
            };

            _quickMenuWindow.Overlay.GoHomeRequested += (_, _) =>
            {
                ConsoleSettings.HideOverlay();
                var window = TopLevel.GetTopLevel(this) as Window;
                window?.Activate();
                ConsoleHome.FocusInitialDestination();
            };
            
            _quickMenuWindow.Overlay.ReturnToDesktopRequested += async (_, _) =>
            {
                ConsoleSettings.HideOverlay();
                _consoleModeRequested = false;
                _controllerConnected = false;
                await ReconcileConsoleModeAsync();
            };
            
            _quickMenuWindow.Overlay.RestartRequested += (_, _) =>
            {
                var launcher = new SafeProcessLauncher();
                launcher.Launch("systemctl", ["reboot"]);
            };
            
            _quickMenuWindow.Overlay.ShutdownRequested += (_, _) =>
            {
                var launcher = new SafeProcessLauncher();
                launcher.Launch("systemctl", ["poweroff"]);
            };

            var runningGame = ConsoleHome.RecentGames.FirstOrDefault(g => g.IsRunning)
                           ?? ConsoleHome.AllLibraryGames.FirstOrDefault(g => g.IsRunning);

            _quickMenuWindow.Overlay.SetActiveGame(runningGame);

            _quickMenuWindow.Overlay.CloseGameRequested += (_, gameVm) =>
            {
                gameVm.IsRunning = false;
                ShellWallpaper.SetGameRunningState(false);
                ConsoleHome.UpdateSelectedGameSpotlight(gameVm);
                (Application.Current as App)?.Feedback.Show(Loc.Format("Closed {0}.", gameVm.Title));
            };

            _quickMenuWindow.Overlay.RestartGameRequested += (_, gameVm) =>
            {
                _quickMenuWindow.Close();
                LaunchConsoleGame(gameVm);
            };

            _quickMenuWindow.Show();
            _quickMenuWindow.Activate();
            _quickMenuWindow.Focus();
            _quickMenuWindow.Overlay.ShowQuickMenu();
        }

    }
}
