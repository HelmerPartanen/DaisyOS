using System;

namespace Avalonia.Wayland.Server.LayerShell;

/// <summary>Creation-time role and geometry for a layer-shell surface.</summary>
public sealed record LayerShellOptions(
    ZwlrLayerShellV1.Layer Layer,
    ZwlrLayerSurfaceV1.Anchor Anchor,
    int ExclusiveZone,
    ZwlrLayerSurfaceV1.KeyboardInteractivity KeyboardInteractivity,
    string Namespace,
    string? OutputName = null,
    uint Width = 0,
    uint Height = 0,
    bool InputPassthrough = false)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Namespace))
            throw new ArgumentException("Layer-shell namespace is required.", nameof(Namespace));
        // zwlr_layer_surface_v1 uses -1 as a defined sentinel: the surface
        // must extend to its anchored output edges beneath other exclusive
        // zones. Wallpaper and lock-screen surfaces rely on this behavior.
        if (ExclusiveZone < -1)
            throw new ArgumentOutOfRangeException(nameof(ExclusiveZone));
    }
}
