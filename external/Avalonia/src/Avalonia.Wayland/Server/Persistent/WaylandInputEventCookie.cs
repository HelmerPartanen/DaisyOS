using System;
using Avalonia.Wayland.Server.Transient;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland.Server.Persistent;

/// <summary>
/// Platform-specific cookie passed through Avalonia's input pipeline.
/// Carries the Wayland seat and serial from the triggering input event,
/// consumed by BeginMoveDrag/BeginResizeDrag to issue xdg_toplevel.move/resize.
/// Single-use: TryConsume returns false on subsequent calls or if the display changed.
/// Both TryConsume and Invalidate are called on the wayland thread.
/// <para>
/// Also captures the <see cref="WaylandGlobals"/> and <see cref="WaylandWorker"/> from the
/// moment the input event was triggered, providing <see cref="PostOob"/> for reconnect-safe
/// access to transient state (e.g. initiating DnD from a pointer press).
/// </para>
/// </summary>
class WaylandInputEventCookie
{
    private WlSeat? _seat;
    private readonly WlSurface? _surface;
    private bool _activationRequested;
    private readonly uint _serial;
    private readonly WlDisplay _display;
    private readonly WaylandWorker _worker;
    private readonly WaylandGlobals _globals;

    public WaylandInputEventCookie(WlSeat seat, uint serial, WlDisplay display,
        WaylandWorker worker, WaylandGlobals globals, WlSurface? surface = null)
    {
        _surface = surface;
        _seat = seat;
        _serial = serial;
        _display = display;
        _worker = worker;
        _globals = globals;
    }

    /// <summary>
    /// Attempts to consume the cookie for a surface that uses the given display.
    /// Returns false if already consumed or if the display doesn't match (reconnection).
    /// Must be called on the wayland thread.
    /// </summary>
    internal void RequestActivation(string appId, Avalonia.Wayland.WaylandActivation.TokenListener listener)
    {
        PostOob(globals =>
        {
            if (_activationRequested || _seat is null || globals.Activation is null)
            { listener.Unavailable(); return; }
            _activationRequested = true;
            var token = globals.Activation.GetActivationToken(listener);
            listener.Attach(token);
            token.SetSerial(_serial, _seat);
            if (_surface != null) token.SetSurface(_surface);
            token.SetAppId(appId);
            token.Commit();
        });
    }

    public bool TryConsume(WlDisplay currentDisplay, out WlSeat seat, out uint serial)
    {
        var s = _seat;
        _seat = null;
        if (s != null && ReferenceEquals(_display, currentDisplay))
        {
            seat = s;
            serial = _serial;
            return true;
        }

        seat = default!;
        serial = 0;
        return false;
    }

    /// <summary>
    /// Posts a callback to the wayland thread that receives the <see cref="WaylandGlobals"/>
    /// captured at input-event time. If a compositor reconnect occurred since then
    /// (i.e. the current globals differ from the captured ones), the callback is not invoked.
    /// </summary>
    public void PostOob(Action<WaylandGlobals> cb)
    {
        _worker.PostOob(() =>
        {
            if (ReferenceEquals(_worker.Globals, _globals))
                cb(_globals);
        });
    }
}
