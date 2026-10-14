using Avalonia.Controls;

namespace DaisyOS.Shell.Services.Shell;

/// <summary>
/// Deliberately refuses to emulate layer-shell with an xdg_toplevel. That old
/// compatibility behaviour is precisely what made DaisyOS look like a
/// fullscreen application instead of a desktop shell.
/// </summary>
public sealed class UnavailableLayerShellWindowBackend : ILayerShellWindowBackend
{
    public bool IsAvailable => false;

    public Window CreateWindow(LayerShellSurfaceOptions options, Control content) =>
        throw new InvalidOperationException(
            "The DaisyOS layer-shell backend is unavailable. Refusing to create a fullscreen xdg_toplevel fallback.");

    public T CreateWindow<T>(LayerShellSurfaceOptions options, Func<T> factory) where T : Window =>
        throw new InvalidOperationException(
            "The DaisyOS layer-shell backend is unavailable. Refusing to create a fullscreen xdg_toplevel fallback.");
}
