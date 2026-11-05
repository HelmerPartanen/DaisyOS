using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Runtime.CompilerServices;

namespace DaisyOS.Shell.Apps;

/// <summary>
/// Keeps DaisyOS-owned application windows visually honest as their native state changes.
/// Maximized windows keep desktop-style controls; explicit full screen removes all chrome.
/// </summary>
internal sealed class SystemAppWindowChrome : IDisposable
{
    private static readonly ConditionalWeakTable<Window, SystemAppWindowChrome> Instances = new();
    private readonly Window _window;
    private readonly Control _titleBar;
    private readonly Grid _contentLayout;
    private readonly string _normalRowDefinitions;
    private readonly string _fullscreenRowDefinitions;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;
    private bool _isDisposed;
    private bool _reapplyQueued;

    public SystemAppWindowChrome(
        Window window,
        Control titleBar,
        Grid contentLayout,
        string normalRowDefinitions = "44,*",
        string fullscreenRowDefinitions = "0,*")
    {
        _window = window;
        _titleBar = titleBar;
        _contentLayout = contentLayout;
        _normalRowDefinitions = normalRowDefinitions;
        _fullscreenRowDefinitions = fullscreenRowDefinitions;

        Instances.Add(_window, this);
        _window.PropertyChanged += OnWindowPropertyChanged;
        _window.Opened += OnWindowOpened;
        _window.Activated += OnWindowActivated;
        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.AddHandler(InputElement.PointerPressedEvent, OnEdgePointerPressed, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.PointerMovedEvent, OnEdgePointerMoved, RoutingStrategies.Tunnel);
        ApplyWindowState();
    }

    public void Dispose()
    {
        _isDisposed = true;
        Instances.Remove(_window);
        _window.PropertyChanged -= OnWindowPropertyChanged;
        _window.Opened -= OnWindowOpened;
        _window.Activated -= OnWindowActivated;
        _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _window.RemoveHandler(InputElement.PointerPressedEvent, OnEdgePointerPressed);
        _window.RemoveHandler(InputElement.PointerMovedEvent, OnEdgePointerMoved);
    }

    private static readonly Lazy<IReadOnlyDictionary<WindowEdge, Cursor>> ResizeCursors = new(() => new Dictionary<WindowEdge, Cursor>
    {
        [WindowEdge.North] = new(StandardCursorType.TopSide),
        [WindowEdge.South] = new(StandardCursorType.BottomSide),
        [WindowEdge.West] = new(StandardCursorType.LeftSide),
        [WindowEdge.East] = new(StandardCursorType.RightSide),
        [WindowEdge.NorthWest] = new(StandardCursorType.TopLeftCorner),
        [WindowEdge.NorthEast] = new(StandardCursorType.TopRightCorner),
        [WindowEdge.SouthWest] = new(StandardCursorType.BottomLeftCorner),
        [WindowEdge.SouthEast] = new(StandardCursorType.BottomRightCorner)
    });

    internal static WindowEdge? ResizeEdge(Point point, Size bounds)
    {
        const double edgeWidth = 6;
        if (point.X < 0 || point.Y < 0 || point.X >= bounds.Width || point.Y >= bounds.Height) return null;
        var left = point.X < edgeWidth;
        var right = point.X >= bounds.Width - edgeWidth;
        var top = point.Y < edgeWidth;
        var bottom = point.Y >= bounds.Height - edgeWidth;
        if (!left && !right && !top && !bottom) return null;
        return top ? (left ? WindowEdge.NorthWest : right ? WindowEdge.NorthEast : WindowEdge.North)
            : bottom ? (left ? WindowEdge.SouthWest : right ? WindowEdge.SouthEast : WindowEdge.South)
            : left ? WindowEdge.West : WindowEdge.East;
    }

    private WindowEdge? EdgeAt(PointerEventArgs e) => _window.CanResize && _window.WindowState == WindowState.Normal
        ? ResizeEdge(e.GetPosition(_window), _window.Bounds.Size) : null;

    private void OnEdgePointerMoved(object? sender, PointerEventArgs e) =>
        _window.Cursor = EdgeAt(e) is { } edge ? ResizeCursors.Value[edge] : null;

    private void OnEdgePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed || EdgeAt(e) is not { } edge) return;
        // KWin owns the live pointer grab and dimensions; never animate client width/height.
        _window.BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            ApplyWindowState();
            QueueStateReapply();
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        // A Wayland compositor can deliver the first maximize/fullscreen
        // configure during mapping. Apply once immediately and once after the
        // first layout pass so the frame cannot retain its floating geometry.
        ApplyWindowState();
        QueueStateReapply();
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        // KWin may confirm a state transition while the client is inactive.
        // Refresh on activation to make the client frame match that confirmed
        // native state before it becomes visible to the user.
        ApplyWindowState();
        QueueStateReapply();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _window.WindowState == WindowState.FullScreen)
        {
            ExitFullscreen();
            e.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        if (_window.WindowState == WindowState.FullScreen)
        {
            ExitFullscreen();
            return;
        }

        _stateBeforeFullscreen = SystemAppWindowPresentation.RestoreStateForFullscreen(_window.WindowState);
        _window.WindowState = WindowState.FullScreen;
    }

    internal static void HandleTitleBarPress(Window window, PointerPressedEventArgs e)
    {
        // Never start a compositor move for the second press: Wayland move
        // grabs can outlive the maximize configure and leave floating geometry.
        // Use the raw click count because BeginMoveDrag releases pointer capture,
        // which can prevent the routed DoubleTapped gesture from being raised.
        if (e.ClickCount % 2 == 0)
            ToggleMaximizeRestore(window);
        else
            window.BeginMoveDrag(e);
        e.Handled = true;
    }

    /// <summary>
    /// Starts the standard desktop maximize/restore operation and immediately
    /// applies its frame presentation. Wayland state changes are asynchronous;
    /// waiting for KWin's configure previously left a maximized DaisyOS app
    /// looking like a large floating card for one or more frames.
    /// </summary>
    internal static void ToggleMaximizeRestore(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (Instances.TryGetValue(window, out var chrome))
        {
            chrome.ToggleMaximizeRestoreCore();
            return;
        }

        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void ToggleMaximizeRestoreCore()
    {
        var requestedState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        ApplyWindowState(requestedState);
        _window.WindowState = requestedState;
    }

    private void ExitFullscreen() => _window.WindowState = _stateBeforeFullscreen;

    private void ApplyWindowState() => ApplyWindowState(_window.WindowState);

    private void ApplyWindowState(WindowState state)
    {
        var presentation = SystemAppWindowPresentation.For(
            state,
            _normalRowDefinitions,
            _fullscreenRowDefinitions);
        _titleBar.IsVisible = presentation.TitleBarVisible;
        _contentLayout.RowDefinitions = new RowDefinitions(presentation.RowDefinitions);
        if (state != WindowState.Normal) _window.Cursor = null;
    }

    private void QueueStateReapply()
    {
        if (_isDisposed || _reapplyQueued)
        {
            return;
        }

        _reapplyQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _reapplyQueued = false;
            if (!_isDisposed)
            {
                ApplyWindowState();
            }
        }, DispatcherPriority.Render);
    }
}

/// <summary>
/// Pure visual contract for DaisyOS-owned application windows. Keeping this
/// separate from the controls makes native Wayland state transitions testable
/// and prevents individual apps from drifting into their own fullscreen rules.
/// </summary>
internal readonly record struct SystemAppWindowPresentation(
    bool FillsDisplay,
    bool TitleBarVisible,
    bool ResizeHandlesVisible,
    string RowDefinitions)
{
    public static SystemAppWindowPresentation For(
        WindowState state,
        string normalRowDefinitions,
        string fullscreenRowDefinitions)
    {
        var isFullscreen = state == WindowState.FullScreen;
        var fillsDisplay = isFullscreen || state == WindowState.Maximized;
        return new SystemAppWindowPresentation(
            fillsDisplay,
            !isFullscreen,
            !fillsDisplay,
            isFullscreen ? fullscreenRowDefinitions : normalRowDefinitions);
    }

    public static WindowState RestoreStateForFullscreen(WindowState currentState) =>
        currentState == WindowState.Minimized ? WindowState.Normal : currentState;
}
