using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
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
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Shell.Controls;

namespace DaisyOS.Shell.Views
{
    public partial class ShellView : UserControl
    {
        private TaskbarPreferences _taskbarPreferences = new();
        private DockComponentsPanel? _dockPanel;
        public DockEdge DockEdge => _taskbarPreferences.Edge;
        public double DockReservedExtent { get; private set; } = ShellSurfacePlan.PrimaryPanelExclusiveZone;
        public void ApplyTaskbarPreferences(TaskbarPreferences preferences, int taskbarSize)
        {
            _taskbarPreferences = preferences.Normalize();
            DockReservedExtent = ShellSurfacePlan.PanelExtent(_taskbarPreferences.Edge, taskbarSize, _taskbarPreferences.ShowSystemBar);
            _taskbarAutoHideTimer.Interval = _systemBarAutoHideTimer.Interval = _mediaAutoHideTimer.Interval = TimeSpan.FromMilliseconds(_taskbarPreferences.HideDelayMilliseconds);
            TaskbarContent.ApplyDockPreferences(_taskbarPreferences);
            SystemBarContent.SetDockEdge(DockEdge);
            SystemBarContent.DockOverlay?.SetDockEdge(DockEdge, DockReservedExtent);
            MediaWidgetContent.SetDockEdge(DockEdge);
            if (_dockPanel is null)
            {
                _dockPanel = new DockComponentsPanel();
                MoveToNativePanel(MediaWidgetContent, _dockPanel);
                MoveToNativePanel(TaskbarContent, _dockPanel);
                MoveToNativePanel(SystemBarContent, _dockPanel);
                DesktopExperience.Children.Add(_dockPanel);
            }
            _dockPanel.Edge = DockEdge;
            _dockPanel.DockAlignment = _taskbarPreferences.Alignment;
            _dockPanel.ReservedExtent = DockReservedExtent;
            _dockPanel.IncludeSystemBar = _taskbarPreferences.ShowSystemBar;
            _dockPanel.IncludeMedia = Desktop.ViewModel.BottomMediaWidgetVisible;
            _dockPanel.InvalidateMeasure();
            _bottomTooltips.Edge = DockEdge;
            _bottomTooltips.ReservedExtent = DockReservedExtent;
            _bottomTooltips.Enabled = _taskbarPreferences.ShowTooltips;
            _bottomTooltips.Dismiss();
            Desktop.SetDockEdge(DockEdge, DockReservedExtent);
            LauncherContent.FindControl<Grid>("LauncherContentGrid")!.Margin = DockGeometry.Inset(DockEdge, DockReservedExtent);
            ApplyBottomBarVisibility(immediate: true);
        }

        public Rect[] GetDockInputRectangles(Visual surface) => new Control[] { MediaWidgetContent, TaskbarContent, SystemBarContent }
            .Where(control => control.IsVisible && control.IsHitTestVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0)
            .Select(control =>
            {
                var point = control.TranslatePoint(default, surface) ?? default;
                return new Rect(point, control.Bounds.Size);
            }).ToArray();

        public Rect[] GetDockRevealRectangles(Size sensorSize)
        {
            if (_dockPanel is null || !IsBottomEdgeSensorEnabled) return [];
            var vertical = DockGeometry.IsVertical(DockEdge);
            var sourceSpan = vertical ? _dockPanel.Bounds.Height : _dockPanel.Bounds.Width;
            var targetSpan = vertical ? sensorSize.Height : sensorSize.Width;
            if (sourceSpan <= 0 || targetSpan <= 0) return [];
            var enabled = new[] { _mediaAutoHide && Desktop.ViewModel.BottomMediaWidgetVisible,
                _taskbarAutoHide, _systemBarAutoHide && _taskbarPreferences.ShowSystemBar };
            return Enumerable.Range(0, 3).Where(index => enabled[index]).Select(index =>
            {
                var slot = _dockPanel.GetComponentSlot(index);
                var start = (vertical ? slot.Y : slot.X) / sourceSpan * targetSpan;
                var length = (vertical ? slot.Height : slot.Width) / sourceSpan * targetSpan;
                return vertical ? new Rect(0, start, sensorSize.Width, length) : new Rect(start, 0, length, sensorSize.Height);
            }).Where(rect => rect.Width > 0 && rect.Height > 0).ToArray();
        }

        private readonly DispatcherTimer _feedbackTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        private readonly DispatcherTimer _taskbarAutoHideTimer = new() { Interval = TimeSpan.FromMilliseconds(1100) };
        private readonly DispatcherTimer _systemBarAutoHideTimer = new() { Interval = TimeSpan.FromMilliseconds(1100) };
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
        {
            InitializeComponent();
            DesktopExperience.Children.Add(_bottomTooltips);
            _bottomTooltips.Attach(TaskbarContent, TaskbarContent.HasHoverPreview);
            TaskbarContent.PreviewShown += (_, _) => _bottomTooltips.Dismiss();
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
            Unloaded += OnUnloaded;

            if (Application.Current is App feedbackApp)
            {
                feedbackApp.Feedback.MessageShown += OnFeedbackMessageShown;
                Unloaded += (_, _) => feedbackApp.Feedback.MessageShown -= OnFeedbackMessageShown;
            }
            _feedbackTimer.Tick += (_, _) =>
            {
                _feedbackTimer.Stop();
                if (this.FindControl<Control>("FeedbackHost") is { } host) host.IsVisible = false;
            };
            RegisterShellComponents();
            MetricsPanel.InitializeMonitor(_metricsMonitor);
            MetricsPanel.PanelVisibilityChanged += (_, visible) => MetricsSurfaceVisibilityChanged?.Invoke(this, visible);

            MediaWidgetContent.MoveToDesktopRequested += (_, _) =>
            {
                Desktop.ViewModel.MoveMediaWidgetToDesktop(Desktop.CurrentMetrics);
                Desktop.InvalidateReorderPanel();
            };
            MediaWidgetContent.RemoveRequested += (_, _) => Desktop.ViewModel.RemoveBottomMediaWidget();

            Desktop.ViewModel.BottomMediaWidgetVisibilityChanged += (_, visible) =>
            {
                if (!visible) { _mediaRevealed = false; _mediaAutoHideTimer.Stop(); }
                if (_dockPanel is not null) { _dockPanel.IncludeMedia = visible; _dockPanel.InvalidateMeasure(); }
                ApplyBottomBarVisibility();
            };
            SetBottomMediaWidgetVisible(Desktop.ViewModel.BottomMediaWidgetVisible, immediate: true);
        }

        private void SetBottomMediaWidgetVisible(bool visible, bool immediate = false)
        {
            _ = GuiMotion.SetVisibleAsync(MediaWidgetContent,
                MediaWidgetContent.FindControl<Border>("MediaFrame")!, visible,
                NotifyBottomChromeStateChanged, immediate, DockGeometry.Slide(DockEdge, GuiMotion.ShellSlideDistance),
                GuiMotion.ShellSlideDuration(this, visible));
        }

        private readonly ShellMetricsMonitor _metricsMonitor = new();

        public TaskbarView Taskbar => TaskbarContent;
        public SystemBarView SystemBar => SystemBarContent;
        public DaisyOS.Shell.Views.Components.Widgets.MediaWidget MediaWidget => MediaWidgetContent;
        public event EventHandler? BottomChromeStateChanged;
        public bool CanReleaseBottomWorkArea => _taskbarAutoHide && (_systemBarAutoHide || !_taskbarPreferences.ShowSystemBar) &&
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
                (Application.Current as App)?.HideLauncher(restoreFocus: false);
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
            var target = _dockPanel is not null
                ? _dockPanel.ComponentAtEdgePosition(pointerX, outputWidth)
                : (pointerX / outputWidth) switch { < 0.30 => 0, <= 0.72 => 1, >= 0.80 => 2, _ => -1 };
            if (target == 1 && _taskbarAutoHide) RevealTaskbarTemporarily();
            if (target == 2 && _systemBarAutoHide && _taskbarPreferences.ShowSystemBar) RevealSystemBarTemporarily();
            if (target == 0 && _mediaAutoHide && Desktop.ViewModel.BottomMediaWidgetVisible)
            {
                _mediaRevealed = true;
                ApplyBottomBarVisibility();
                ScheduleMediaAutoHide();
            }
        }

        public void SetLauncherOpen(bool isOpen)
        {
            TaskbarContent.SetLauncherOpen(isOpen);
            if (isOpen)
            {
                _taskbarRevealed = true;
                _systemBarRevealed = true;
                _taskbarAutoHideTimer.Stop();
                _systemBarAutoHideTimer.Stop();
                ApplyBottomBarVisibility();
                return;
            }

            if (_taskbarAutoHide &&
                !TaskbarContent.IsPointerOver &&
                !TaskbarContent.IsContextMenuVisible &&
                !DaisyOS.Shell.Services.Compositor.TaskbarPreviewState.IsShowing)
            {
                _taskbarRevealed = false;
                _taskbarAutoHideTimer.Stop();
            }
            else
            {
                ScheduleTaskbarAutoHide();
            }

            if (_systemBarAutoHide &&
                !SystemBarContent.IsPointerOver &&
                !SystemBar.IsQuickSettingsVisible &&
                !SystemBar.IsNotificationPanelVisible &&
                !SystemBar.IsCalendarPanelVisible &&
                !SystemBar.IsAppTrayVisible)
            {
                _systemBarRevealed = false;
                _systemBarAutoHideTimer.Stop();
            }
            else
            {
                ScheduleSystemBarAutoHide();
            }

            ApplyBottomBarVisibility();
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
            var taskbarVisible = !_lockScreenActive && (!_externalFullscreenActive || _taskbarAutoHide || IsLauncherVisible) && IsTaskbarVisibleByPreference;
            var systemBarVisible = _taskbarPreferences.ShowSystemBar && !_lockScreenActive && (!_externalFullscreenActive || _systemBarAutoHide || IsLauncherVisible) && (!_systemBarAutoHide || _systemBarRevealed || IsLauncherVisible);
            _ = GuiMotion.SetVisibleAsync(
                TaskbarContent,
                TaskbarContent.FindControl<Border>("TaskbarBody")!,
                taskbarVisible,
                NotifyBottomChromeStateChanged,
                immediate,
                DockGeometry.Slide(DockEdge, GuiMotion.ShellSlideDistance),
                GuiMotion.ShellSlideDuration(this, taskbarVisible), fadeOnExit: false, fadeOnEnter: false);
            _ = GuiMotion.SetVisibleAsync(
                SystemBarContent,
                SystemBarContent.FindControl<Border>("SystemBarFrame")!,
                systemBarVisible,
                NotifyBottomChromeStateChanged,
                immediate,
                DockGeometry.Slide(DockEdge, GuiMotion.ShellSlideDistance),
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
            if (_systemBarAutoHide && _systemBarRevealed && !IsLauncherVisible && !SystemBar.IsQuickSettingsVisible &&
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
            if (!_systemBarAutoHide || !_systemBarRevealed || IsLauncherVisible || SystemBarContent.IsPointerOver ||
                SystemBar.IsQuickSettingsVisible || SystemBar.IsNotificationPanelVisible || SystemBar.IsCalendarPanelVisible || SystemBar.IsAppTrayVisible) return;
            _systemBarRevealed = false;
            ApplyBottomBarVisibility();
        }
        public LauncherView Launcher => LauncherContent;
        public bool IsLauncherVisible => LauncherContent.IsOpen;
        public bool IsMetricsPanelVisible => MetricsPanel.IsPanelOpen;
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
            MediaWidgetContent.IsNativePanelHosted = true;
            _dockPanel ??= new DockComponentsPanel { IsHitTestVisible = true, Background = Brushes.Transparent };
            if (_dockPanel.Parent is Panel parent) parent.Children.Remove(_dockPanel);
            if (_dockPanel.Children.Count == 0)
            {
                MoveToNativePanel(MediaWidgetContent, _dockPanel);
                MoveToNativePanel(TaskbarContent, _dockPanel);
                MoveToNativePanel(SystemBarContent, _dockPanel);
            }
            return _dockPanel;
        }

        /// <summary>Moves the launcher into a modal, native overlay surface.</summary>
        public Control DetachLauncherForNativeOverlay()
        {
            if (LauncherContent.Parent is Panel parent)
            {
                parent.Children.Remove(LauncherContent);
            }

            // The launcher reserves room for the bottom bars within its own layout.
            var overlay = new Grid();
            LauncherContent.Margin = default;
            LauncherContent.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            LauncherContent.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
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
            control.Margin = default;
            control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
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
                activeCheck: () => true,
                customCpuWeightProvider: () => 1.3);

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

        }

        private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _feedbackTimer.Stop();
            _mediaAutoHideTimer.Stop();
            _taskbarAutoHideTimer.Stop();
            _systemBarAutoHideTimer.Stop();
            _bottomTooltips.Dismiss();
        }

        public void ShowLauncher(bool interactive = false)
        {
            DaisyOS.Shell.Services.Diagnostics.ResponsivenessTrace.Begin("launcher");
            LauncherContent.AnimateIn(interactive);
            if (!interactive) LauncherContent.FocusSearch();
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
            if (DockGeometry.IsAtEdge(position, Bounds.Size, DockEdge))
                RevealBottomBarAt(DockGeometry.IsVertical(DockEdge) ? position.Y : position.X, DockGeometry.IsVertical(DockEdge) ? Bounds.Height : Bounds.Width);
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {

            if (e.Source is not Visual source)
            {
                return;
            }

            // The taskbar/System Bar toggles must see their own press before they decide
            // whether to open or close an overlay. Everything else is an outside click.
            if (IsLauncherVisible && !IsWithin(source, LauncherContent) && !IsWithin(source, TaskbarContent) && !IsWithin(source, SystemBarContent))
            {
                (Application.Current as App)?.HideLauncher();
            }

            if ((SystemBar.IsQuickSettingsVisible || SystemBar.IsNotificationPanelVisible || SystemBar.IsCalendarPanelVisible || SystemBar.IsAppTrayVisible) && !IsWithin(source, SystemBarContent))
            {
                (Application.Current as App)?.DismissTransientShellSurfaces();
            }
        }

        private static bool IsWithin(Visual source, Visual container) =>
            ReferenceEquals(source, container) || source.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, container));

        private void OnFeedbackMessageShown(object? sender, string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                const string textName = "FeedbackText";
                if (this.FindControl<TextBlock>(textName) is { } text) text.Text = message;
                if (this.FindControl<Control>("FeedbackHost") is { } host) host.IsVisible = true;
                _feedbackTimer.Stop();
                _feedbackTimer.Start();
            });
        }

    }
}
