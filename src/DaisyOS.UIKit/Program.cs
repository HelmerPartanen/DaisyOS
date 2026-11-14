using DaisyOS.Shell.Apps.UIKit;
using DaisyOS.Shell.Services;
namespace DaisyOS.UIKit;
internal static class Program {
    [STAThread] public static int Main(string[] args) => StandaloneApplication.Run("uikit", args,
        app => { app.UseDesktopSettings(); return new UIKitWindow(); }, (_, _) => { });
}
