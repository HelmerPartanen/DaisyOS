using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Wayland.Server.LayerShell;

namespace Avalonia.Wayland;

/// <summary>
/// Creates an Avalonia <see cref="Window"/> with a zwlr_layer_shell_v1 role.
///
/// The callback is deliberately the only creation path: the Wayland backend
/// must choose the surface role while Avalonia constructs the window's native
/// implementation.  Normal windows created outside this scope remain ordinary
/// xdg_toplevels.
/// </summary>
public static class LayerShellWindow
{
    // Avalonia creates native top-level implementations synchronously on its
    // UI thread. AsyncLocal is the wrong scope for this hand-off: execution
    // contexts captured by unrelated UI work can retain a stale (or cleared)
    // value and turn a later shell surface into an invalid layer window.
    private static readonly ThreadLocal<LayerShellOptions?> PendingOptions = new();

    public static T Create<T>(LayerShellOptions options, Func<T> createWindow)
        where T : Window
    {
        ArgumentNullException.ThrowIfNull(createWindow);
        options.Validate();

        if (PendingOptions.Value is not null)
            throw new InvalidOperationException("Layer-shell window creation cannot be nested.");

        try
        {
            WriteDiagnostics($"offering '{options.Namespace}' to the next native window");
            PendingOptions.Value = options;
            return createWindow();
        }
        finally
        {
            PendingOptions.Value = null;
        }
    }

    internal static bool TryTakePendingOptions(out LayerShellOptions options)
    {
        if (PendingOptions.Value is { } pending)
        {
            PendingOptions.Value = null;
            options = pending;
            WriteDiagnostics($"assigned '{options.Namespace}' to a layer window");
            return true;
        }

        options = default!;
        return false;
    }

    /// <summary>
    /// Enables or disables pointer hit testing for a layer-shell surface
    /// without destroying its Wayland role. This is the safe visibility
    /// primitive for transient shell UI: hiding and recreating a layer role
    /// on each click makes compositors reconfigure it and causes flicker.
    /// </summary>
    public static void SetInputPassthrough(Window window, bool inputPassthrough)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.PlatformImpl is LayerShellWindowImpl layerWindow)
            layerWindow.SetInputPassthrough(inputPassthrough);
    }

    /// <summary>
    /// Changes whether a layer-shell surface receives keyboard focus without
    /// destroying or remapping its role. Transient shell UI uses Exclusive
    /// while visible so Escape is routed to the open surface, and None while
    /// hidden so it cannot retain focus from the application below.
    /// </summary>
    public static void SetKeyboardInteractivity(
        Window window,
        ZwlrLayerSurfaceV1.KeyboardInteractivity interactivity)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.PlatformImpl is LayerShellWindowImpl layerWindow)
            layerWindow.SetKeyboardInteractivity(interactivity);
    }

    /// <summary>
    /// Updates the output space reserved by a persistent layer surface.
    /// DaisyOS uses this when a fullscreen client becomes active: passive
    /// bottom chrome is hidden and its exclusive zone is released so KWin can
    /// return the entire output to the client without a black reserved band.
    /// </summary>
    public static void SetExclusiveZone(Window window, int exclusiveZone)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (exclusiveZone < -1)
            throw new ArgumentOutOfRangeException(nameof(exclusiveZone));
        if (window.PlatformImpl is LayerShellWindowImpl layerWindow)
            layerWindow.SetExclusiveZone(exclusiveZone);
    }

    /// <summary>
    /// Publishes surface-local blur regions through ext_background_effect_v1.
    /// An empty collection removes compositor blur from the surface.
    /// </summary>
    public static void SetBlurRegions(Window window, IReadOnlyList<WaylandBlurRect> regions)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(regions);
        if (window.PlatformImpl is LayerShellWindowImpl layerWindow)
        {
            var data = new int[regions.Count * 4];
            for (var index = 0; index < regions.Count; index++)
            {
                var region = regions[index];
                var offset = index * 4;
                data[offset] = region.X;
                data[offset + 1] = region.Y;
                data[offset + 2] = region.Width;
                data[offset + 3] = region.Height;
            }
            layerWindow.SetBlurRegions(data);
        }
    }

    /// <summary>Attaches a renderer-owned native child to a mapped layer window.
    /// The factory runs on the Wayland worker; the returned resource is also
    /// disposed before the parent disconnects. Disposal must be idempotent.</summary>
    public static System.Threading.Tasks.Task<IDisposable> AttachNativeChildAsync(
        Window window, Func<nint, nint, IDisposable> factory)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(factory);
        return window.PlatformImpl is LayerShellWindowImpl impl
            ? impl.AttachNativeChild(factory)
            : System.Threading.Tasks.Task.FromException<IDisposable>(new NotSupportedException("A native layer host is required."));
    }

    private static void WriteDiagnostics(string message)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("DAISYOS_WAYLAND_DIAGNOSTICS"), "1", StringComparison.Ordinal))
            Console.Error.WriteLine($"[daisy-layer] {message}");
    }
}
