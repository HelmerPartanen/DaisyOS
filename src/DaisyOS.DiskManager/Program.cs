using DaisyOS.Shell.Apps.System.Disks;
using DaisyOS.Shell.Services;

namespace DaisyOS.DiskManager;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => StandaloneApplication.Run("diskmanager", args,
        _ => new DiskManagerWindow(), (_, _) => { });
}
