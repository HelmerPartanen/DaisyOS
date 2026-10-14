using System.Runtime.InteropServices;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;

namespace DaisyOS.System.Authentication;

/// <summary>
/// Authenticates the current session user directly through Linux PAM. A
/// redirected `su` process cannot securely receive a password because su
/// reads from the controlling terminal, so lock-screen authentication must
/// use PAM's conversation API instead.
/// </summary>
public sealed class LinuxPamAuthenticationService : IAuthenticationService
{
    private const int PamSuccess = 0;
    private const int PamPromptEchoOff = 1;
    private const int PamPromptEchoOn = 2;
    private const int PamConversationError = 19;
    private readonly ILogService _log;

    public LinuxPamAuthenticationService(ILogService? log = null)
    {
        _log = log ?? NullLogService.Instance;
    }

    public bool Authenticate(string password)
    {
        if (string.IsNullOrEmpty(password)) return false;

        IntPtr handle = IntPtr.Zero;
        var status = PamSuccess;
        var conversationCallback = new PamConversationCallback(
            (int messageCount, IntPtr messages, out IntPtr responses, IntPtr _) =>
                Converse(messageCount, messages, password, out responses));
        var conversation = new PamConversation
        {
            Callback = Marshal.GetFunctionPointerForDelegate(conversationCallback),
            Data = IntPtr.Zero
        };

        try
        {
            var serviceName = File.Exists("/etc/pam.d/system-auth") ? "system-auth" : "login";
            status = PamStart(serviceName, Environment.UserName, ref conversation, out handle);
            if (status != PamSuccess)
            {
                LogFailure(handle, status, "start authentication");
                return false;
            }

            status = PamAuthenticate(handle, 0);
            if (status != PamSuccess) LogFailure(handle, status, "verify password");
            // This unlocks an existing user session; pam_acct_mgmt is a login
            // authorization check and can incorrectly deny an otherwise valid
            // password due to time/access policy after the session is active.
            return status == PamSuccess;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            _log.Log(LogLevel.Warning, "Couldn't access Linux authentication services.", exception);
            return false;
        }
        catch (Exception exception)
        {
            _log.Log(LogLevel.Warning, "Linux authentication failed unexpectedly.", exception);
            return false;
        }
        finally
        {
            if (handle != IntPtr.Zero) PamEnd(handle, status);
            GC.KeepAlive(conversationCallback);
        }
    }

    private static int Converse(int count, IntPtr messages, string password, out IntPtr responses)
    {
        responses = IntPtr.Zero;
        if (count <= 0) return PamConversationError;

        var responseSize = Marshal.SizeOf<PamResponse>();
        var buffer = Calloc((nuint)count, (nuint)responseSize);
        if (buffer == IntPtr.Zero) return PamConversationError;

        try
        {
            for (var index = 0; index < count; index++)
            {
                var messagePointer = Marshal.ReadIntPtr(messages, index * IntPtr.Size);
                var message = Marshal.PtrToStructure<PamMessage>(messagePointer);
                var reply = StrDup(message.Style is PamPromptEchoOff or PamPromptEchoOn ? password : string.Empty);
                if (reply == IntPtr.Zero) throw new OutOfMemoryException();
                Marshal.StructureToPtr(new PamResponse { Response = reply }, buffer + (index * responseSize), false);
            }

            responses = buffer;
            return PamSuccess;
        }
        catch
        {
            for (var index = 0; index < count; index++)
            {
                var response = Marshal.PtrToStructure<PamResponse>(buffer + (index * responseSize));
                if (response.Response != IntPtr.Zero) Free(response.Response);
            }

            Free(buffer);
            return PamConversationError;
        }
    }

    private void LogFailure(IntPtr handle, int status, string operation)
    {
        var messagePointer = handle == IntPtr.Zero ? IntPtr.Zero : PamStrError(handle, status);
        var detail = messagePointer == IntPtr.Zero ? "Unknown error" : Marshal.PtrToStringUTF8(messagePointer);
        _log.Log(LogLevel.Warning, $"Couldn't {operation} (PAM status {status}: {detail}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PamConversation
    {
        public IntPtr Callback;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PamMessage
    {
        public int Style;
        public IntPtr Message;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PamResponse
    {
        public IntPtr Response;
        public int ReturnCode;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PamConversationCallback(int messageCount, IntPtr messages, out IntPtr responses, IntPtr data);

    [DllImport("libpam.so.0", EntryPoint = "pam_start", CallingConvention = CallingConvention.Cdecl)]
    private static extern int PamStart(string serviceName, string user, ref PamConversation conversation, out IntPtr handle);

    [DllImport("libpam.so.0", EntryPoint = "pam_authenticate", CallingConvention = CallingConvention.Cdecl)]
    private static extern int PamAuthenticate(IntPtr handle, int flags);

    [DllImport("libpam.so.0", EntryPoint = "pam_end", CallingConvention = CallingConvention.Cdecl)]
    private static extern int PamEnd(IntPtr handle, int status);

    [DllImport("libpam.so.0", EntryPoint = "pam_strerror", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr PamStrError(IntPtr handle, int status);

    [DllImport("libc", EntryPoint = "calloc", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Calloc(nuint count, nuint size);

    [DllImport("libc", EntryPoint = "strdup", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr StrDup([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport("libc", EntryPoint = "free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Free(IntPtr pointer);
}
