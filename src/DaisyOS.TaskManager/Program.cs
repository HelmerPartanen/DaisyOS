using DaisyOS.Shell.Apps.System.Diagnostics;
using DaisyOS.Shell.Services;

namespace DaisyOS.TaskManager;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => StandaloneApplication.Run("taskmanager", args,
        _ => TaskManagerWindow.CreateStandalone(), (_, _) => { });
}
