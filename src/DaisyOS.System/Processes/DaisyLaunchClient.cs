using System.Text.Json;
using System.Diagnostics;
using DaisyOS.Core.Models;
using Tmds.DBus.Protocol;

namespace DaisyOS.System.Processes;

/// <summary>Persistent, asynchronous session-bus client. Never retries ambiguous dispatches.</summary>
public sealed class DaisyLaunchClient
{
    public static DaisyLaunchClient Shared { get; } = new();
    private readonly SemaphoreSlim _connect = new(1, 1);
    private DBusConnection? _connection;
    private IDisposable? _registryObserver;
    private IDisposable? _ownerObserver;
    private long _catalogVersion;
    public long CatalogVersion => Interlocked.Read(ref _catalogVersion);
    public event Action? CatalogChanged;
    public static bool Enabled => Environment.GetEnvironmentVariable("DAISYOS_LAUNCH_SERVICE") != "0";
    public event Action<string, string, string>? StateChanged;

    private async Task<DBusConnection> ConnectionAsync(CancellationToken token)
    {
        await _connect.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_connection is null)
            {
                var connection = new DBusConnection(DBusAddress.Session ?? throw new IOException("No session bus."));
                try { await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false); }
                catch { connection.Dispose(); throw; }
                _registryObserver = await connection.AddMatchAsync<int>(new MatchRule
                { Type = MessageType.Signal, Sender = "org.daisyos.Launcher1", Path = "/org/daisyos/Launcher1", Interface = "org.daisyos.Launcher1", Member = "RegistryChanged" },
                    (message, _) => 0, notification => { if (notification.HasValue) InvalidateCatalog(); }, emitOnCapturedContext: false).ConfigureAwait(false);
                _ownerObserver = await connection.AddMatchAsync<int>(new MatchRule
                { Type = MessageType.Signal, Sender = "org.freedesktop.DBus", Interface = "org.freedesktop.DBus", Member = "NameOwnerChanged", Arg0 = "org.daisyos.Launcher1" },
                    (message, _) => 0, notification => { if (notification.HasValue) InvalidateCatalog(); }, emitOnCapturedContext: false).ConfigureAwait(false);
                _connection = connection;
            }
            return _connection;
        }
        finally { _connect.Release(); }
    }

    internal void InvalidateCatalog()
    {
        Interlocked.Increment(ref _catalogVersion);
        CatalogChanged?.Invoke();
    }

    private static AppEntry CatalogEntry(JsonElement entry)
    {
        var labels = entry.TryGetProperty("labels", out var map)
            ? map.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "") : new Dictionary<string, string>();
        var argv = entry.TryGetProperty("argv", out var arguments)
            ? arguments.EnumerateArray().Select(a => a.GetString()!).ToArray() : Array.Empty<string>();
        return new AppEntry(entry.GetProperty("id").GetString()!,
            DesktopEntryLocalization.GetValue(labels, "Name") ?? entry.GetProperty("name").GetString()!,
            DesktopEntryLocalization.GetValue(labels, "Comment") ?? entry.GetProperty("description").GetString()!,
            entry.GetProperty("icon").GetString()!, false, AppLaunchKind.DesktopEntry,
            argv.FirstOrDefault(), LaunchArguments: argv.Skip(1).ToArray(), Categories: entry.GetProperty("categories").EnumerateArray().Select(c => c.GetString()!).ToArray(),
            SearchTerms: new[] { DesktopEntryLocalization.GetValue(labels, "GenericName") ?? "" }
                .Concat((DesktopEntryLocalization.GetValue(labels, "Keywords") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)).ToArray());
    }

    private static MessageBuffer Call(DBusConnection connection, string member, string signature = "", params string[] strings)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: "org.daisyos.Launcher1", path: "/org/daisyos/Launcher1",
            @interface: "org.daisyos.Launcher1", member: member, signature: signature);
        foreach (var value in strings) writer.WriteString(value);
        return writer.CreateMessage();
    }

    private static string HelperBinary()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "daisy-launch");
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "daisy-launch");
        return File.Exists(bundled) ? bundled : File.Exists(local) ? local : "daisy-launch";
    }
    public static async Task<IReadOnlyList<AppEntry>> CatalogFallbackAsync()
    {
        var result = await Task.Run(() => new SafeCommandRunner().RunAsync(HelperBinary(), ["--catalog"], TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        if (!result.Succeeded) throw new IOException("The native application catalog is unavailable.");
        using var document = JsonDocument.Parse(result.StandardOutput);
        return document.RootElement.EnumerateArray().Select(CatalogEntry).ToArray();
    }

    private readonly Func<CancellationToken, Task<string>> _readCatalog;

    public DaisyLaunchClient() => _readCatalog = ReadCatalogAsync;
    internal DaisyLaunchClient(Func<CancellationToken, Task<string>> readCatalog) => _readCatalog = readCatalog;

    private async Task<string> ReadCatalogAsync(CancellationToken token)
    {
        var connection = await ConnectionAsync(token).ConfigureAwait(false);
        return await connection.CallMethodAsync(Call(connection, "Catalog"), (reply, _) => reply.GetBodyReader().ReadString())
            .WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
    }

    private readonly SemaphoreSlim _catalogRead = new(1, 1);
    private IReadOnlyList<AppEntry>? _catalog;
    private long _cachedCatalogVersion = -1;
    private string? _catalogCulture;

    public async Task<IReadOnlyList<AppEntry>> CatalogAsync(CancellationToken token = default)
    {
        // Launcher, Settings and desktop shortcuts share one parsed snapshot.
        // Serialize misses so opening several surfaces never duplicates the RPC
        // and JSON allocations. Registry signals invalidate it immediately.
        await _catalogRead.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var version = CatalogVersion;
            var culture = global::System.Globalization.CultureInfo.CurrentUICulture.Name;
            if (_catalog is not null && _cachedCatalogVersion == version && _catalogCulture == culture)
                return _catalog;

            var json = await _readCatalog(token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            _catalog = Array.AsReadOnly(document.RootElement.EnumerateArray().Select(CatalogEntry).ToArray());
            _cachedCatalogVersion = version;
            _catalogCulture = culture;
            return _catalog;
        }
        finally { _catalogRead.Release(); }
    }

    private readonly object _dedupeGate = new();
    private readonly Dictionary<string, (long Started, Task<AppLaunchResult> Result)> _recent = new();
    public Task<AppLaunchResult> LaunchAsync(string desktopId, string action = "", IReadOnlyList<string>? uris = null,
        string activationToken = "", CancellationToken token = default)
    {
        if (Environment.GetEnvironmentVariable("DAISYOS_LAUNCH_DEDUPE") == "0" || action.Length > 0 || uris?.Count > 0)
            return LaunchCoreAsync(desktopId, action, uris, activationToken, token);
        lock (_dedupeGate)
        {
            if (_recent.TryGetValue(desktopId, out var previous) && Stopwatch.GetElapsedTime(previous.Started).TotalMilliseconds < 200)
                return previous.Result.WaitAsync(token);
            var task = LaunchCoreAsync(desktopId, action, uris, activationToken, token);
            _recent[desktopId] = (Stopwatch.GetTimestamp(), task);
            _ = RemoveRecentAsync(desktopId, task);
            return task;
        }
    }
    private async Task RemoveRecentAsync(string app, Task<AppLaunchResult> task)
    {
        await Task.Delay(250).ConfigureAwait(false);
        lock (_dedupeGate) if (_recent.TryGetValue(app, out var entry) && ReferenceEquals(entry.Result, task)) _recent.Remove(app);
    }

    private async Task<AppLaunchResult> LaunchCoreAsync(string desktopId, string action = "", IReadOnlyList<string>? uris = null,
        string activationToken = "", CancellationToken token = default)
    {
        var id = Guid.NewGuid().ToString("N");
        StateChanged?.Invoke(id, desktopId, "Requested");
        try
        {
            var connection = await ConnectionAsync(token).ConfigureAwait(false);
            MessageBuffer message;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination: "org.daisyos.Launcher1", path: "/org/daisyos/Launcher1",
                    @interface: "org.daisyos.Launcher1", member: "Launch", signature: "sssassa{sv}");
                writer.WriteString(id); writer.WriteString(desktopId); writer.WriteString(action);
                writer.WriteArray((uris ?? []).ToArray()); writer.WriteString(activationToken);
                var dict = writer.WriteDictionaryStart(); writer.WriteDictionaryEnd(dict);
                message = writer.CreateMessage();
            }
            var accepted = await connection.CallMethodAsync(message, (reply, _) => reply.GetBodyReader().ReadBool())
                .WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            if (!accepted)
            {
                StateChanged?.Invoke(id, desktopId, "Rejected");
                return new(false, "Application launch was rejected.", RequestId: id, ErrorCode: "Rejected");
            }
            await connection.CallMethodAsync(Call(connection, "Await", "s", id), (reply, _) => reply.GetBodyReader().ReadBool())
                .WaitAsync(TimeSpan.FromSeconds(17), token).ConfigureAwait(false);
            StateChanged?.Invoke(id, desktopId, "Accepted");
            return new(true, "Application launch requested.", RequestId: id);
        }
        catch (DBusErrorReplyException ex) when (ex.ErrorName is "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.Spawn.ExecFailed" or "org.freedesktop.DBus.Error.Spawn.ChildExited")
        {
            StateChanged?.Invoke(id, desktopId, "Unavailable");
            // These replies prove the service never accepted the request. No fallback
            // for timeout/disconnection: it could duplicate a successfully started app.
            return await FallbackAsync(desktopId, action, uris, activationToken, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (_connection is { } connection)
                try { await connection.CallMethodAsync(Call(connection, "Cancel", "s", id)).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); } catch { }
            StateChanged?.Invoke(id, desktopId, "Cancelled"); throw;
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException or TimeoutException)
        {
            if (ex is DBusConnectionClosedException)
            {
                await _connect.WaitAsync().ConfigureAwait(false);
                try { _registryObserver?.Dispose(); _ownerObserver?.Dispose(); _connection?.Dispose(); _connection = null; }
                finally { _connect.Release(); }
            }
            var state = ex is DBusErrorReplyException reply && reply.ErrorName.StartsWith("org.daisyos.Launcher1.", StringComparison.Ordinal)
                ? reply.ErrorName["org.daisyos.Launcher1.".Length..] : "Unknown";
            StateChanged?.Invoke(id, desktopId, state);
            return new(false, state == "Unknown" ? "The application launch could not be confirmed. Check whether its window opened before retrying." : "The application could not be opened.", RequestId: id, ErrorCode: state);
        }
    }

    public async Task<string> TraceAsync(string id)
    {
        var connection = await ConnectionAsync(default).ConfigureAwait(false);
        return await connection.CallMethodAsync(Call(connection, "Trace", "s", id), (reply, _) => reply.GetBodyReader().ReadString())
            .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }

    public async Task AssociateAsync(string id, string window)
    {
        try
        {
            var connection = await ConnectionAsync(default).ConfigureAwait(false);
            await connection.CallMethodAsync(Call(connection, "Associate", "ss", id, window)).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or DBusErrorReplyException) { }
    }

    public static async Task<AppLaunchResult> FallbackAsync(string desktopId, string action = "", IReadOnlyList<string>? uris = null,
        string activationToken = "", CancellationToken token = default)
    {
        var arguments = new List<string> { "--launch", desktopId };
        if (action.Length > 0) arguments.AddRange(["--action", action]);
        if (activationToken.Length > 0) arguments.AddRange(["--token", activationToken]);
        foreach (var uri in uris ?? []) arguments.AddRange(["--uri", uri]);
        var helper = HelperBinary();
        var result = await Task.Run(() => new SafeCommandRunner().RunAsync(helper, arguments, TimeSpan.FromSeconds(18), token), token).ConfigureAwait(false);
        return new(result.Succeeded, result.Succeeded ? "Application launch requested." : "The application could not be opened.");
    }
}
