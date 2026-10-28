using DaisyOS.Shell.Apps.Notes;
using DaisyOS.Shell.Services;

namespace DaisyOS.Notes;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => StandaloneApplication.Run("notes", args,
        _ => new NotesWindow(), (window, activation) =>
        {
            var handoff = Array.IndexOf(activation, "--continuity-import");
            if (handoff >= 0 && handoff + 1 < activation.Length)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ((NotesWindow)window).ImportContinuityActivity(activation[handoff + 1]), Avalonia.Threading.DispatcherPriority.Background);
            if (activation.Contains("--new-note")) ((NotesWindow)window).CreateNewNote();
        });
}
