using Avalonia.Controls;

namespace DaisyOS.Shell.Services.Shell;

/// <summary>
/// Contract implemented by the version-pinned DaisyOS Avalonia Wayland fork.
/// Keeping this boundary small means shell views do not take a dependency on
/// KWin or private Avalonia implementation types.
/// </summary>
public interface ILayerShellWindowBackend
{
    bool IsAvailable { get; }

    Window CreateWindow(LayerShellSurfaceOptions options, Control content);

    T CreateWindow<T>(LayerShellSurfaceOptions options, Func<T> factory) where T : Window;
}
