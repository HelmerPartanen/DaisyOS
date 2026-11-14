using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Avalonia.Wayland.Server.Interop;
using Avalonia.Wayland.Server.LayerShell;
using Avalonia.Wayland.Server.Transient;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland.Server.Persistent;

/// <summary>Worker-side zwlr_layer_surface_v1 backed by Avalonia's normal render path.</summary>
internal sealed class WLayerSurface : WSurface, ILayerSurface
{
    private LayerShellOptions _options;
    private readonly List<IDisposable> _nativeChildren = new();

    // Executed on the Wayland worker. Children must stop their render threads
    // before the borrowed display/parent pointers become invalid.
    public IDisposable AttachNativeChild(Func<nint, nint, IDisposable> factory)
    {
        if (CurrentDisplay is null || WlSurface is null)
            throw new InvalidOperationException("The wallpaper host is not connected.");
        _nativeChildren.RemoveAll(child => child is NativeChildLease { IsDisposed: true });
        var lease = new NativeChildLease(factory(CurrentDisplay.Handle, WlSurface.Handle));
        _nativeChildren.Add(lease);
        return lease;
    }

    private sealed class NativeChildLease(IDisposable child) : IDisposable
    {
        private IDisposable? _child = child;
        public bool IsDisposed => _child is null;
        public void Dispose() => System.Threading.Interlocked.Exchange(ref _child, null)?.Dispose();
    }

    private readonly WLayerSurfaceEventSinkProxy _eventSink;
    private ZwlrLayerSurfaceV1? _layerSurface;
    private WaylandConnection? _pendingConnection;
    private WaylandGlobals? _pendingGlobals;
    private WlOutput? _selectedOutput;
    private WlOutput? _closedOutput;
    private uint? _pendingAckSerial;
    private uint? _latestConfigureSerial;
    private bool _configured;
    private bool _inputPassthrough;
    private int[]? _inputRectangles;
    private int _exclusiveZone;
    private ZwlrLayerSurfaceV1.KeyboardInteractivity _keyboardInteractivity;
    private readonly TaskCompletionSource<XdgConfigureBatch> _initialConfigure = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WLayerSurface(WaylandWorker worker, LayerShellOptions options, WLayerSurfaceEventSinkProxy eventSink)
        : base(worker, registerImmediately: false)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _eventSink = eventSink;
        _inputPassthrough = options.InputPassthrough;
        _exclusiveZone = options.ExclusiveZone;
        _keyboardInteractivity = options.KeyboardInteractivity;
        RegisterWithWorker();
    }

    public Task<XdgConfigureBatch> BasicInitCompleted => _initialConfigure.Task;

    internal override WSurfaceEventSinkProxy EventSink => _eventSink;

    protected override void OnScaleChanged(double scale) => _eventSink.OnScaleChanged(scale);

    public override void OnConnected(WaylandConnection connection, WaylandGlobals globals)
    {
        _pendingConnection = connection;
        _pendingGlobals = globals;
        globals.Outputs.Changed += TryConnectOutput;
        TryConnectOutput();
    }

    private void TryConnectOutput()
    {
        if (_layerSurface is not null || _pendingConnection is not { } connection || _pendingGlobals is not { } globals)
            return;
        var options = _options;
        var output = globals.Outputs.Outputs.FirstOrDefault(candidate =>
            !ReferenceEquals(candidate.WlOutput, _closedOutput) &&
            (string.IsNullOrWhiteSpace(options.OutputName) ||
                string.Equals(candidate.XdgName ?? candidate.OutputName, options.OutputName, StringComparison.Ordinal)))?.WlOutput;
        // KWin can withdraw every wl_output while the machine sleeps. Keep the
        // logical window alive until the requested output is announced again.
        if (output is null)
            return;
        if (globals.LayerShell is null)
            throw new AvaloniaWaylandException("zwlr_layer_shell_v1 is required for a DaisyOS shell surface.");
        _selectedOutput = output;
        _closedOutput = null;
        base.OnConnected(connection, globals);
        LayerShellDiagnostics.Write($"connecting '{options.Namespace}' to restored output");
        _layerSurface = globals.LayerShell.GetLayerSurface(
            WlSurface!, output, options.Layer, options.Namespace,
            new LayerListener(this), connection.Queue);
        _layerSurface.SetSize(options.Width, options.Height);
        _layerSurface.SetAnchor(options.Anchor);
        _layerSurface.SetExclusiveZone(_exclusiveZone);
        _layerSurface.SetKeyboardInteractivity(_keyboardInteractivity);
        ApplyInputRegion();
        WlSurface!.Commit();
    }

    public void SetPendingAckSerial(uint serial)
    {
        if (_layerSurface is null || _latestConfigureSerial != serial) return;
        LayerShellDiagnostics.Write($"'{_options.Namespace}' acknowledged configure {serial}");
        _configured = true;
        _pendingAckSerial = serial;

        // Configure is delivered worker → UI → worker. The first wake occurs
        // while this surface is still NotReady; wake again after the UI has
        // accepted the serial so the initial layer buffer is actually drawn.
        Worker.WakeupRenderLoop();
    }

    public void SetInputPassthrough(bool inputPassthrough)
    {
        _inputPassthrough = inputPassthrough;
        if (WlSurface is null)
            return;

        ApplyInputRegion();
        WlSurface.Commit();
    }

    public void SetKeyboardInteractivity(ZwlrLayerSurfaceV1.KeyboardInteractivity interactivity)
    {
        _keyboardInteractivity = interactivity;
        if (_layerSurface is null || WlSurface is null)
            return;

        _layerSurface.SetKeyboardInteractivity(interactivity);
        WlSurface.Commit();
    }

    public void SetOutputName(string outputName)
    {
        if (_options.OutputName == outputName) return;
        var connection = _pendingConnection;
        var globals = _pendingGlobals;
        _options = _options with { OutputName = outputName };
        // Output is immutable on a layer role. Replace only its native resources,
        // retaining the Avalonia window, content and logical input state.
        OnDisconnected();
        _closedOutput = null;
        if (connection is not null && globals is not null) OnConnected(connection, globals);
        Worker.Compositor.Server.InvalidateAllCompositionTargets();
        Worker.WakeupRenderLoop();
    }

    public void SetGeometry(ZwlrLayerSurfaceV1.Anchor anchor, uint width, uint height)
    {
        _options = _options with { Anchor = anchor, Width = width, Height = height };
        if (_layerSurface is null || WlSurface is null) return;
        // Commit the complete geometry together. An intermediate zero size
        // with only one axis anchored is invalid in the layer-shell protocol.
        _layerSurface.SetAnchor(anchor);
        _layerSurface.SetSize(width, height);
        WlSurface.Commit();
    }

    public void SetExclusiveZone(int exclusiveZone)
    {
        if (exclusiveZone < -1)
            throw new ArgumentOutOfRangeException(nameof(exclusiveZone));

        _exclusiveZone = exclusiveZone;
        if (_layerSurface is null || WlSurface is null)
            return;

        _layerSurface.SetExclusiveZone(exclusiveZone);
        WlSurface.Commit();
    }

    public void SetInputRectangles(int[] rectangles)
    {
        _inputRectangles = rectangles;
        if (WlSurface is null) return;
        ApplyInputRegion(); WlSurface.Commit();
    }

    private void ApplyInputRegion()
    {
        if (WlSurface is null)
            return;

        if (!_inputPassthrough && _inputRectangles is null)
        {
            // A null region restores the protocol default: the full surface
            // accepts input.
            WlSurface.SetInputRegion(null!);
            return;
        }

        // Wayland's null input region means *infinite*, not empty. Supply a
        // real empty wl_region so the compositor hit-tests the layer below
        // while this transient surface is visually hidden.
        var emptyRegion = Globals!.WlCompositor.CreateRegion(new EmptyRegionListener(), Connection!.Queue);
        if (!_inputPassthrough && _inputRectangles is { } rectangles)
            for (var offset = 0; offset + 3 < rectangles.Length; offset += 4)
                if (rectangles[offset + 2] > 0 && rectangles[offset + 3] > 0)
                    emptyRegion.Add(rectangles[offset], rectangles[offset + 1], rectangles[offset + 2], rectangles[offset + 3]);
        WlSurface.SetInputRegion(emptyRegion);
        emptyRegion.Destroy();
    }

    public override PlatformRenderTargetState State =>
        _configured ? base.State : PlatformRenderTargetState.NotReadyWillWakeupRenderLoop;

    public override void OnBeforeNewBufferAttached(IRenderTarget.RenderTargetSceneInfo sceneInfo)
    {
        if (_pendingAckSerial is { } serial)
        {
            _layerSurface!.AckConfigure(serial);
            _pendingAckSerial = null;
        }
        base.OnBeforeNewBufferAttached(sceneInfo);
    }

    public override void OnDisconnected()
    {
        if (_pendingGlobals is { } globals) globals.Outputs.Changed -= TryConnectOutput;
        _pendingConnection = null;
        _pendingGlobals = null;
        _selectedOutput = null;
        foreach (var child in _nativeChildren) child.Dispose();
        _nativeChildren.Clear();
        _layerSurface?.Destroy();
        _layerSurface = null;
        _pendingAckSerial = null;
        _latestConfigureSerial = null;
        _configured = false;
        base.OnDisconnected();
    }

    private sealed class LayerListener(WLayerSurface parent) : ZwlrLayerSurfaceV1.Listener
    {
        protected override void Configure(ZwlrLayerSurfaceV1 sender, uint serial, uint width, uint height)
        {
            LayerShellDiagnostics.Write($"'{parent._options.Namespace}' configured {width}x{height} (serial {serial})");
            if (!ReferenceEquals(sender, parent._layerSurface)) return;
            parent._latestConfigureSerial = serial;
            var batch = new XdgConfigureBatch
            {
                Serial = serial,
                Size = new PixelSize((int)width, (int)height)
            };
            parent._eventSink.OnConfigure(batch);
            parent._initialConfigure.TrySetResult(batch);
            parent.Worker.WakeupRenderLoop();
        }

        protected override void Closed(ZwlrLayerSurfaceV1 sender)
        {
            if (!ReferenceEquals(sender, parent._layerSurface)) return;
            // A closed layer role is final, but output removal is not a request
            // to close the Avalonia Window. Recreate its native resources when
            // the output returns, preserving UI and lock-screen state.
            var connection = parent._pendingConnection;
            var globals = parent._pendingGlobals;
            var output = parent._selectedOutput;
            parent.OnDisconnected();
            parent._closedOutput = output;
            if (connection is not null && globals is not null)
                parent.OnConnected(connection, globals);
            parent.Worker.Compositor.Server.InvalidateAllCompositionTargets();
            parent.Worker.WakeupRenderLoop();
        }
    }

    private sealed class EmptyRegionListener : WlRegion.Listener
    {
    }
}

/// <summary>
/// Tiny opt-in startup trace for diagnosing compositor/session integration.
/// It is intentionally disabled outside the development prototype so normal
/// applications do not receive shell-specific stderr noise.
/// </summary>
internal static class LayerShellDiagnostics
{
    private static readonly bool Enabled = string.Equals(
        Environment.GetEnvironmentVariable("DAISYOS_WAYLAND_DIAGNOSTICS"), "1", StringComparison.Ordinal);

    public static void Write(string message)
    {
        if (Enabled)
            Console.Error.WriteLine($"[daisy-layer] {message}");
    }
}
