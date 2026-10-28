using Tmds.DBus.Protocol;

namespace DaisyOS.System.Continuity;

/// <summary>The shell owns a bus name for its whole lifetime. Losing that owner
/// invalidates daemon session access, independently of Settings.</summary>
public sealed class ContinuitySessionBridge : IDisposable
{
    private DBusConnection? _bus;
    private IDisposable? _owner;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly SemaphoreSlim _send = new(1, 1);
    private bool _locked;
    private bool _disposed;
    public async Task RunAsync(CancellationToken token)
    {
        try
        {
            var bus = new DBusConnection(DBusAddress.Session ?? throw new IOException());
            _bus = bus;
            await bus.ConnectAsync();
            if (!await bus.TryRequestNameAsync("org.daisyos.ContinuitySession1")) { Dispose(); return; }
            _owner = await bus.AddMatchAsync<int>(new MatchRule { Type = MessageType.Signal, Sender = "org.freedesktop.DBus",
                Interface = "org.freedesktop.DBus", Member = "NameOwnerChanged", Arg0 = ContinuityClient.BusName },
                (message, _) => 0, notification => { if (notification.HasValue) Wake(); }, emitOnCapturedContext: false);
            Wake();
            while (!token.IsCancellationRequested)
            {
                await _signal.WaitAsync(token);
                try { await SendAsync(token); } catch (Exception ex) when (ex is IOException or DBusExceptionBase or TimeoutException) { }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or DBusExceptionBase) { }
        finally { Dispose(); }
    }
    private void Wake() { try { _signal.Release(); } catch (SemaphoreFullException) { } }
    public async Task SetLockedAsync(bool locked)
    {
        _locked = locked;
        if (_bus is null || _disposed) return;
        try { await SendAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch { if (locked) Dispose(); else Wake(); }
    }
    private async Task SendAsync(CancellationToken token)
    {
        await _send.WaitAsync(token);
        try
        {
            if (_bus is not null && !_disposed)
                await ContinuityClient.ExecuteOnAsync(_bus, new("session", Value: Environment.GetEnvironmentVariable("XDG_SESSION_ID") ?? "", Allowed: !_locked), token);
        }
        finally { _send.Release(); }
    }
    public void Dispose()
    {
        _disposed = true;
        _owner?.Dispose(); _owner = null; _bus?.Dispose(); _bus = null;
    }
}
