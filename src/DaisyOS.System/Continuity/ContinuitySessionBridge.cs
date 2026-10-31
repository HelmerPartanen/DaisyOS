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
            while (!token.IsCancellationRequested && !_disposed)
            {
                try
                {
                    await StartServiceAsync(token);
                    var bus = new DBusConnection(DBusAddress.Session ?? throw new IOException());
                    _bus = bus;
                    await bus.ConnectAsync();
                    if (!await bus.TryRequestNameAsync("org.daisyos.ContinuitySession1")) return;
                    _owner = await bus.AddMatchAsync<int>(new MatchRule { Type = MessageType.Signal, Sender = "org.freedesktop.DBus",
                        Interface = "org.freedesktop.DBus", Member = "NameOwnerChanged", Arg0 = ContinuityClient.BusName },
                        (message, _) => 0, notification => { if (notification.HasValue) Wake(); }, emitOnCapturedContext: false);
                    Wake();
                    while (!token.IsCancellationRequested && !_disposed)
                    {
                        // Retry missed startup/wake updates without requiring Settings
                        // to be opened or a new NameOwnerChanged signal to arrive.
                        await _signal.WaitAsync(TimeSpan.FromSeconds(5), token);
                        try { await SendAsync(token); }
                        catch (Exception ex) when (ex is IOException or DBusExceptionBase or TimeoutException or ObjectDisposedException)
                        { await StartServiceAsync(token); break; }
                    }
                }
                catch (Exception ex) when (ex is IOException or DBusExceptionBase or TimeoutException or ObjectDisposedException) { }
                finally
                {
                    _owner?.Dispose(); _owner = null;
                    _bus?.Dispose(); _bus = null;
                }
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { Dispose(); }
    }
    private static async Task StartServiceAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) return;
        // Starting the daemon preserves its saved enabled/paused state and trust.
        // Native sessions do not necessarily activate graphical-session.target.
        // Never replace a stop/restart job belonging to the upgrade installer.
        await new Processes.SafeCommandRunner().RunAsync("systemctl", ["--user", "--job-mode=fail", "start", "daisyos-continuity.service"], TimeSpan.FromSeconds(3), token);
    }
    private void Wake() { try { _signal.Release(); } catch (SemaphoreFullException) { } }
    public async Task SetLockedAsync(bool locked)
    {
        _locked = locked;
        if (_bus is null || _disposed) return;
        try { await SendAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch
        {
            // Losing the owner stops input immediately if the lock update failed.
            // RunAsync reconnects it instead of permanently disabling Continuity.
            if (locked) _bus?.Dispose();
            Wake();
        }
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
        Wake();
        _owner?.Dispose(); _owner = null; _bus?.Dispose(); _bus = null;
    }
}
