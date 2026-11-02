using DaisyOS.Shell.Apps.Mail;
using DaisyOS.Shell.Services;

namespace DaisyOS.Mail;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => StandaloneApplication.Run("mail", args, _ => new MailWindow(), (window, activation) =>
        {
            foreach (var link in activation.Where(a => a.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)))
                Avalonia.Threading.Dispatcher.UIThread.Post(async () => await ((MailWindow)window).OpenMailtoAsync(link), Avalonia.Threading.DispatcherPriority.Background);
        });
}
