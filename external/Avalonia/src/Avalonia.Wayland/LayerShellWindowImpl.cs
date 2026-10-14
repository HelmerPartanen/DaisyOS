using System;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Avalonia.Wayland.Server;
using Avalonia.Wayland.Server.LayerShell;
using Avalonia.Wayland.Server.Persistent;
using NWayland.Protocols.XdgShell;

namespace Avalonia.Wayland;

/// <summary>Window implementation for a configured zwlr_layer_shell_v1 surface.</summary>
internal sealed class LayerShellWindowImpl : WindowImpl
{
    private readonly LayerShellOptions _options;
    private bool _inputPassthrough;
    private ZwlrLayerSurfaceV1.KeyboardInteractivity _keyboardInteractivity;
    private WaylandSurfaceCreateResult<WLayerSurfaceProxy>? _layerHandle;
    private WLayerSurfaceProxy? _layerSurfaceProxy;
    private int[] _blurRegions = [];

    public LayerShellWindowImpl(WaylandWorkerClient client, LayerShellOptions options)
        : base(client, createInitialSink: false)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _inputPassthrough = options.InputPassthrough;
        _keyboardInteractivity = options.KeyboardInteractivity;
        CurrentSink = new LayerSink(this, secondShow: false);
    }

    public override IPlatformRenderSurface[] Surfaces => _layerHandle?.GetRenderSurfaces() ?? [];

    // Layer-shell has no xdg_surface, xdg_popup parent or title-bar chrome.
    internal override WXdgShellSurfaceProxy? SurfaceProxy => null;

    public override IPopupImpl? CreatePopup() => null;

    public override void Show(bool activate, bool isDialog)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(LayerShellWindowImpl));

        if (CurrentSink is null)
            CurrentSink = new LayerSink(this, secondShow: true);

        Client.AnyThreadWakeupRenderLoop();
    }

    internal System.Threading.Tasks.Task<IDisposable> AttachNativeChild(Func<nint, nint, IDisposable> factory) =>
        _layerHandle?.AttachNativeChild?.Invoke(factory)
        ?? System.Threading.Tasks.Task.FromException<IDisposable>(new InvalidOperationException("Layer surface is not mapped."));

    internal void SetInputPassthrough(bool inputPassthrough)
    {
        _inputPassthrough = inputPassthrough;
        _layerSurfaceProxy?.SetInputPassthrough(inputPassthrough);
    }

    internal void SetKeyboardInteractivity(ZwlrLayerSurfaceV1.KeyboardInteractivity interactivity)
    {
        _keyboardInteractivity = interactivity;
        _layerSurfaceProxy?.SetKeyboardInteractivity(interactivity);
    }

    internal void SetExclusiveZone(int exclusiveZone)
    {
        if (exclusiveZone < -1)
            throw new ArgumentOutOfRangeException(nameof(exclusiveZone));

        _layerSurfaceProxy?.SetExclusiveZone(exclusiveZone);
    }

    internal void SetBlurRegions(int[] regions)
    {
        _blurRegions = regions;
        _layerSurfaceProxy?.SetBlurRegions(regions);
    }

    private void ApplyConfigure(XdgConfigureBatch batch, bool secondShow)
    {
        if (batch.Size is not { Width: > 0, Height: > 0 })
            return;

        var size = new Size(batch.Size.Width, batch.Size.Height);
        if (size == ClientSize)
            return;

        ClientSize = size;
        Resized?.Invoke(size, secondShow ? WindowResizeReason.Unspecified : WindowResizeReason.Layout);
    }

    private sealed class LayerSink : WindowBaseImpl.Sink, ILayerSurfaceEventSink
    {
        private new LayerShellWindowImpl Parent => (LayerShellWindowImpl)base.Parent;
        private WLayerSurfaceProxy? _surfaceProxy;
        private readonly bool _secondShow;
        private bool _configured;

        public LayerSink(LayerShellWindowImpl parent, bool secondShow) : base(parent)
        {
            _secondShow = secondShow;
            var handle = parent.Client.CreateLayerShellHandle(
                parent._options with
                {
                    InputPassthrough = parent._inputPassthrough,
                    KeyboardInteractivity = parent._keyboardInteractivity
                },
                new WLayerSurfaceEventSinkProxy(this, WaylandMarshallers.UIThread));
            _surfaceProxy = handle.Proxy;
            parent._layerHandle = handle;
            parent._layerSurfaceProxy = _surfaceProxy;
            _surfaceProxy.SetCursor(parent.CurrentCursor?.Cursor);
            _surfaceProxy.SetBlurRegions(parent._blurRegions);

            // Unlike an xdg toplevel, a layer surface must not synchronously
            // wait for its first configure while Avalonia is constructing the
            // window. The configure callback is marshalled onto Avalonia's UI
            // dispatcher; blocking that dispatcher here deadlocks startup and
            // leaves a nested compositor with an empty, black scene. Until
            // KWin configures the surface WLayerSurface deliberately reports
            // NotReady, then this callback enables the first render.
        }

        protected override void DisconnectFromSurface()
        {
            var proxy = _surfaceProxy;
            _surfaceProxy = null;
            Parent._layerHandle = null;
            Parent._layerSurfaceProxy = null;
            proxy?.Disconnect();
        }

        public void OnConfigure(XdgConfigureBatch batch)
        {
            if (IsDisposed)
                return;

            Parent.ApplyConfigure(batch, secondShow: _configured || _secondShow);
            _configured = true;
            _surfaceProxy?.SetPendingAckSerial(batch.Serial);
        }

        public void OnClose()
        {
            if (IsDisposed)
                return;

            if (Parent.Closing?.Invoke(WindowCloseReason.WindowClosing) != true)
                Dispose();
        }
    }
}
