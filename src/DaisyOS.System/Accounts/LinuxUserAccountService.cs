using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Tmds.DBus.Protocol;

namespace DaisyOS.System.Accounts;

/// <summary>Edits only the current session's account through the system account authority.</summary>
public sealed class LinuxUserAccountService : IUserAccountService
{
    private const string Destination = "org.freedesktop.Accounts";
    private const string UserInterface = "org.freedesktop.Accounts.User";
    private static readonly object CryptLock = new();

    public async Task<AccountReadResult> GetCurrentAccountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new DBusConnection(DBusAddress.System ?? throw new IOException());
            await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            var path = await FindCurrentUserAsync(connection, cancellationToken);
            MessageBuffer message;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination: Destination, path: path, @interface: "org.freedesktop.DBus.Properties", member: "GetAll", signature: "s");
                writer.WriteString(UserInterface);
                message = writer.CreateMessage();
            }
            var properties = await connection.CallMethodAsync(message,
                (reply, _) => reply.GetBodyReader().ReadDictionaryOfStringToVariantValue()).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            return new(new UserAccount(properties["UserName"].GetString(), properties["RealName"].GetString(),
                properties["AccountType"].GetInt32() == 1, properties["PasswordMode"].GetInt32(),
                properties["AutomaticLogin"].GetBool(), properties["LocalAccount"].GetBool()));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (IsServiceError(ex))
        {
            return new(null, "Account settings are unavailable right now. Try again.");
        }
    }

    public Task<AccountChangeResult> SetFullNameAsync(string fullName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 128 || fullName.Any(char.IsControl) || fullName.Contains(':'))
            return Task.FromResult(new AccountChangeResult(false, "Enter a valid account name."));
        return ChangeAsync("SetRealName", "s", fullName.Trim(), null, null, cancellationToken);
    }

    public async Task<AccountChangeResult> SetPasswordAsync(string password, string hint, CancellationToken cancellationToken = default)
    {
        if (password.Length < 8 || password.Contains('\0') || Encoding.UTF8.GetByteCount(password) > 512)
            return new(false, "Use a password with at least 8 characters and no more than 512 UTF-8 bytes.");
        if (hint.Length > 1024 || hint.Any(char.IsControl)) return new(false, "Enter a valid password hint.");
        try
        {
            // Neither a password nor its hash is placed in process arguments or settings files.
            var hash = await Task.Run(() => HashPassword(password), cancellationToken);
            return await ChangeAsync("SetPassword", "ss", hash, hint, null, cancellationToken);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or CryptographicException)
        {
            return new(false, "Password changes are unavailable on this device.");
        }
    }

    public Task<AccountChangeResult> RemovePasswordAsync(CancellationToken cancellationToken = default) =>
        ChangeAsync("SetPasswordMode", "i", null, null, 2, cancellationToken);

    public Task<AccountChangeResult> SetAutomaticLoginAsync(bool enabled, CancellationToken cancellationToken = default) =>
        ChangeAsync("SetAutomaticLogin", "b", null, null, enabled ? 1 : 0, cancellationToken);

    private static async Task<AccountChangeResult> ChangeAsync(string member, string signature, string? first, string? second, int? number, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new DBusConnection(DBusAddress.System ?? throw new IOException());
            await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            var path = await FindCurrentUserAsync(connection, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            MessageBuffer message;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination: Destination, path: path, @interface: UserInterface, member: member,
                    signature: signature, flags: MessageFlags.AllowInteractiveAuthorization);
                if (signature == "i") writer.WriteInt32(number!.Value);
                else if (signature == "b") writer.WriteBool(number == 1);
                else
                {
                    writer.WriteString(first!);
                    if (second is not null) writer.WriteString(second);
                }
                message = writer.CreateMessage();
            }
            // Allow time for the system authorization dialog. A successful reply is
            // required before the UI reports that an account change was saved.
            await connection.CallMethodAsync(message).WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
            return new(true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (IsServiceError(ex))
        {
            return new(false, "The account change was not completed. Check authorization and try again.");
        }
    }

    private static async Task<string> FindCurrentUserAsync(DBusConnection connection, CancellationToken cancellationToken)
    {
        MessageBuffer message;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: Destination, path: "/org/freedesktop/Accounts", @interface: Destination,
                member: "FindUserByName", signature: "s");
            writer.WriteString(Environment.UserName);
            message = writer.CreateMessage();
        }
        return await connection.CallMethodAsync(message, (reply, _) => reply.GetBodyReader().ReadObjectPath().ToString())
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    internal static string HashPassword(string password)
    {
        var salt = "$6$rounds=100000$" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)).Replace('+', '.') + "$";
        // crypt() owns a static buffer. Serialize access and copy its result
        // before another caller can overwrite it; clear the temporary plaintext.
        var secret = Marshal.StringToCoTaskMemUTF8(password);
        try
        {
            lock (CryptLock)
            {
                var pointer = Crypt(secret, salt);
                var hash = pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);
                return hash is not null && hash.StartsWith("$6$", StringComparison.Ordinal)
                    ? hash : throw new CryptographicException();
            }
        }
        finally
        {
            var length = Encoding.UTF8.GetByteCount(password);
            for (var i = 0; i < length; i++) Marshal.WriteByte(secret, i, 0);
            Marshal.FreeCoTaskMem(secret);
        }
    }

    private static bool IsServiceError(Exception ex) => ex is IOException or TimeoutException or DBusExceptionBase
        or InvalidOperationException or KeyNotFoundException or ArgumentException or PlatformNotSupportedException or global::System.Net.Sockets.SocketException;

    private static IntPtr Crypt(IntPtr password, string salt)
    {
        try { return CryptCurrent(password, salt); }
        catch (DllNotFoundException) { return CryptLegacy(password, salt); }
    }

    [DllImport("libcrypt.so.2", EntryPoint = "crypt", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr CryptCurrent(IntPtr password, [MarshalAs(UnmanagedType.LPUTF8Str)] string salt);

    [DllImport("libcrypt.so.1", EntryPoint = "crypt", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr CryptLegacy(IntPtr password, [MarshalAs(UnmanagedType.LPUTF8Str)] string salt);
}
