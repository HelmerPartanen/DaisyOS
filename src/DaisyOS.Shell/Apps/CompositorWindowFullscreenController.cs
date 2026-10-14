using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DaisyOS.Shell.Apps;

/// <summary>
/// Keeps the explicit F11 fullscreen contract for DaisyOS applications whose
/// normal title bar and frame are supplied by KWin rather than the client.
/// </summary>
internal sealed class CompositorWindowFullscreenController : IDisposable
{
    private readonly Window _window;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;

    public CompositorWindowFullscreenController(Window window)
    {
        _window = window;
        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public void Dispose() => _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _window.WindowState == WindowState.FullScreen)
        {
            _window.WindowState = _stateBeforeFullscreen;
            e.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        if (_window.WindowState == WindowState.FullScreen)
        {
            _window.WindowState = _stateBeforeFullscreen;
            return;
        }

        _stateBeforeFullscreen = _window.WindowState == WindowState.Minimized
            ? WindowState.Normal
            : _window.WindowState;
        _window.WindowState = WindowState.FullScreen;
    }
}
