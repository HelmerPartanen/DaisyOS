using System;
using NWayland;
using NWayland.Interop;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland.Server.LayerShell;

/// <summary>
/// Bindings for wlr-layer-shell-unstable-v1, version 5. Kept in the DaisyOS
/// Avalonia fork because stock Avalonia deliberately exposes xdg toplevels
/// only. The protocol is stable in practice across KWin, wlroots compositors,
/// and COSMIC's compatibility layer.
/// </summary>
public sealed class ZwlrLayerShellV1 : WlProxy, IWlProxyTypeDescriptorProvider
{
    public enum Layer : uint
    {
        Background = 0,
        Bottom = 1,
        Top = 2,
        Overlay = 3
    }

    public static WlProxyTypeDescriptor ProxyType { get; } = new WlProxyTypeDescriptor(
        WlInterfaceDescription.Create("zwlr_layer_shell_v1", 5)
            .AddMethod(WlMessageDescription.Create("get_layer_surface")
                .Add(WlMessageArgumentDescription.NewId(ZwlrLayerSurfaceV1.ProxyType))
                .Add(WlMessageArgumentDescription.Object(WlSurface.ProxyType))
                .Add(WlMessageArgumentDescription.Object(WlOutput.ProxyType))
                .Add(WlMessageArgumentDescription.UInt32)
                .Add(WlMessageArgumentDescription.String)
                .Build())
            .AddMethod(WlMessageDescription.Create("destroy").IsDestructor().Build())
            .AddEvent(WlMessageDescription.Create("closed").Build())
            .Build(), typeof(ZwlrLayerShellV1), context => new ZwlrLayerShellV1(context), false);

    private ZwlrLayerShellV1(WlProxyCreationContext context) : base(context) { }

    public static ZwlrLayerShellV1 Bind(WlRegistry registry, uint name, uint version) =>
        registry.Bind<ZwlrLayerShellV1>(name, version);

    public ZwlrLayerSurfaceV1 GetLayerSurface(
        WlSurface surface,
        WlOutput? output,
        Layer layer,
        string surfaceNamespace,
        ZwlrLayerSurfaceV1.Listener listener,
        IWlTargetQueue? queue = null)
    {
        using var call = WaylandCallBuilder.Create(this, 0u);
        call.ArgNewId();
        call.Arg(surface);
        call.Arg(output);
        call.Arg((uint)layer);
        call.Arg(surfaceNamespace);
        return call.InvokeNewId<ZwlrLayerSurfaceV1>(listener, queue);
    }

    public void Destroy()
    {
        using var call = WaylandCallBuilder.Create(this, 1u);
        call.Invoke();
    }
}

public sealed class ZwlrLayerSurfaceV1 : WlProxy, IWlProxyTypeDescriptorProvider
{
    [Flags]
    public enum Anchor : uint
    {
        None = 0,
        Top = 1,
        Bottom = 2,
        Left = 4,
        Right = 8
    }

    public enum KeyboardInteractivity : uint
    {
        None = 0,
        Exclusive = 1,
        OnDemand = 2
    }

    public abstract class Listener : IWlEventsListener
    {
        protected virtual void Configure(ZwlrLayerSurfaceV1 sender, uint serial, uint width, uint height) { }
        protected virtual void Closed(ZwlrLayerSurfaceV1 sender) { }

        void IWlEventsListener.DispatchEvent(WlEventArgs arguments)
        {
            switch (arguments.Opcode)
            {
                case 0:
                    Configure((ZwlrLayerSurfaceV1)arguments.Sender, arguments.GetUInt32(0), arguments.GetUInt32(1), arguments.GetUInt32(2));
                    break;
                case 1:
                    Closed((ZwlrLayerSurfaceV1)arguments.Sender);
                    break;
            }
        }
    }

    public static WlProxyTypeDescriptor ProxyType { get; } = new WlProxyTypeDescriptor(
        WlInterfaceDescription.Create("zwlr_layer_surface_v1", 5)
            .AddMethod(WlMessageDescription.Create("set_size").Add(WlMessageArgumentDescription.UInt32).Add(WlMessageArgumentDescription.UInt32).Build())
            .AddMethod(WlMessageDescription.Create("set_anchor").Add(WlMessageArgumentDescription.UInt32).Build())
            .AddMethod(WlMessageDescription.Create("set_exclusive_zone").Add(WlMessageArgumentDescription.Int32).Build())
            .AddMethod(WlMessageDescription.Create("set_margin").Add(WlMessageArgumentDescription.Int32).Add(WlMessageArgumentDescription.Int32).Add(WlMessageArgumentDescription.Int32).Add(WlMessageArgumentDescription.Int32).Build())
            .AddMethod(WlMessageDescription.Create("set_keyboard_interactivity").Add(WlMessageArgumentDescription.UInt32).Build())
            // Layer-shell deliberately accepts any xdg_popup implementation here.
            // The protocol descriptor has no concrete proxy type for that argument.
            .AddMethod(WlMessageDescription.Create("get_popup").Add(WlMessageArgumentDescription.Object(null!)).Build())
            .AddMethod(WlMessageDescription.Create("ack_configure").Add(WlMessageArgumentDescription.UInt32).Build())
            .AddMethod(WlMessageDescription.Create("destroy").IsDestructor().Build())
            .AddMethod(WlMessageDescription.Create("set_layer").SinceVersion(4).Add(WlMessageArgumentDescription.UInt32).Build())
            .AddMethod(WlMessageDescription.Create("set_exclusive_edge").SinceVersion(5).Add(WlMessageArgumentDescription.UInt32).Build())
            .AddEvent(WlMessageDescription.Create("configure").Add(WlMessageArgumentDescription.UInt32).Add(WlMessageArgumentDescription.UInt32).Add(WlMessageArgumentDescription.UInt32).Build())
            .AddEvent(WlMessageDescription.Create("closed").Build())
            .Build(), typeof(ZwlrLayerSurfaceV1), context => new ZwlrLayerSurfaceV1(context), false);

    private ZwlrLayerSurfaceV1(WlProxyCreationContext context) : base(context) { }

    public void SetSize(uint width, uint height)
    {
        using var call = WaylandCallBuilder.Create(this, 0u);
        call.Arg(width); call.Arg(height); call.Invoke();
    }

    public void SetAnchor(Anchor anchor)
    {
        using var call = WaylandCallBuilder.Create(this, 1u);
        call.Arg((uint)anchor); call.Invoke();
    }

    public void SetExclusiveZone(int zone)
    {
        using var call = WaylandCallBuilder.Create(this, 2u);
        call.Arg(zone); call.Invoke();
    }

    public void SetMargin(int top, int right, int bottom, int left)
    {
        using var call = WaylandCallBuilder.Create(this, 3u);
        call.Arg(top); call.Arg(right); call.Arg(bottom); call.Arg(left); call.Invoke();
    }

    public void SetKeyboardInteractivity(KeyboardInteractivity interactivity)
    {
        using var call = WaylandCallBuilder.Create(this, 4u);
        call.Arg((uint)interactivity); call.Invoke();
    }

    public void AckConfigure(uint serial)
    {
        using var call = WaylandCallBuilder.Create(this, 6u);
        call.Arg(serial); call.Invoke();
    }

    public void Destroy()
    {
        using var call = WaylandCallBuilder.Create(this, 7u);
        call.Invoke();
    }

    public void SetLayer(ZwlrLayerShellV1.Layer layer)
    {
        using var call = WaylandCallBuilder.Create(this, 8u);
        call.Arg((uint)layer); call.Invoke();
    }

    public void SetExclusiveEdge(Anchor edge)
    {
        using var call = WaylandCallBuilder.Create(this, 9u);
        call.Arg((uint)edge); call.Invoke();
    }
}
