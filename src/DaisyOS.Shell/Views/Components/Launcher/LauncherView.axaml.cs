using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Motion;
using DaisyOS.System.Power;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Views.Components.Launcher;

public partial class LauncherView : UserControl
{
    private Task<string>? _activationToken;
    private string? _activationApp;
    private readonly InstalledApplicationService _installedApplications = new();
    private readonly DaisyOS.Shell.Services.Diagnostics.LauncherIconLoader _icons = new();
    private CancellationTokenSource? _animationCts;
    private Border? _powerMenu;
    private Border? _appContextMenu;
    private readonly MenuItem? _appContextUninstallItem;
    private Button? _contextMenuButton;
    private LauncherItemViewModel? _contextMenuItem;
    private bool _iconsReclaimed;
    public bool IsOpen { get; private set; }
    public event EventHandler<bool>? OpenStateChanged;
    public event EventHandler<bool>? PowerMenuVisibilityChanged;
    public event EventHandler<bool>? AppContextMenuVisibilityChanged;
    public bool IsPowerMenuVisible => (_powerMenu ?? this.FindControl<Border>("MenuBlurHost"))?.IsVisible == true;
    public bool IsAppContextMenuVisible => (_appContextMenu ?? this.FindControl<Border>("LauncherAppContextMenuHost"))?.IsVisible == true;

    public async void AnimateIn()
    {
        _animationCts?.Cancel();
        _animationCts?.Dispose();
        var cts = new CancellationTokenSource();
        _animationCts = cts;
        IsOpen = true;

        var content = this.FindControl<Border>("LauncherRootBorder");

        if (!IsVisible)
        {
            // A hidden control has not been measured on first use. Lay out the
            // complete card before choosing its off-screen starting position.
            // Keep the host transparent throughout this preparation.
            Opacity = 0;
            IsVisible = true;
            UpdateLayout();
            foreach (var frame in this.GetVisualDescendants().OfType<DaisyOS.Shell.Controls.ShellFrameBorder>()
                         .Where(frame => frame.IsEffectivelyVisible))
                frame.PrepareFrame();
        }
        if (content is not null)
        {
            var slideDistance = GuiMotion.BottomViewportSlideDistance(content);
            _ = GuiMotion.SetVisibleAsync(
                this,
                content,
                true,
                offset: slideDistance,
                durationOverride: GuiMotion.ShellSlideDuration(this, true),
                fadeOnEnter: false);
        }
        Opacity = 1;
        OpenStateChanged?.Invoke(this, true);

        try
        {
            await ViewModel.ApplicationsReady;
            PreloadCatalogIcons();
            do { await _icons.WhenReady; } while (!_icons.WhenReady.IsCompleted);
        }
        catch (Exception error)
        {
            global::System.Console.Error.WriteLine($"[daisy-launcher] Could not prepare launcher icons: {error.Message}");
        }
        if (!ReferenceEquals(_animationCts, cts) || !IsOpen) return;
        _animationCts = null;
        cts.Dispose();
    }

    public void CloseImmediate()
    {
        IsOpen = false;
        _animationCts?.Cancel();
        _animationCts?.Dispose();
        _animationCts = null;

        var content = this.FindControl<Border>("LauncherRootBorder");
        if (content is not null)
        {
            var slideDistance = GuiMotion.BottomViewportSlideDistance(content);
            _ = GuiMotion.SetVisibleAsync(
                this,
                content,
                false,
                immediate: true,
                offset: slideDistance,
                durationOverride: GuiMotion.ShellSlideDuration(this, false),
                fadeOnExit: false);
        }
        ClosePowerMenu();
        CloseAppContextMenu();

        Opacity = 0;
        IsVisible = false;
        OpenStateChanged?.Invoke(this, false);
    }

    public async void AnimateOut(Action onCompleted)
    {
        if (!IsOpen)
        {
            if (_animationCts != null)
            {
                return;
            }

            onCompleted();
            return;
        }

        IsOpen = false;
        OpenStateChanged?.Invoke(this, false);
        _animationCts?.Cancel();
        _animationCts?.Dispose();
        var cts = new CancellationTokenSource();
        _animationCts = cts;

        var content = this.FindControl<Border>("LauncherRootBorder");

        if (content is not null)
        {
            var slideDistance = GuiMotion.BottomViewportSlideDistance(content);
            await GuiMotion.SetVisibleAsync(
                this,
                content,
                false,
                offset: slideDistance,
                durationOverride: GuiMotion.ShellSlideDuration(this, false),
                fadeOnExit: false);
        }
        if (!cts.IsCancellationRequested && ReferenceEquals(_animationCts, cts))
        {
            _animationCts = null;
            cts.Dispose();
            onCompleted();
            OpenStateChanged?.Invoke(this, false);
        }
    }

    public override void Render(Avalonia.Media.DrawingContext context)
    {
        base.Render(context);
        DaisyOS.Shell.Services.Diagnostics.ResponsivenessTrace.Complete("launcher", "launcher.render.pass");
    }
    private void OnTileLoaded(object? sender, RoutedEventArgs e) { if (sender is Button button) RegisterTile(button); }
    private void OnTileUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button) button.Tag = null;
    }
    private void OnTileDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is Button button && button.IsLoaded) RegisterTile(button);
    }
    /// <summary>Full reload: purge cache and re-decode everything (theme change).</summary>
    private void ReloadRealizedIcons()
    {
        _icons.Dispose();
        if (ViewModel != null)
        {
            foreach (var item in ViewModel.AllApps) item.IconBitmap = null;
            PreloadCatalogIcons();
        }
        foreach (var button in this.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("LauncherTile") && button.IsLoaded))
        {
            button.Tag = null;
            RegisterTile(button);
        }
    }
    /// <summary>Soft reload: only register tiles that don't have icons yet (visibility restore).</summary>
    private void EnsureRealizedIcons()
    {
        foreach (var button in this.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("LauncherTile") && button.IsLoaded))
            RegisterTile(button);
    }
    private void RegisterTile(Button button)
    {
        if (_iconsReclaimed && !IsOpen) return;
        if (ReferenceEquals(button.Tag, button.DataContext)) return;
        button.Tag = button.DataContext;
        if (button.DataContext is LauncherItemViewModel item)
            _icons.Register(item, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
    }
    private void PreloadCatalogIcons()
    {
        if (_iconsReclaimed && !IsOpen) return;
        _iconsReclaimed = false;
        _icons.Preload(ViewModel.AllApps, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
    }

    internal void ReclaimHiddenIcons()
    {
        if (IsVisible || IsOpen || _iconsReclaimed) return;
        _iconsReclaimed = true;
        _icons.Dispose();
    }

    public LauncherViewModel ViewModel { get; }
    public event EventHandler? AppLaunchRequested;

    static LauncherView()
    {
        IsVisibleProperty.Changed.AddClassHandler<LauncherView>((view, _) =>
        {
            if (!view.IsVisible)
            {
                view.ClosePowerMenu();
                view.CloseAppContextMenu();
            }
        });
    }

    public LauncherView() : this(null) { }

    internal LauncherView(LauncherViewModel? viewModel)
    {
        InitializeComponent();
        // This menu is later reparented to a separate native layer surface.
        // Retain named children while the Launcher name scope is still intact.
        _appContextUninstallItem = this.FindControl<MenuItem>("LauncherUninstallMenuItem");
        ActualThemeVariantChanged += (_, _) => ReloadRealizedIcons();
        _ = new DaisyOS.Shell.Services.Diagnostics.VisualActivityGate(this, visible =>
        {
            if (visible) EnsureRealizedIcons();
        });
        Focusable = true;
        AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Tunnel);

        var powerButton = this.FindControl<Button>("PowerButton");
        if (powerButton != null)
        {
            powerButton.LostFocus += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                    if (focused is not Visual visual || (!IsWithinFlyout(visual) && visual != powerButton))
                    {
                        ClosePowerMenu();
                    }
                }, DispatcherPriority.Input);
            };
        }

        AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Visual visual)
            {
                var button = visual.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.DataContext is LauncherItemViewModel);
                if (button?.DataContext is LauncherItemViewModel target)
                {
                    _activationApp = target.App.Id;
                    _activationToken = Avalonia.Wayland.WaylandActivation.RequestAsync(e, target.App.Id.Replace(".desktop", ""));
                }
            }
        }, RoutingStrategies.Tunnel);
        AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space && e.Source is Visual visual)
            {
                var button = visual.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.DataContext is LauncherItemViewModel);
                if (button?.DataContext is LauncherItemViewModel target)
                {
                    _activationApp = target.App.Id;
                    _activationToken = Avalonia.Wayland.WaylandActivation.RequestAsync(e, target.App.Id.Replace(".desktop", ""));
                }
            }
        }, RoutingStrategies.Tunnel);
        ViewModel = viewModel ?? new LauncherViewModel((Application.Current as App)?.SessionState);
        Loc.RefreshOnChange(this, () => { foreach (var item in ViewModel.AllApps) item.UpdateApp(item.App); _ = ViewModel.ReloadApplicationsAsync(); });
        ViewModel.AllApps.CollectionChanged += (_, _) => PreloadCatalogIcons();
        PreloadCatalogIcons();
        DataContext = ViewModel;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Deactivated += OnWindowDeactivated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Deactivated -= OnWindowDeactivated;
        }
        ClosePowerMenu();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        ClosePowerMenu();
        CloseAppContextMenu();
    }

    public void ClosePowerMenu()
    {
        if ((_powerMenu ?? this.FindControl<Border>("MenuBlurHost")) is { IsVisible: true } menu)
        {
            menu.IsVisible = false;
            PowerMenuVisibilityChanged?.Invoke(this, false);
        }
    }

    public Control? DetachPowerMenuForNativeOverlay()
    {
        var menu = _powerMenu = this.FindControl<Border>("MenuBlurHost");
        if (menu?.Parent is Panel parent) parent.Children.Remove(menu);
        return menu;
    }

    public Control? DetachAppContextMenuForNativeOverlay()
    {
        var menu = _appContextMenu = this.FindControl<Border>("LauncherAppContextMenuHost");
        if (menu?.Parent is Panel parent) parent.Children.Remove(menu);
        return menu;
    }

    public void CloseAppContextMenu()
    {
        if ((_appContextMenu ?? this.FindControl<Border>("LauncherAppContextMenuHost")) is { IsVisible: true } menu)
        {
            menu.IsVisible = false;
            AppContextMenuVisibilityChanged?.Invoke(this, false);
        }
        _contextMenuButton?.Classes.Remove("ContextMenuActive");
        _contextMenuButton = null;
        _contextMenuItem = null;
    }

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not LauncherItemViewModel item
            || !e.GetCurrentPoint(button).Properties.IsRightButtonPressed)
        {
            return;
        }
        ShowAppContextMenu(button, item);
        e.Handled = true;
    }

    private void ShowAppContextMenu(Button button, LauncherItemViewModel item)
    {
        ClosePowerMenu();
        CloseAppContextMenu();
        var menu = _appContextMenu ?? this.FindControl<Border>("LauncherAppContextMenuHost");
        var overlay = menu is null ? null : TopLevel.GetTopLevel(menu);
        if (menu is null || overlay is null) return;

        _contextMenuButton = button;
        _contextMenuItem = item;
        button.Classes.Add("ContextMenuActive");
        if (_appContextUninstallItem is { } uninstall)
            uninstall.IsEnabled = item.CanRequestUninstall;

        // Position margins participate in DesiredSize. Never include the
        // previous anchor offset when measuring a reopened menu.
        menu.Margin = default;
        menu.IsVisible = true;
        menu.Measure(Size.Infinity);
        var targetScreen = button.PointToScreen(default);
        var overlayScreen = overlay.PointToScreen(default);
        var scale = Math.Max(overlay.RenderScaling, 0.01);
        var x = (targetScreen.X - overlayScreen.X) / scale + button.Bounds.Width;
        var y = (targetScreen.Y - overlayScreen.Y) / scale;
        var width = Math.Max(menu.DesiredSize.Width, 204);
        var height = menu.DesiredSize.Height;
        var bounds = overlay.Bounds;
        if (x + width > bounds.Width - 6) x = (targetScreen.X - overlayScreen.X) / scale - width;
        if (y + height > bounds.Height - 6) y = bounds.Height - height - 6;
        menu.Margin = new Thickness(Math.Clamp(x, 6, Math.Max(6, bounds.Width - width - 6)), Math.Clamp(y, 6, Math.Max(6, bounds.Height - height - 6)), 0, 0);
        AppContextMenuVisibilityChanged?.Invoke(this, true);
        menu.Focus();
    }

    private void OnPowerButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (IsPowerMenuVisible)
        {
            ClosePowerMenu();
            return;
        }

        var menu = _powerMenu ?? this.FindControl<Border>("MenuBlurHost");
        var button = this.FindControl<Button>("PowerButton");
        var surface = TopLevel.GetTopLevel(this);
        if (menu is null || button is null || surface is null) return;
        // Measure the real visible card before mapping its native overlay.
        // This also lets KWinBlur publish the region before the surface is
        // presented, avoiding the old one-frame unblurred flash.
        // Position margins participate in DesiredSize. Never include the
        // previous anchor offset when measuring a reopened menu.
        menu.Margin = default;
        menu.IsVisible = true;
        menu.Measure(Size.Infinity);
        // The menu lives on a different layer surface, so visual-tree
        // translation is not valid here and can return null when an app owns
        // focus. Convert both native surfaces through screen pixels instead.
        var buttonScreen = button.PointToScreen(default);
        var surfaceScreen = surface.PointToScreen(default);
        var scale = Math.Max(surface.RenderScaling, 0.01);
        var buttonTopLeft = new Point(
            (buttonScreen.X - surfaceScreen.X) / scale,
            (buttonScreen.Y - surfaceScreen.Y) / scale);
        var width = Math.Max(menu.DesiredSize.Width, 184);
        var height = menu.DesiredSize.Height;
        menu.Margin = new Thickness(
            Math.Max(6, buttonTopLeft.X + button.Bounds.Width - width),
            Math.Max(6, buttonTopLeft.Y - height), 0, 0);
        PowerMenuVisibilityChanged?.Invoke(this, true);
        menu.Focus();
        e.Handled = true;
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (focused is null || (!IsWithinFlyout(focused as Visual) && (focused as Visual) != this.FindControl<Button>("PowerButton")))
            {
                ClosePowerMenu();
            }
        }, DispatcherPriority.Input);
    }

    private static bool IsWithinFlyout(Visual? visual)
    {
        while (visual != null)
        {
            if (visual is MenuFlyoutPresenter or MenuItem or FlyoutPresenter)
                return true;
            visual = visual.GetVisualParent();
        }
        return false;
    }

    private async void OnAppItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is LauncherItemViewModel item)
        {
            await LaunchItemAsync(item);
        }
    }

    private async void OnContextOpenClicked(object? sender, RoutedEventArgs e)
    {
        if (_contextMenuItem is not { } item) return;
        CloseAppContextMenu();
        await LaunchItemAsync(item);
    }

    private void OnAddToDesktopClicked(object? sender, RoutedEventArgs e)
    {
        if (_contextMenuItem is not { } item) return;
        CloseAppContextMenu();
        try
        {
            var path = _installedApplications.AddToDesktop(item.App);
            (Application.Current as App)?.Feedback.Show(path is null
                ? Loc.Format("{0} does not provide a desktop shortcut.", item.Name)
                : Loc.Format("Added {0} to the Desktop.", item.Name));
        }
        catch (Exception ex)
        {
            (Application.Current as App)?.Feedback.Show(Loc.Format("Couldn’t add {0} to the Desktop: {1}", item.Name, ex.Message));
        }
    }

    private void OnPinToTaskbarClicked(object? sender, RoutedEventArgs e)
    {
        if (_contextMenuItem is not { } item || Application.Current is not App app) return;
        CloseAppContextMenu();
        app.PinApplicationToTaskbar(item.App.Id);
        app.Feedback.Show(Loc.Format("Pinned {0} to the Taskbar.", item.Name));
    }

    private async void OnLauncherUninstallClicked(object? sender, RoutedEventArgs e)
    {
        if (_contextMenuItem is not { } item) return;
        CloseAppContextMenu();
        var target = await _installedApplications.ResolveAsync(item.App);
        if (!target.CanManage)
        {
            (Application.Current as App)?.Feedback.Show(target.UnavailableReason);
            return;
        }

        if (!await ConfirmUninstallAsync(item.Name)) return;
        var removed = await _installedApplications.UninstallAsync(target);
        (Application.Current as App)?.Feedback.Show(removed
            ? Loc.Format("{0} was uninstalled.", item.Name)
            : Loc.Format("Couldn’t uninstall {0}.", item.Name));
        if (removed) await ViewModel.ReloadApplicationsAsync();
    }

    private async Task<bool> ConfirmUninstallAsync(string appName)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return false;
        return await DaisyOS.Shell.Views.Dialogs.ConfirmationDialog.ShowApplicationRemovalAsync(owner, appName);
    }

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ViewModel.TopSearchResult is not { } item)
        {
            return;
        }

        e.Handled = true;
        _activationApp = item.App.Id;
        _activationToken = Avalonia.Wayland.WaylandActivation.RequestAsync(e, item.App.Id.Replace(".desktop", ""));
        await LaunchItemAsync(item);
    }

    private async Task LaunchItemAsync(LauncherItemViewModel item)
    {
        ClosePowerMenu();
        // Launcher activation dismisses immediately, matching established desktop launchers.
        AppLaunchRequested?.Invoke(this, EventArgs.Empty);
        var tokenTask = _activationApp == item.App.Id ? _activationToken : null;
        _activationToken = null; _activationApp = null;
        var result = await ViewModel.LaunchAppAsync(item, tokenTask is null ? "" : await tokenTask);
        if (result is { Succeeded: true })
        {
            ViewModel.RecordSuccessfulLaunch(item);
        }
        else if (result is { Succeeded: false })
        {
            (Application.Current as App)?.Feedback.Show(Loc.Get(result.Message));
        }
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        if (IsAppContextMenuVisible)
        {
            CloseAppContextMenu();
        }
        var powerButton = this.FindControl<Button>("PowerButton");
        if (e.Source is not Visual source || (source != powerButton && !source.GetVisualAncestors().Contains(powerButton)))
        {
            ClosePowerMenu();
        }

        if (e.Source is not Visual src || IsInteractive(src))
        {
            return;
        }

        // A click on launcher canvas is a deliberate exit from search editing.
        // Move focus to the launcher itself so the TextBox reliably receives LostFocus.
        Focus();
    }

    private static bool IsInteractive(Visual source) =>
        source is TextBox or Button ||
        source.GetVisualAncestors().Any(ancestor => ancestor is TextBox or Button);

    public void FocusSearch()
    {
        ClosePowerMenu();
        if (!string.IsNullOrEmpty(ViewModel.SearchText))
        {
            ViewModel.SearchText = string.Empty;
        }
        var searchBar = this.FindControl<Controls.SearchBar>("LauncherSearchBar");
        searchBar?.FocusInput();
    }

    private void OnLockClicked(object? sender, RoutedEventArgs e)
    {
        ClosePowerMenu();
        (Application.Current as App)?.LockSession();
    }

    private async void OnSuspendClicked(object? sender, RoutedEventArgs e)
    {
        ClosePowerMenu();
        await RunPowerActionAsync(service => service.SuspendAsync());
    }

    private async void OnLogOutClicked(object? sender, RoutedEventArgs e)
    {
        ClosePowerMenu();
        await RunPowerActionAsync(service => service.LogOutAsync());
    }

    private async void OnRestartClicked(object? sender, RoutedEventArgs e)
    {
        ClosePowerMenu();
        await RunPowerActionAsync(service => service.RebootAsync());
    }

    private async void OnShutdownClicked(object? sender, RoutedEventArgs e)
    {
        ClosePowerMenu();
        await RunPowerActionAsync(service => service.ShutdownAsync());
    }

    private static async Task RunPowerActionAsync(Func<LinuxPowerService, Task<DaisyOS.Core.Models.PowerActionResult>> action)
    {
        // Each menu selection is deliberate; systemd/logind remains the policy authority and
        // returns a clean failure when the session is not allowed to perform the action.
        try
        {
            var result = await action(new LinuxPowerService(new SafeCommandRunner()));
            if (!result.Succeeded) (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t complete that power action."));
        }
        catch (Exception exception)
        {
            global::System.Console.Error.WriteLine($"[power] Action failed: {exception.Message}");
            (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t complete that power action."));
        }
    }
}
