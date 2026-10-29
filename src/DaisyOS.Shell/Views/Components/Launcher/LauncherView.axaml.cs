using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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
    private static readonly Easing StageEnterEasing = new SplineEasing(0.16, 1.0, 0.3, 1.0);
    private static readonly Easing StageExitEasing = new CubicEaseIn();

    private Task<string>? _activationToken;
    private string? _activationApp;
    private readonly InstalledApplicationService _installedApplications = new();
    private readonly DaisyOS.Shell.Services.Diagnostics.LauncherIconLoader _icons = new();
    private CancellationTokenSource? _animationCts;
    private CancellationTokenSource? _stageAnimationCts;
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
    public event EventHandler? BackdropStyleChanged;
    public bool UseSolidBackdrop { get; private set; }
    public bool IsPowerMenuVisible => (_powerMenu ?? this.FindControl<Border>("MenuBlurHost"))?.IsVisible == true;
    public bool IsAppContextMenuVisible => (_appContextMenu ?? this.FindControl<Border>("LauncherAppContextMenuHost"))?.IsVisible == true;

    /// <summary>
    /// Gaming mode and fullscreen clients use an opaque backdrop. A full-output
    /// KWin blur sample is unnecessary there and competes with the game for
    /// GPU time; the translucent role is only used on the desktop.
    /// </summary>
    public void SetSolidBackdrop(bool solid)
    {
        if (UseSolidBackdrop == solid) return;
        UseSolidBackdrop = solid;
        var root = this.FindControl<Border>("LauncherRootBorder");
        root?.Classes.Set("SolidBackdrop", solid);
        BackdropStyleChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True when the pointer landed on the live launcher column.</summary>
    public bool IsWithinInteractiveContent(Visual source) =>
        this.FindControl<Grid>("LauncherInteractiveContent") is { } content &&
        IsWithin(source, content);

    /// <summary>
    /// True when the pointer landed on live launcher chrome. Everything else is
    /// the dismissal backdrop, including the area around the centred column.
    /// </summary>
    public bool IsWithinInteractiveSurface(Visual source) =>
        IsWithinInteractiveContent(source) ||
        (this.FindControl<Border>("MenuBlurHost") is { } powerMenu && IsWithin(source, powerMenu)) ||
        (this.FindControl<Border>("LauncherAppContextMenuHost") is { } appMenu && IsWithin(source, appMenu));

    private static bool IsWithin(Visual source, Visual target) =>
        ReferenceEquals(source, target) ||
        source.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, target));

    private TimeSpan ResolveLauncherDuration(bool entering)
    {
        if (Application.Current is App { ReduceMotionEnabled: true })
            return TimeSpan.Zero;
        var key = entering ? "MotionDurationEnter" : "MotionDurationExit";
        if (Resources.TryGetResource(key, ActualThemeVariant, out var localOverride) && localOverride is TimeSpan overridden)
            return overridden;
        return TimeSpan.FromMilliseconds(entering ? 190 : 115);
    }

    private void AnimateStage(bool entering, TimeSpan duration, bool immediate = false)
    {
        var stage = this.FindControl<Grid>("LauncherStage");
        if (stage is null) return;

        var startScale = entering ? 0.972 : 1.0;
        var startY = entering ? 14.0 : 0.0;
        if (_stageAnimationCts is not null && stage.RenderTransform is { } existing)
        {
            startScale = existing.Value.M11;
            startY = existing.Value.M32;
        }

        _stageAnimationCts?.Cancel();
        _stageAnimationCts?.Dispose();
        _stageAnimationCts = null;

        if (immediate || duration <= TimeSpan.Zero || TopLevel.GetTopLevel(this) is not { } host)
        {
            stage.RenderTransform = null;
            return;
        }

        var cts = new CancellationTokenSource();
        _stageAnimationCts = cts;
        var targetScale = entering ? 1.0 : 0.985;
        var targetY = entering ? 0.0 : 8.0;
        var easing = entering ? StageEnterEasing : StageExitEasing;
        TimeSpan? lastFrame = null;
        var elapsedSeconds = 0d;
        var totalSeconds = duration.TotalSeconds;

        ApplyStageTransform(stage, startScale, startY);

        void Frame(TimeSpan timestamp)
        {
            if (cts.IsCancellationRequested || !ReferenceEquals(_stageAnimationCts, cts)) return;
            var dt = lastFrame is { } prev ? Math.Max(0, (timestamp - prev).TotalSeconds) : 0;
            lastFrame = timestamp;
            elapsedSeconds += dt;
            if (elapsedSeconds >= totalSeconds)
            {
                _stageAnimationCts = null;
                cts.Dispose();
                stage.RenderTransform = entering ? null : CreateStageTransform(targetScale, targetY);
                return;
            }

            var t = easing.Ease(Math.Clamp(elapsedSeconds / totalSeconds, 0, 1));
            var s = startScale + (targetScale - startScale) * t;
            var y = startY + (targetY - startY) * t;
            ApplyStageTransform(stage, s, y);
            host.RequestAnimationFrame(Frame);
        }

        host.RequestAnimationFrame(Frame);
    }

    private static TransformGroup CreateStageTransform(double scale, double translateY)
    {
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(scale, scale));
        group.Children.Add(new TranslateTransform(0, translateY));
        return group;
    }

    private static void ApplyStageTransform(Control stage, double scale, double translateY)
    {
        if (stage.RenderTransform is TransformGroup { Children.Count: 2 } group &&
            group.Children[0] is ScaleTransform scaleTransform &&
            group.Children[1] is TranslateTransform translateTransform)
        {
            scaleTransform.ScaleX = scale;
            scaleTransform.ScaleY = scale;
            translateTransform.Y = translateY;
            return;
        }
        stage.RenderTransform = CreateStageTransform(scale, translateY);
    }

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

        var duration = ResolveLauncherDuration(entering: true);
        if (content is not null)
        {
            _ = GuiMotion.SetScaleVisibleAsync(
                this,
                content,
                true,
                closedScale: 1.0,
                durationOverride: duration,
                easingOverride: StageEnterEasing);
        }
        AnimateStage(entering: true, duration);

        if (ViewModel.PageIndex != 0)
        {
            ViewModel.GoToPage(0);
        }
        ViewModel.ResetSelection();
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
        CancelPageDrag();
        _animationCts?.Cancel();
        _animationCts?.Dispose();
        _animationCts = null;
        AnimateStage(entering: false, TimeSpan.Zero, immediate: true);

        var content = this.FindControl<Border>("LauncherRootBorder");
        if (content is not null)
        {
            _ = GuiMotion.SetScaleVisibleAsync(
                this,
                content,
                false,
                immediate: true,
                closedScale: 1.0);
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
        CancelPageDrag();
        OpenStateChanged?.Invoke(this, false);
        _animationCts?.Cancel();
        _animationCts?.Dispose();
        var cts = new CancellationTokenSource();
        _animationCts = cts;

        var duration = ResolveLauncherDuration(entering: false);
        var content = this.FindControl<Border>("LauncherRootBorder");
        AnimateStage(entering: false, duration);

        if (content is not null)
        {
            await GuiMotion.SetScaleVisibleAsync(
                this,
                content,
                false,
                closedScale: 1.0,
                durationOverride: duration,
                easingOverride: StageExitEasing);
        }
        if (!cts.IsCancellationRequested && ReferenceEquals(_animationCts, cts))
        {
            _animationCts = null;
            cts.Dispose();
            AnimateStage(entering: false, TimeSpan.Zero, immediate: true);
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
        // Queue currently visible page tiles ahead of off-screen pages for instant first-frame icons.
        _icons.Preload(ViewModel.PageApps.Concat(ViewModel.AllApps), TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
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

    private double _pageWheelDelta;
    private long _lastPageWheelTime;
    private long _lastPageChangeTime;

    private void OnPageViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is not LauncherViewModel model) return;
        model.SetPageLayout(
            (int)(e.NewSize.Width / 136),
            Math.Min(LauncherViewModel.MaxPageRows, (int)(e.NewSize.Height / 128)));
    }

    private void OnPageWheel(object? sender, PointerWheelEventArgs e)
    {
        if (IsWithinFlyout(e.Source as Visual ?? this)) return;
        if (e.IsPrecise)
        {
            e.Handled = true;
            DragPage(e.Delta);
            if (e.IsScrollEnd) SettlePageDrag();
            return;
        }
        var now = Environment.TickCount64;
        if (now - _lastPageWheelTime > 160) _pageWheelDelta = 0;
        _lastPageWheelTime = now;
        e.Handled = true;
        if (now - _lastPageChangeTime < 220) return;
        var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
        _pageWheelDelta += delta;
        if (Math.Abs(_pageWheelDelta) < 1) return;
        ViewModel.GoToPage(ViewModel.PageIndex + (_pageWheelDelta < 0 ? 1 : -1));
        _pageWheelDelta = 0;
        _lastPageChangeTime = now;
    }

    private readonly DispatcherTimer _pageDragIdle = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private bool _pageDragging;
    private bool _committingDrag;
    private double _pageDragOffset;
    private int _dragNeighbour = -1;
    private int _dragGeneration;
    private double PageStride => Math.Max(1, this.FindControl<Grid>("LauncherPageViewport")!.Bounds.Width);

    private void DragPage(Vector delta)
    {
        if (ViewModel.PageCount <= 1) return;
        var movement = Math.Abs(delta.X) > Math.Abs(delta.Y) ? delta.X : delta.Y;
        if (movement == 0) return;
        if (!_pageDragging)
        {
            ++_pageTransitionGeneration;
            var current = this.FindControl<ItemsControl>("ApplicationGrid")!;
            GuiMotion.Cancel(current);
            GuiMotion.Cancel(this.FindControl<ItemsControl>("OutgoingPageGrid")!);
            _pageDragOffset = 0;
            _pageDragging = true;
            current.IsHitTestVisible = false;
            CloseAppContextMenu();
        }
        ++_dragGeneration; // Interrupt settling without resetting the finger position.
        _pageDragOffset = Math.Clamp(_pageDragOffset + movement, -PageStride, PageStride);
        RenderPageDrag(_pageDragOffset);
        _pageDragIdle.Stop();
        _pageDragIdle.Start(); // Fallback for devices that do not send axis_stop.
    }

    private void RenderPageDrag(double offset)
    {
        var current = this.FindControl<ItemsControl>("ApplicationGrid")!;
        var neighbour = this.FindControl<ItemsControl>("OutgoingPageGrid")!;
        var direction = offset < 0 ? 1 : -1;
        var index = ViewModel.PageIndex + direction;
        var valid = index >= 0 && index < ViewModel.PageCount;
        if (_dragNeighbour != index || neighbour.ItemsSource is null)
        {
            _dragNeighbour = index;
            neighbour.ItemsSource = valid ? ViewModel.GetPageApps(index) : null;
        }
        neighbour.IsVisible = valid;
        // Resist pulling beyond the first or last page.
        var visibleOffset = valid ? offset : offset * 0.2;
        current.Opacity = neighbour.Opacity = 1;
        current.RenderTransform = new TranslateTransform(visibleOffset, 0);
        neighbour.RenderTransform = new TranslateTransform(visibleOffset + direction * PageStride, 0);
    }

    private void SettlePageDrag()
    {
        _pageDragIdle.Stop();
        if (!_pageDragging) return;
        var generation = ++_dragGeneration;
        var from = _pageDragOffset;
        var direction = from < 0 ? 1 : -1;
        var targetPage = ViewModel.PageIndex + direction;
        var commit = Math.Abs(from) >= Math.Min(60, PageStride * 0.10) &&
            targetPage >= 0 && targetPage < ViewModel.PageCount;
        var to = commit ? -direction * PageStride : 0;
        TimeSpan? start = null;
        void Finish()
        {
            if (generation != _dragGeneration) return;
            _committingDrag = true;
            _pageDragging = false;
            _pageDragOffset = 0;
            ViewModel.GoToPage(commit ? targetPage : ViewModel.PageIndex);
            _committingDrag = false;
        }
        if (ResolveLauncherDuration(entering: true) == TimeSpan.Zero || TopLevel.GetTopLevel(this) is not { } host)
        {
            Finish();
            return;
        }
        void Frame(TimeSpan time)
        {
            if (generation != _dragGeneration) return;
            start ??= time;
            var t = Math.Clamp((time - start.Value).TotalMilliseconds / 180, 0, 1);
            _pageDragOffset = from + (to - from) * (1 - Math.Pow(1 - t, 3));
            RenderPageDrag(_pageDragOffset);
            if (t >= 1) Finish();
            else host.RequestAnimationFrame(Frame);
        }
        host.RequestAnimationFrame(Frame);
    }

    private void CancelPageDrag()
    {
        ++_dragGeneration;
        _pageDragIdle.Stop();
        _pageDragging = false;
        _pageDragOffset = 0;
        _dragNeighbour = -1;
        this.FindControl<ItemsControl>("ApplicationGrid")!.IsHitTestVisible = true;
    }

    private LauncherItemViewModel[] _displayedPage = [];
    private int _displayedPageIndex;
    private int _pageTransitionGeneration;

    private async void OnPageChanged()
    {
        CancelPageDrag();
        var incoming = this.FindControl<ItemsControl>("ApplicationGrid")!;
        var outgoing = this.FindControl<ItemsControl>("OutgoingPageGrid")!;
        var previous = _displayedPage;
        var direction = Math.Sign(ViewModel.PageIndex - _displayedPageIndex);
        _displayedPageIndex = ViewModel.PageIndex;
        _displayedPage = ViewModel.PageApps.ToArray();
        var generation = ++_pageTransitionGeneration;
        GuiMotion.Cancel(incoming);
        GuiMotion.Cancel(outgoing);
        incoming.Opacity = 1;
        incoming.RenderTransform = null;
        outgoing.IsVisible = false;
        outgoing.ItemsSource = null;
        UpdatePageIndicators();
        if (_committingDrag || direction == 0 || previous.Length == 0 || !IsEffectivelyVisible ||
            TopLevel.GetTopLevel(this) is null || ResolveLauncherDuration(entering: true) == TimeSpan.Zero)
            return;

        CloseAppContextMenu();
        outgoing.ItemsSource = previous;
        outgoing.IsVisible = true;
        outgoing.Opacity = 1;
        outgoing.RenderTransform = null;
        // Move only the tiles: the material, search, dots, and footer stay still.
        var duration = TimeSpan.FromMilliseconds(190);
        await Task.WhenAll(
            GuiMotion.SlideOutAsync(outgoing, new Point(-direction * PageStride, 0), duration),
            GuiMotion.SlideInAsync(incoming, new Point(direction * PageStride, 0), duration));
        if (generation != _pageTransitionGeneration) return;
        outgoing.IsVisible = false;
        outgoing.ItemsSource = null;
    }

    private IBrush ResolveIndicatorBrush(bool active)
    {
        var key = active ? "LauncherDotActiveBrush" : "LauncherDotInactiveBrush";
        if (this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush)
            return brush;
        if (this.TryFindResource("ContentPrimaryBrush", ActualThemeVariant, out var fallback) && fallback is IBrush contentBrush)
            return contentBrush;
        return Brushes.Gray;
    }

    private void UpdatePageIndicators()
    {
        var host = this.FindControl<StackPanel>("LauncherPageIndicators");
        if (host is null) return;

        var multiPage = ViewModel.PageCount > 1;
        host.Opacity = multiPage ? 1 : 0;
        host.IsHitTestVisible = multiPage;
        host.Width = multiPage ? double.NaN : 0;

        var activeBrush = ResolveIndicatorBrush(active: true);
        var inactiveBrush = ResolveIndicatorBrush(active: false);

        if (host.Children.Count == ViewModel.PageCount)
        {
            for (var i = 0; i < host.Children.Count; i++)
            {
                if (host.Children[i] is Button { Content: Border pill })
                {
                    var isCurrent = i == ViewModel.PageIndex;
                    pill.Width = isCurrent ? 20 : 7;
                    pill.Background = isCurrent ? activeBrush : inactiveBrush;
                    pill.Opacity = isCurrent ? 0.95 : 0.75;
                }
            }
            return;
        }

        host.Children.Clear();
        var transitionDuration = ResolveLauncherDuration(entering: true) == TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(150);

        for (var index = 0; index < ViewModel.PageCount; index++)
        {
            var page = index;
            var isCurrent = index == ViewModel.PageIndex;
            var button = new Button
            {
                Classes = { "LauncherPageDotButton" },
                Height = 24,
                Padding = new Thickness(5, 8),
                Content = new Border
                {
                    Width = isCurrent ? 20 : 7,
                    Height = 7,
                    CornerRadius = new CornerRadius(999),
                    Background = isCurrent ? activeBrush : inactiveBrush,
                    Opacity = isCurrent ? 0.95 : 0.75,
                    IsHitTestVisible = false,
                    Transitions = new Transitions
                    {
                        new DoubleTransition { Property = WidthProperty, Duration = transitionDuration, Easing = StageEnterEasing },
                        new DoubleTransition { Property = OpacityProperty, Duration = transitionDuration, Easing = StageEnterEasing }
                    }
                }
            };
            Avalonia.Automation.AutomationProperties.SetName(button, $"Page {index + 1} of {ViewModel.PageCount}");
            button.Click += (_, _) => ViewModel.GoToPage(page);
            host.Children.Add(button);
        }
    }

    public LauncherView() : this(null) { }

    internal LauncherView(LauncherViewModel? viewModel)
    {
        InitializeComponent();
        _pageDragIdle.Tick += (_, _) => SettlePageDrag();
        // This menu is later reparented to a separate native layer surface.
        // Retain named children while the Launcher name scope is still intact.
        _appContextUninstallItem = this.FindControl<MenuItem>("LauncherUninstallMenuItem");
        ActualThemeVariantChanged += (_, _) =>
        {
            ReloadRealizedIcons();
            UpdatePageIndicators();
        };
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

        AddHandler(InputElement.KeyDownEvent, OnLauncherTunnelKeyDown, RoutingStrategies.Tunnel);
        AddHandler(InputElement.TextInputEvent, OnLauncherTunnelTextInput, RoutingStrategies.Tunnel);

        ViewModel = viewModel ?? new LauncherViewModel((Application.Current as App)?.SessionState);
        Loc.RefreshOnChange(this, () => { foreach (var item in ViewModel.AllApps) item.UpdateApp(item.App); _ = ViewModel.ReloadApplicationsAsync(); });
        ViewModel.AllApps.CollectionChanged += (_, _) => PreloadCatalogIcons();
        PreloadCatalogIcons();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LauncherViewModel.PageIndex)) OnPageChanged();
            else if (e.PropertyName == nameof(LauncherViewModel.PageCount)) UpdatePageIndicators();
        };
        AddHandler(InputElement.PointerWheelChangedEvent, OnPageWheel, RoutingStrategies.Tunnel);
        OnPageChanged();
    }

    private TextBox? GetSearchInput() =>
        this.FindControl<Controls.SearchBar>("LauncherSearchBar")?.FindControl<TextBox>("SearchInput");

    private void FocusSearchAtEnd()
    {
        if (GetSearchInput() is not { } input) return;
        input.Focus();
        var len = input.Text?.Length ?? 0;
        input.SelectionStart = len;
        input.SelectionEnd = len;
    }

    private static LauncherItemViewModel? GetFocusedTileItem(Visual? source) =>
        source?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.DataContext is LauncherItemViewModel)?.DataContext as LauncherItemViewModel;

    private async void OnLauncherTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsWithinFlyout(e.Source as Visual)) return;

        // Space activates an explicitly focused tile button without interfering with typing spaces in Search.
        if (e.Key == Key.Space && e.Source is Visual spaceVisual && GetSearchInput()?.IsFocused != true)
        {
            if (GetFocusedTileItem(spaceVisual) is { } tileTarget)
            {
                _activationApp = tileTarget.App.Id;
                _activationToken = Avalonia.Wayland.WaylandActivation.RequestAsync(e, tileTarget.App.Id.Replace(".desktop", ""));
            }
            return;
        }

        // Alt+1..9 or Ctrl+1..9 launches visible page tiles directly.
        if ((e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.KeyModifiers.HasFlag(KeyModifiers.Control)) &&
            e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            var quickIndex = (int)(e.Key - Key.D1);
            if (quickIndex >= 0 && quickIndex < ViewModel.PageApps.Count)
            {
                var quickTarget = ViewModel.PageApps[quickIndex];
                e.Handled = true;
                _activationApp = quickTarget.App.Id;
                _activationToken = Avalonia.Wayland.WaylandActivation.RequestAsync(e, quickTarget.App.Id.Replace(".desktop", ""));
                await LaunchItemAsync(quickTarget);
                return;
            }
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.U)
        {
            if (!string.IsNullOrEmpty(ViewModel.SearchText))
            {
                ViewModel.SearchText = string.Empty;
                FocusSearchAtEnd();
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
            {
                var target = ViewModel.HasSearchQuery
                    ? ViewModel.ResolveSearchResult()
                    : (GetFocusedTileItem(e.Source as Visual) ?? ViewModel.SelectedItem);
                if (target is null) return;

                e.Handled = true;
                _activationApp = target.App.Id;
                _activationToken = Avalonia.Wayland.WaylandActivation.RequestAsync(e, target.App.Id.Replace(".desktop", ""));
                await LaunchItemAsync(target);
                return;
            }

            case Key.Down:
            case Key.Up:
                if (ViewModel.MoveSelection(0, e.Key == Key.Down ? 1 : -1))
                {
                    e.Handled = true;
                }
                return;

            case Key.Tab:
            {
                var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
                if (ViewModel.MoveSelection(step, 0))
                {
                    FocusSearchAtEnd();
                    e.Handled = true;
                }
                return;
            }

            case Key.Left:
            case Key.Right:
            {
                if (e.KeyModifiers != KeyModifiers.None) return;
                var delta = e.Key == Key.Right ? 1 : -1;
                if (!ViewModel.HasSearchQuery)
                {
                    if (ViewModel.MoveSelection(delta, 0)) e.Handled = true;
                    return;
                }

                var input = GetSearchInput();
                var searchFocused = input?.IsFocused == true;
                var navigatedBeyondFirst = ViewModel.SelectedItem is not null &&
                    !ReferenceEquals(ViewModel.SelectedItem, ViewModel.FilteredApps.FirstOrDefault());
                var caretAtEnd = input is not null &&
                    input.SelectionStart == (input.Text?.Length ?? 0) &&
                    input.SelectionEnd == (input.Text?.Length ?? 0);

                if (!searchFocused || navigatedBeyondFirst || (e.Key == Key.Right && caretAtEnd))
                {
                    if (ViewModel.MoveSelection(delta, 0)) e.Handled = true;
                }
                return;
            }

            case Key.PageDown:
            case Key.PageUp:
                if (ViewModel.PageCount > 1)
                {
                    ViewModel.GoToPage(ViewModel.PageIndex + (e.Key == Key.PageDown ? 1 : -1));
                    ViewModel.ResetSelection();
                    e.Handled = true;
                }
                return;

            case Key.Back:
            {
                var input = GetSearchInput();
                if (input is not null && !input.IsFocused && !string.IsNullOrEmpty(ViewModel.SearchText))
                {
                    ViewModel.SearchText = ViewModel.SearchText[..^1];
                    FocusSearchAtEnd();
                    e.Handled = true;
                }
                return;
            }
        }
    }

    private void OnLauncherTunnelTextInput(object? sender, TextInputEventArgs e)
    {
        if (IsWithinFlyout(e.Source as Visual)) return;
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        var input = GetSearchInput();
        if (input is null || input.IsFocused) return;

        ViewModel.SearchText += e.Text;
        FocusSearchAtEnd();
        e.Handled = true;
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
        CancelPageDrag();
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
        if (e.Handled || e.Key != Key.Enter) return;
        if (ViewModel.ResolveSearchResult() is not { } item) return;

        e.Handled = true;
        _activationApp = item.App.Id;
        _activationToken = Avalonia.Wayland.WaylandActivation.RequestAsync(e, item.App.Id.Replace(".desktop", ""));
        await LaunchItemAsync(item);
    }

    private async Task LaunchItemAsync(LauncherItemViewModel item)
    {
        ClosePowerMenu();
        CloseAppContextMenu();
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

        if (!IsWithinInteractiveSurface(src))
        {
            // The fullscreen backdrop is the dismissal target. The native
            // surface has the same boundary in App.ConfigureTransientSurfaceDismissal;
            // keeping it here too preserves the behaviour when the launcher is
            // hosted inline by the windowed prototype.
            e.Handled = true;
            (Application.Current as App)?.HideLauncher();
            return;
        }

        // Keep search immediately ready for typing while clearing any stale tile focus.
        FocusSearchAtEnd();
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
        if (Application.Current is App app) await app.SuspendSessionAsync();
        else await RunPowerActionAsync(service => service.SuspendAsync());
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
