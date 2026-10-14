using Avalonia.Controls;
using Avalonia.Wayland;
using Avalonia.Wayland.Server.LayerShell;

namespace DaisyOS.Shell.Services.Shell;

/// <summary>
/// Adapter for the pinned DaisyOS Avalonia fork.  It assigns the Wayland role
/// while Avalonia constructs the native window, before any xdg_toplevel role
/// can be created for the same wl_surface.
/// </summary>
public sealed class AvaloniaLayerShellWindowBackend : ILayerShellWindowBackend
{
    public bool IsAvailable => true;

    public Window CreateWindow(LayerShellSurfaceOptions options, Control content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return CreateWindow(options, () => new Window
        {
            Content = content,
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            CanResize = false,
            Background = null
        });
    }

    public T CreateWindow<T>(LayerShellSurfaceOptions options, Func<T> factory) where T : Window
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(factory);
        options.Validate();

        var nativeOptions = new LayerShellOptions(
            ToNativeLayer(options.Role),
            ToNativeAnchor(options.Anchor),
            options.ExclusiveZone,
            ToNativeKeyboardInteractivity(options.KeyboardInteractivity),
            options.Namespace,
            options.OutputName,
            options.Width,
            options.Height,
            options.InputPassthrough);

        return LayerShellWindow.Create(nativeOptions, factory);
    }

    private static ZwlrLayerShellV1.Layer ToNativeLayer(LayerShellSurfaceRole role) => role switch
    {
        LayerShellSurfaceRole.Background => ZwlrLayerShellV1.Layer.Background,
        LayerShellSurfaceRole.DesktopContent => ZwlrLayerShellV1.Layer.Bottom,
        LayerShellSurfaceRole.Panel => ZwlrLayerShellV1.Layer.Top,
        LayerShellSurfaceRole.Overlay => ZwlrLayerShellV1.Layer.Overlay,
        _ => throw new InvalidOperationException("Normal application windows must use xdg_toplevel, not layer-shell.")
    };

    private static ZwlrLayerSurfaceV1.Anchor ToNativeAnchor(LayerShellAnchor anchor)
    {
        var result = ZwlrLayerSurfaceV1.Anchor.None;
        if (anchor.HasFlag(LayerShellAnchor.Top)) result |= ZwlrLayerSurfaceV1.Anchor.Top;
        if (anchor.HasFlag(LayerShellAnchor.Bottom)) result |= ZwlrLayerSurfaceV1.Anchor.Bottom;
        if (anchor.HasFlag(LayerShellAnchor.Left)) result |= ZwlrLayerSurfaceV1.Anchor.Left;
        if (anchor.HasFlag(LayerShellAnchor.Right)) result |= ZwlrLayerSurfaceV1.Anchor.Right;
        return result;
    }

    private static ZwlrLayerSurfaceV1.KeyboardInteractivity ToNativeKeyboardInteractivity(
        LayerShellKeyboardInteractivity interactivity) => interactivity switch
        {
            LayerShellKeyboardInteractivity.None => ZwlrLayerSurfaceV1.KeyboardInteractivity.None,
            LayerShellKeyboardInteractivity.OnDemand => ZwlrLayerSurfaceV1.KeyboardInteractivity.OnDemand,
            LayerShellKeyboardInteractivity.Exclusive => ZwlrLayerSurfaceV1.KeyboardInteractivity.Exclusive,
            _ => throw new ArgumentOutOfRangeException(nameof(interactivity))
        };
}
