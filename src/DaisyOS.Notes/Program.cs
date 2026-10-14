using DaisyOS.Shell.Apps.Notes;
using DaisyOS.Shell.Services;

namespace DaisyOS.Notes;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => StandaloneApplication.Run("notes", args,
        _ => new NotesWindow(), (window, activation) =>
        {
            if (activation.Contains("--new-note")) ((NotesWindow)window).CreateNewNote();
        });
}
