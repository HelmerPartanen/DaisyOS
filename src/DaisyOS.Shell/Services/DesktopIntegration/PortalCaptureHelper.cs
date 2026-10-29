namespace DaisyOS.Shell.Services.DesktopIntegration;

internal static class PortalCaptureHelper
{
    // Continuity publishes the versioned helper too, so later helper updates do
    // not depend on which standalone app directory hosts desktop services.
    public static string Path
    {
        get
        {
            var paths = new[]
            {
                global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "lib", "daisyos", "continuity", "daisyos-portal-capture"),
                global::System.IO.Path.Combine(AppContext.BaseDirectory, "daisyos-portal-capture"),
                "/usr/lib/daisyos/apps/Continuity/daisyos-portal-capture"
            };
            return paths.FirstOrDefault(File.Exists) ?? paths[1];
        }
    }
}
