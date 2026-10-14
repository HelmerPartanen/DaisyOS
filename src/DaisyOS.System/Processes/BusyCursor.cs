using Tmds.DBus.Protocol;

namespace DaisyOS.System.Processes;

/// <summary>
/// A native, non-blocking cursor lease. Dispose when the operation really finishes,
/// including failure/cancellation. Concurrent scopes remain independent. No UI thread waits.
/// </summary>
public sealed class BusyCursor : IDisposable
{
    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly bool _blocking;
    private readonly CancellationTokenSource _finished = new();
    private readonly Task _lease;
    private int _disposed;

    private BusyCursor(bool blocking)
    {
        _blocking = blocking;
        _lease = Task.Run(RunAsync);
    }

    /// <param name="blocking">Use the wait spinner when the task prevents interaction;
    /// otherwise retain the arrow beside the working spinner.</param>
    public static BusyCursor Begin(bool blocking = false) => new(blocking);

    private MessageBuffer Call(DBusConnection bus, bool begin)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: "org.kde.KWin", path: "/org/daisyos/BusyCursor1",
            @interface: "org.daisyos.BusyCursor1", member: begin ? "Begin" : "End", signature: begin ? "sb" : "s");
        writer.WriteString(_id);
        if (begin) writer.WriteBool(_blocking);
        return writer.CreateMessage();
    }

    private async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux() || DBusAddress.Session is not { } address) return;
        using var bus = new DBusConnection(address);
        try
        {
            await bus.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            while (!_finished.IsCancellationRequested)
            {
                if (!await bus.CallMethodAsync(Call(bus, true), (reply, _) => reply.GetBodyReader().ReadBool())
                    .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false)) return;
                await Task.Delay(TimeSpan.FromSeconds(5), _finished.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_finished.IsCancellationRequested) { }
        catch (Exception) { /* Feedback is optional; never fail the user's operation. */ }
        finally
        {
            try { await bus.CallMethodAsync(Call(bus, false)).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception) { /* Disconnect also releases all leases owned by this connection. */ }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _finished.Cancel();
        _ = _lease.ContinueWith(_ => _finished.Dispose(), TaskScheduler.Default);
    }
}
