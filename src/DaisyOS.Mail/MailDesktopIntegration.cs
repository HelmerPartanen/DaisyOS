using DaisyOS.Shell.Apps.Mail;
using DaisyOS.Shell.Services;
using DaisyOS.System.Processes;

namespace DaisyOS.Mail;

internal static class MailDesktopIntegration
{
    public static async Task<int> PinAsync()
    {
        const string id = "org.daisyos.mail";
        DesktopApplicationMaskStore.Restore(id + ".desktop");
        try
        {
            var response = await ApplicationPipe.RequestAsync(ApplicationPipe.Name("desktop-settings"), ["pin", id]);
            if (response != "ok") throw new IOException(response);
            Console.WriteLine("Mail pinned to the running taskbar.");
            try { await ApplicationPipe.RequestAsync(ApplicationPipe.Name("desktop-settings"), ["refresh-applications"]); } catch { }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        {
            var state = new ShellSessionState();
            state.SetDockOrder(state.DockOrder.Append(id));
            Console.WriteLine("Mail pinned for the next desktop session; no active settings service was reachable.");
            return 0;
        }
    }
    public static int CheckSignIn()
    {
        try
        {
            var config = MailProviderRegistration.Load();
            Console.WriteLine("Google registration: " + (config.GoogleConfigured ? "configured" : "missing or invalid"));
            Console.WriteLine("Microsoft registration: " + (config.MicrosoftConfigured ? "configured" : "missing or invalid"));
            Console.WriteLine("Provider consent and live mailbox access must still be verified with real accounts.");
            return config.GoogleConfigured && config.MicrosoftConfigured ? 0 : 2;
        }
        catch (Exception ex) when (ex is IOException or global::System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Mail provider registration could not be loaded. Check the distributor configuration file.");
            return 2;
        }
    }
}
