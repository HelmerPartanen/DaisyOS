using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Wayland.Server.Transient.Rendering;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland.Server.Transient.Clipboard;

/// <summary>
/// A small compositor-owned preview for a native Wayland drag. The preview is
/// intentionally independent of a control tree: while a drag is active the
/// compositor owns pointer routing, so an application overlay cannot reliably
/// follow the pointer across clients.
/// </summary>
sealed class WaylandDragIcon : IWaylandFramebufferSurface, IDisposable
{
    private const int Width = 52;
    private const int Height = 44;
    private readonly List<IDisposable> _activeRenderTargets = [];
    private bool _disposed;
    private bool _positioned;
    private readonly bool _hasPreview;

    public WaylandDragIcon(WaylandGlobals globals, int itemCount, Avalonia.Media.Imaging.Bitmap? preview = null)
    {
        _hasPreview = preview is not null;
        Globals = globals;
        WlSurface = globals.WlCompositor.CreateSurface(null);
        if (preview is not null)
        {
            var framebuffer = new WaylandFramebuffer(this);
            using var target = framebuffer.CreateFramebufferRenderTarget();
            using var locked = target.Lock(new IRenderTarget.RenderTargetSceneInfo(preview.PixelSize,
                preview.Dpi.X / 96, CompositionTransparencyLevel.Transparent), out _);
            preview.CopyPixels(locked);
        }
        else
            Render(itemCount);
    }

    public WaylandGlobals? Globals { get; }
    public WlSurface? WlSurface { get; private set; }
    public PlatformRenderTargetState State => !_disposed && WlSurface is not null
        ? PlatformRenderTargetState.Ready
        : PlatformRenderTargetState.Disposed;

    public bool EnforceBufferCreationRoundtrip => true;

    public void RegisterRenderTarget(IDisposable renderTarget) => _activeRenderTargets.Add(renderTarget);
    public void UnregisterRenderTarget(IDisposable renderTarget) => _activeRenderTargets.Remove(renderTarget);
    public void OnBeforeNewBufferAttached(IRenderTarget.RenderTargetSceneInfo sceneInfo)
    {
        WlSurface!.SetBufferScale(Math.Max(1, (int)Math.Ceiling(sceneInfo.Scaling)));
        if (_hasPreview && !_positioned && WlSurface.Version >= 5)
        {
            WlSurface.Offset(0, -60);
            _positioned = true;
        }
    }

    private void Render(int itemCount)
    {
        var pixels = new byte[Width * Height * 4];

        // Shadow, card, and a compact document glyph. This remains legible over
        // both light and dark content without borrowing any app-specific theme.
        FillRoundedRectangle(pixels, 5, 7, 40, 30, 8, 0, 0, 0, 74);
        if (itemCount > 1)
            FillRoundedRectangle(pixels, 10, 3, 36, 28, 7, 195, 203, 214, 214);
        FillRoundedRectangle(pixels, 4, 4, 40, 30, 7, 246, 248, 251, 238);
        FillRoundedRectangle(pixels, 13, 10, 18, 18, 4, 42, 124, 226, 255);
        FillRoundedRectangle(pixels, 18, 14, 8, 2, 1, 255, 255, 255, 255);
        FillRoundedRectangle(pixels, 18, 19, 8, 2, 1, 255, 255, 255, 255);
        FillRoundedRectangle(pixels, 18, 24, 5, 2, 1, 255, 255, 255, 255);

        var framebuffer = new WaylandFramebuffer(this);
        using var renderTarget = framebuffer.CreateFramebufferRenderTarget();
        using var locked = renderTarget.Lock(
            new IRenderTarget.RenderTargetSceneInfo(new PixelSize(Width, Height), 1, CompositionTransparencyLevel.Transparent),
            out _);
        Marshal.Copy(pixels, 0, locked.Address, pixels.Length);
    }

    private static void FillRoundedRectangle(byte[] pixels, int x, int y, int width, int height, int radius,
        byte red, byte green, byte blue, byte alpha)
    {
        var radiusSquared = radius * radius;
        for (var py = Math.Max(0, y); py < Math.Min(Height, y + height); py++)
        {
            for (var px = Math.Max(0, x); px < Math.Min(Width, x + width); px++)
            {
                var cornerX = px < x + radius ? x + radius - px : px >= x + width - radius ? px - (x + width - radius - 1) : 0;
                var cornerY = py < y + radius ? y + radius - py : py >= y + height - radius ? py - (y + height - radius - 1) : 0;
                if (cornerX > 0 && cornerY > 0 && cornerX * cornerX + cornerY * cornerY > radiusSquared)
                    continue;

                var index = (py * Width + px) * 4;
                pixels[index] = (byte)(blue * alpha / 255);
                pixels[index + 1] = (byte)(green * alpha / 255);
                pixels[index + 2] = (byte)(red * alpha / 255);
                pixels[index + 3] = alpha;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var renderTarget in _activeRenderTargets.ToArray())
            renderTarget.Dispose();
        _activeRenderTargets.Clear();
        WlSurface?.Destroy();
        WlSurface = null;
    }
}
