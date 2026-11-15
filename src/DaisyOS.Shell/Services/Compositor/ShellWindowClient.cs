using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Services.Compositor;

/// <summary>Reads the desktop's published windows without taking ownership of its bridge.</summary>
public sealed class ShellWindowClient : ICompositorWindowService
{
    private readonly CancellationTokenSource _stop = new();
    private readonly KWinCommandDispatcher _commands = new();
    private DBusConnection? _connection;
    private Task? _poll;
    private IReadOnlyCollection<CompositorWindowRecord> _windows = [];
    public IReadOnlyCollection<CompositorWindowRecord> Windows => Volatile.Read(ref _windows);
    public bool IsAvailable { get; private set; }
    public event EventHandler? WindowsChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_poll is not null || _stop.IsCancellationRequested) return;
        await RefreshAsync(cancellationToken);
        if (!_stop.IsCancellationRequested) _poll = PollAsync();
    }
    private async Task PollAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(_stop.Token)) await RefreshAsync(_stop.Token);
        }
        catch (OperationCanceledException) { }
    }
    private async Task RefreshAsync(CancellationToken token)
    {
        try
        {
            if (_connection is null)
            {
                _connection = new DBusConnection(DBusAddress.Session ?? throw new IOException("No desktop session bus"));
                await _connection.ConnectAsync();
                KWinCommandDispatcher.SetConnection(_connection);
            }
            MessageBuffer call;
            using (var writer = _connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination: KWinWindowBridge.BusName, path: KWinWindowBridge.ObjectPath,
                    @interface: KWinWindowBridge.InterfaceName, member: "GetCaptureWindows");
                call = writer.CreateMessage();
            }
            var json = await _connection.CallMethodAsync(call, (reply, _) => reply.GetBodyReader().ReadString(), (object?)null)
                .WaitAsync(TimeSpan.FromSeconds(2), token);
            if (!KWinWindowSnapshotParser.TryParse(json, out var windows)) throw new IOException("Invalid desktop window snapshot");
            var changed = !IsAvailable || !Windows.SequenceEqual(windows);
            IsAvailable = true;
            Volatile.Write(ref _windows, windows.ToArray());
            if (changed) WindowsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _connection?.Dispose(); _connection = null; KWinCommandDispatcher.SetConnection(null);
            var changed = IsAvailable; IsAvailable = false; Volatile.Write(ref _windows, []);
            if (changed) WindowsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public Task ActivateOrMinimizeAsync(string id, bool wasActive, CancellationToken cancellationToken = default) => _commands.DispatchAsync(id, KWinWindowCommand.ActivateOrMinimize, cancellationToken, wasActive: wasActive);
    public Task CloseAsync(string id, CancellationToken cancellationToken = default) => _commands.DispatchAsync(id, KWinWindowCommand.Close, cancellationToken);
    public Task<bool> ForceQuitAsync(string id, CancellationToken cancellationToken = default) =>
        Windows.FirstOrDefault(w => w.Id == id) is { IsUnresponsive: true, CanClose: true, ProcessId: > 1 } window
            ? _commands.ForceQuitAsync(window.ProcessId, cancellationToken) : Task.FromResult(false);
    public Task SetMaximizedAsync(string id, bool maximized, CancellationToken cancellationToken = default) => _commands.DispatchAsync(id, maximized ? KWinWindowCommand.Maximize : KWinWindowCommand.Restore, cancellationToken);
    public Task MoveToWorkspaceAsync(string id, int workspace, CancellationToken cancellationToken = default) => _commands.DispatchAsync(id, KWinWindowCommand.MoveToWorkspace, cancellationToken, workspace);
    public Task ToggleOverviewEffectAsync(string effectName = "daisyos-task-overview-v16", CancellationToken cancellationToken = default) => _commands.ToggleOverviewEffectAsync(effectName, cancellationToken);
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_poll is not null) await _poll;
        _connection?.Dispose(); _connection = null; KWinCommandDispatcher.SetConnection(null);
        IsAvailable = false; _stop.Dispose();
    }
}
