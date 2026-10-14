using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Apps.Files.Services;

/// <summary>
/// Implements the freedesktop file-manager activation contract used by web
/// browsers and desktop applications for "Show in folder" actions.
/// </summary>
public sealed class FileManagerActivationService : IAsyncDisposable
{
    public const string BusName = "org.freedesktop.FileManager1";
    public const string ObjectPath = "/org/freedesktop/FileManager1";
    public const string InterfaceName = "org.freedesktop.FileManager1";

    private DBusConnection? _connection;
    private FileManagerHandler? _handler;

    public bool IsAvailable { get; private set; }
    public event EventHandler<FileManagerActivationRequest>? ActivationRequested;

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

            var handler = new FileManagerHandler(PublishRequest);
            connection.AddMethodHandler(handler);

            // Never displace an already running file manager. A competing
            // desktop session remains in control of its own file activation.
            if (!await connection.TryRequestNameAsync(BusName, RequestNameOptions.None))
            {
                handler.Dispose();
                connection.Dispose();
                Console.Error.WriteLine("[files] Another file manager already owns the session activation service.");
                return;
            }

            _connection = connection;
            _handler = handler;
            IsAvailable = true;
            Console.Error.WriteLine("[files] Registered the freedesktop file-manager activation service.");
        }
        catch (Exception exception)
        {
            _handler?.Dispose();
            _handler = null;
            _connection?.Dispose();
            _connection = null;
            IsAvailable = false;
            Console.Error.WriteLine($"[files] Could not register file activation: {exception.Message}");
        }
    }

    private void PublishRequest(FileManagerActivationRequest request) =>
        ActivationRequested?.Invoke(this, request);

    public ValueTask DisposeAsync()
    {
        _handler?.Dispose();
        _handler = null;
        _connection?.Dispose();
        _connection = null;
        IsAvailable = false;
        return ValueTask.CompletedTask;
    }

    private sealed class FileManagerHandler(Action<FileManagerActivationRequest> publish) : IPathMethodHandler, IDisposable
    {
        private static readonly ReadOnlyMemory<byte> IntrospectionXml = """
            <interface name="org.freedesktop.FileManager1">
              <method name="ShowFolders">
                <arg direction="in" type="as" name="uris"/>
                <arg direction="in" type="s" name="startup_id"/>
              </method>
              <method name="ShowItems">
                <arg direction="in" type="as" name="uris"/>
                <arg direction="in" type="s" name="startup_id"/>
              </method>
              <method name="ShowItemProperties">
                <arg direction="in" type="as" name="uris"/>
                <arg direction="in" type="s" name="startup_id"/>
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
            var member = request.MemberAsString;
            if (request.InterfaceAsString == InterfaceName
                && request.SignatureAsString == "ass"
                && member is "ShowFolders" or "ShowItems" or "ShowItemProperties")
            {
                var reader = request.GetBodyReader();
                var uris = reader.ReadArrayOfString();
                _ = reader.ReadString(); // Startup notification is handled by KWin.
                publish(new FileManagerActivationRequest(
                    uris.Select(FileManagerPath.GetLocalPathOrNull)
                        .Where(path => path is not null)
                        .Cast<string>()
                        .ToArray(),
                    SelectItems: member is "ShowItems" or "ShowItemProperties"));

                using var writer = context.CreateReplyWriter("");
                context.Reply(writer.CreateMessage());
            }

            return default;
        }

        public void Dispose()
        {
        }
    }
}

public sealed record FileManagerActivationRequest(IReadOnlyList<string> Paths, bool SelectItems);

public static class FileManagerPath
{
    /// <summary>Converts only local file URIs. Network and malformed URIs are deliberately ignored.</summary>
    public static bool TryGetLocalPath(string? value, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile)
                {
                    return false;
                }

                path = Path.GetFullPath(uri.LocalPath);
                return true;
            }

            path = Path.GetFullPath(value);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static string? GetLocalPathOrNull(string value) =>
        TryGetLocalPath(value, out var path) ? path : null;
}
