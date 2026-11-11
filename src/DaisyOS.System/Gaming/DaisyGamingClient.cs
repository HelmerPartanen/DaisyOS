using Tmds.DBus.Protocol;

namespace DaisyOS.System.Gaming;

/// <summary>Small session-bus client for the per-user DaisyOS gaming coordinator.</summary>
public sealed class DaisyGamingClient : IDisposable
{
    public const string BusName = "org.daisyos.Gaming1";
    public const string ObjectPath = "/org/daisyos/Gaming1";
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private DBusConnection? _connection;

    private async Task<DBusConnection> ConnectionAsync(CancellationToken token)
    {
        if (_connection is not null) return _connection;
        await _connectGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_connection is null)
            {
                var connection = new DBusConnection(DBusAddress.Session ?? throw new IOException("No session bus."));
                try { await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false); }
                catch { connection.Dispose(); throw; }
                _connection = connection;
            }
            return _connection;
        }
        finally { _connectGate.Release(); }
    }

    private static MessageBuffer BuildCall(DBusConnection connection, string member, string signature = "")
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: BusName, path: ObjectPath, @interface: BusName, member: member, signature: signature);
        return writer.CreateMessage();
    }

    private static MessageBuffer BuildConfigureCall(DBusConnection connection, bool automatic, bool allowBatteryBoost, bool performanceProfile)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: BusName, path: ObjectPath, @interface: BusName, member: "Configure", signature: "bbb");
        writer.WriteBool(automatic);
        writer.WriteBool(allowBatteryBoost);
        writer.WriteBool(performanceProfile);
        return writer.CreateMessage();
    }

    private static MessageBuffer BuildManualCall(DBusConnection connection, bool enabled, bool userInitiated)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: BusName, path: ObjectPath, @interface: BusName, member: "SetManualEnabled", signature: "bb");
        writer.WriteBool(enabled);
        writer.WriteBool(userInitiated);
        return writer.CreateMessage();
    }

    public async Task ConfigureAsync(bool automatic, bool allowBatteryBoost, bool performanceProfile, CancellationToken token)
    {
        var connection = await ConnectionAsync(token).ConfigureAwait(false);
        await connection.CallMethodAsync(BuildConfigureCall(connection, automatic, allowBatteryBoost, performanceProfile))
            .WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
    }

    public async Task SetManualEnabledAsync(bool enabled, bool userInitiated, CancellationToken token)
    {
        var connection = await ConnectionAsync(token).ConfigureAwait(false);
        await connection.CallMethodAsync(BuildManualCall(connection, enabled, userInitiated))
            .WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
    }

    public async Task<bool> GetGamingActiveAsync(CancellationToken token)
    {
        var connection = await ConnectionAsync(token).ConfigureAwait(false);
        var status = await connection.CallMethodAsync(BuildCall(connection, "Status"),
            (reply, _) => reply.GetBodyReader().ReadString()).WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
        using var document = global::System.Text.Json.JsonDocument.Parse(status);
        return document.RootElement.GetProperty("runtime").GetProperty("gaming_active").GetBoolean();
    }

    public void ResetConnection()
    {
        var connection = Interlocked.Exchange(ref _connection, null);
        connection?.Dispose();
    }

    public void Dispose()
    {
        ResetConnection();
        _connectGate.Dispose();
    }
}
