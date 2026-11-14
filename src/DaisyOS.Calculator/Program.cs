using DaisyOS.Shell.Apps.Calculator;
using DaisyOS.Shell.Services;
namespace DaisyOS.Calculator;
internal static class Program {
    [STAThread] public static int Main(string[] args) => StandaloneApplication.Run("calculator", args,
        app => { app.UseDesktopSettings(); return new CalculatorWindow(); }, (_, _) => { });
}
