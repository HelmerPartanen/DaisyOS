using System.Text.Json;
using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Services.Compositor;

/// <summary>
/// Receives KWin window snapshots from the session bus. The KWin script sends
/// snapshots to a private, owned session-bus service. Owning the destination
/// is required: a passive dbus-monitor cannot receive calls sent by KWin.
/// </summary>
public sealed class KWinWindowBridge : ICompositorWindowService
{
    internal const string BusName = "org.daisyos.Shell.WindowBridge";
    internal const string ObjectPath = "/org/daisyos/Shell/WindowBridge";
    internal const string InterfaceName = "org.daisyos.Shell.WindowBridge";
    internal const string SnapshotMember = "PublishSnapshot";

    private readonly Dictionary<string, CompositorWindowRecord> _windows = new(StringComparer.Ordinal);
    private IReadOnlyCollection<CompositorWindowRecord> _publishedWindows = Array.Empty<CompositorWindowRecord>();
    private readonly KWinCommandDispatcher _dispatcher = new();
    private DBusConnection? _connection;
    private SnapshotHandler? _handler;
    private bool _receivedSnapshot;
    private string _captureSnapshot = "{\"windows\":[]}";

    public IReadOnlyCollection<CompositorWindowRecord> Windows => Volatile.Read(ref _publishedWindows);
    public bool IsAvailable { get; private set; }
    public bool IsDesktopOccluded { get; private set; }
    public bool HasFullscreenWindow { get; private set; }
    public event EventHandler? WindowsChanged;
    /// <summary>Raised by KWin-owned global shortcuts for a DaisyOS capture action.</summary>
    public event EventHandler<string>? ScreenshotRequested;
    /// <summary>Raised by KWin-owned global shortcuts for DaisyOS lock screen action.</summary>
    public event EventHandler? LockRequested;
    /// <summary>Raised by KWin-owned global shortcuts for DaisyOS task overview action.</summary>
    public event EventHandler? OverviewRequested;
    /// <summary>Raised by KWin-owned global media-key shortcuts.</summary>
    public event EventHandler<string>? MediaActionRequested;
    /// <summary>Raised by KWin-owned global shortcuts for common DaisyOS shell actions.</summary>
    public event EventHandler<string>? ShellActionRequested;
    public event EventHandler<LauncherPinchUpdate>? LauncherPinchChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux() || _connection is not null)
        {
            return;
        }

        try
        {
            var connection = new DBusConnection(DBusAddress.Session
                ?? throw new InvalidOperationException("No session D-Bus is available."));
            await connection.ConnectAsync();
            var handler = new SnapshotHandler(
                ApplySerializedSnapshot,
                action => ScreenshotRequested?.Invoke(this, action),
                () => LockRequested?.Invoke(this, EventArgs.Empty),
                () => OverviewRequested?.Invoke(this, EventArgs.Empty),
                action => MediaActionRequested?.Invoke(this, action),
                action => ShellActionRequested?.Invoke(this, action),
                () => Volatile.Read(ref _captureSnapshot),
                payload =>
                {
                    if (LauncherPinchUpdate.TryParse(payload, out var update))
                        LauncherPinchChanged?.Invoke(this, update!);
                });
            connection.AddMethodHandler(handler);
            await connection.RequestNameAsync(BusName, RequestNameOptions.ReplaceExisting);

            _connection = connection;
            KWinCommandDispatcher.SetConnection(connection);
            _handler = handler;
            IsAvailable = true;
            Console.Error.WriteLine("[daisy-window-bridge] Registered the KWin window bridge on the session bus.");

            // The package publishes its initial state while the session starts,
            // often before a restarted shell owns this service. Ask KWin for a
            // fresh reconciliation after the destination is available.
            await _dispatcher.RequestSnapshotAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _handler = null;
            if (_connection is not null)
            {
                _connection.Dispose();
                _connection = null;
            }
            IsAvailable = false;
            Console.Error.WriteLine($"[daisy-window-bridge] Could not start the KWin window bridge: {exception.Message}");
        }
    }

    public async Task ActivateOrMinimizeAsync(string windowId, bool wasActive, CancellationToken cancellationToken = default)
    {
        await _dispatcher.DispatchAsync(windowId, KWinWindowCommand.ActivateOrMinimize, cancellationToken, wasActive: wasActive);
        await _dispatcher.RequestSnapshotAsync(cancellationToken);
    }

    public Task CloseAsync(string windowId, CancellationToken cancellationToken = default) =>
        _dispatcher.DispatchAsync(windowId, KWinWindowCommand.Close, cancellationToken);

    public async Task<bool> ForceQuitAsync(string windowId, CancellationToken cancellationToken = default)
    {
        var window = Windows.FirstOrDefault(window => window.Id == windowId);
        if (window is null
            || !window.IsUnresponsive
            || !window.CanClose
            || window.ProcessId <= 1)
        {
            return false;
        }

        var stopped = await _dispatcher.ForceQuitAsync(window.ProcessId, cancellationToken);
        if (stopped)
        {
            await _dispatcher.RequestSnapshotAsync(cancellationToken);
        }
        return stopped;
    }

    public Task SetMaximizedAsync(string windowId, bool maximized, CancellationToken cancellationToken = default) =>
        _dispatcher.DispatchAsync(windowId,
            maximized ? KWinWindowCommand.Maximize : KWinWindowCommand.Restore,
            cancellationToken);

    public Task MoveToWorkspaceAsync(string windowId, int workspace, CancellationToken cancellationToken = default) =>
        _dispatcher.DispatchAsync(windowId, KWinWindowCommand.MoveToWorkspace, cancellationToken, workspace);

    public Task ToggleOverviewEffectAsync(string effectName = "daisyos-task-overview-v15", CancellationToken cancellationToken = default) =>
        _dispatcher.ToggleOverviewEffectAsync(effectName, cancellationToken);

    internal void ApplySerializedSnapshot(string? payload)
    {
        if (KWinWindowSnapshotParser.TryParse(payload, out var snapshot, out var occluded, out var fullscreen))
        {
            Volatile.Write(ref _captureSnapshot, payload!);
            var coverageChanged = IsDesktopOccluded != occluded || HasFullscreenWindow != fullscreen;
            IsDesktopOccluded = occluded;
            HasFullscreenWindow = fullscreen;
            ApplySnapshot(snapshot, coverageChanged);
            if (!_receivedSnapshot)
            {
                _receivedSnapshot = true;
                Console.Error.WriteLine($"[daisy-window-bridge] Received the initial KWin snapshot ({snapshot.Count} taskbar window(s)).");
            }
        }
    }

    internal void ApplySnapshot(IReadOnlyList<CompositorWindowRecord> snapshot, bool coverageChanged = false)
    {
        var changed = coverageChanged || _windows.Count != snapshot.Count;
        var incoming = snapshot.ToDictionary(window => window.Id, StringComparer.Ordinal);
        foreach (var window in snapshot)
        {
            if (!_windows.TryGetValue(window.Id, out var current) || current != window)
            {
                _windows[window.Id] = window;
                changed = true;
            }
        }
        foreach (var staleId in _windows.Keys.Where(id => !incoming.ContainsKey(id)).ToArray())
        {
            _windows.Remove(staleId);
            changed = true;
        }
        if (changed)
        {
            Volatile.Write(ref _publishedWindows, Array.AsReadOnly(snapshot.ToArray()));
            WindowsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ValueTask DisposeAsync()
    {
        _handler = null;
        if (_connection is not null)
        {
            KWinCommandDispatcher.SetConnection(null);
            _connection.Dispose();
            _connection = null;
        }
        IsAvailable = false;
        _receivedSnapshot = false;
        return ValueTask.CompletedTask;
    }

    private sealed class SnapshotHandler(
        Action<string?> publish,
        Action<string> screenshotRequested,
        Action lockRequested,
        Action overviewRequested,
        Action<string> mediaActionRequested,
        Action<string> shellActionRequested,
        Func<string> captureSnapshot,
        Action<string> launcherPinchChanged) : IPathMethodHandler
    {
        private static readonly ReadOnlyMemory<byte> IntrospectionXml = """
            <interface name="org.daisyos.Shell.WindowBridge">
              <method name="PublishSnapshot">
                <arg direction="in" type="s" name="snapshot"/>
              </method>
              <method name="GetCaptureWindows"><arg direction="out" type="s" name="windows"/></method>
              <method name="GetTaskbarPreview">
                <arg direction="in" type="b" name="visible"/>
                <arg direction="out" type="s" name="preview"/>
              </method>
              <method name="RequestScreenshot">
                <arg direction="in" type="s" name="action"/>
              </method>
              <method name="RequestLock">
              </method>
              <method name="RequestOverview">
              </method>
              <method name="RequestMediaAction">
                <arg direction="in" type="s" name="action"/>
              </method>
              <method name="UpdateLauncherPinch">
                <arg direction="in" type="s" name="update"/>
              </method>
              <method name="RequestShellAction">
                <arg direction="in" type="s" name="action"/>
              </method>
            </interface>
            """u8.ToArray();

        public string Path => ObjectPath;
        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.IsDBusIntrospectRequest)
            {
                context.ReplyIntrospectXml([IntrospectionXml]);
                return default;
            }

            var request = context.Request;
            if (request.InterfaceAsString == InterfaceName && request.MemberAsString == "GetCaptureWindows" && request.SignatureAsString == "")
            {
                using var captureWriter = context.CreateReplyWriter("s");
                captureWriter.WriteString(captureSnapshot());
                context.Reply(captureWriter.CreateMessage());
                return default;
            }
            if (request.InterfaceAsString == InterfaceName && request.MemberAsString == "GetTaskbarPreview" && request.SignatureAsString == "b")
            {
                using var previewWriter = context.CreateReplyWriter("s");
                previewWriter.WriteString(TaskbarPreviewState.Read(request.GetBodyReader().ReadBool()));
                context.Reply(previewWriter.CreateMessage());
                return default;
            }
            if (request.InterfaceAsString == InterfaceName
                && request.MemberAsString == SnapshotMember
                && request.SignatureAsString == "s")
            {
                publish(request.GetBodyReader().ReadString());
                using var writer = context.CreateReplyWriter("");
                context.Reply(writer.CreateMessage());
            }
            else if (request.InterfaceAsString == InterfaceName
                     && request.MemberAsString == "RequestScreenshot"
                     && request.SignatureAsString == "s")
            {
                screenshotRequested(request.GetBodyReader().ReadString());
                using var writer = context.CreateReplyWriter("");
                context.Reply(writer.CreateMessage());
            }
            else if (request.InterfaceAsString == InterfaceName
                     && request.MemberAsString == "RequestLock")
            {
                lockRequested();
                using var writer = context.CreateReplyWriter("");
                context.Reply(writer.CreateMessage());
            }
            else if (request.InterfaceAsString == InterfaceName
                     && request.MemberAsString == "RequestOverview")
            {
                overviewRequested();
                using var writer = context.CreateReplyWriter("");
                context.Reply(writer.CreateMessage());
            }
            else if (request.InterfaceAsString == InterfaceName
                     && request.MemberAsString == "RequestMediaAction"
                     && request.SignatureAsString == "s")
            {
                mediaActionRequested(request.GetBodyReader().ReadString());
                using var writer = context.CreateReplyWriter("");
                context.Reply(writer.CreateMessage());
            }
            else if (request.InterfaceAsString == InterfaceName
                     && request.MemberAsString == "UpdateLauncherPinch"
                     && request.SignatureAsString == "s")
            {
                launcherPinchChanged(request.GetBodyReader().ReadString());
                using var writer = context.CreateReplyWriter("");
                context.Reply(writer.CreateMessage());
            }
            else if (request.InterfaceAsString == InterfaceName
                     && request.MemberAsString == "RequestShellAction"
                     && request.SignatureAsString == "s")
            {
                shellActionRequested(request.GetBodyReader().ReadString());
                using var writer = context.CreateReplyWriter("");
                context.Reply(writer.CreateMessage());
            }

            return default;
        }
    }
}
