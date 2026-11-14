using DaisyOS.Shell.Apps.AppStore;
using DaisyOS.Shell.Services;
namespace DaisyOS.AppStore;
internal static class Program {
    [STAThread] public static int Main(string[] args) => StandaloneApplication.Run("appstore", args,
        app => { app.UseDesktopSettings(); return new AppStoreWindow(); }, (_, _) => { });
}
