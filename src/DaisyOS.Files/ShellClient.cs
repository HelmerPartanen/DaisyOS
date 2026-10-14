using System.Text.Json;
using Tmds.DBus.Protocol;

namespace DaisyOS.Files;

/// <summary>Only shell-owned UI is requested over IPC; file operations stay in Files.</summary>
internal static class ShellClient
{
    public static Task ActivateAsync() => RequestAsync("activate-files");
    public static Task<bool> ShareAsync(IEnumerable<string> paths) => RequestAsync("files-share:" + JsonSerializer.Serialize(paths.ToArray()));
    public static Task<bool> ShowFeedbackAsync(string message) => RequestAsync("app-feedback:" + message);

    private static async Task<bool> RequestAsync(string action)
    {
        try
        {
            using var connection = new DBusConnection(DBusAddress.Session ?? throw new IOException("No session bus."));
            await connection.ConnectAsync();
            MessageBuffer message;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination: "org.daisyos.Shell.WindowBridge",
                    path: "/org/daisyos/Shell/WindowBridge", @interface: "org.daisyos.Shell.WindowBridge",
                    member: "RequestShellAction", signature: "s");
                writer.WriteString(action);
                message = writer.CreateMessage();
            }
            await connection.CallMethodAsync(message).WaitAsync(TimeSpan.FromSeconds(3));
            return true;
        }
        catch (Exception ex) { Console.Error.WriteLine($"[files] Shell request failed: {ex.Message}"); return false; }
    }
}
