using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaisyOS.Shell.Apps.Mail;

public partial class MailAccountSetup : UserControl
{
    public MailService? Service { get; set; }
    public Func<MailAccount, Task>? Connected { get; set; }
    private CancellationTokenSource? _signIn;

    public MailAccountSetup() => InitializeComponent();
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _signIn?.Cancel();
        base.OnDetachedFromVisualTree(e);
    }
    public void Cancel() => _signIn?.Cancel();
    public void ShowError(string message) { SignInStatus.Text = message; SignInStatus.IsVisible = true; }
    private async Task SignInAsync(string provider)
    {
        if (_signIn is not null || Service is null) return;
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _signIn = stop;
        MailAccount? account = null;
        var connected = false;
        var stage = "browser";
        try
        {
            account = MailOAuth.CreateAccount(provider);
            Providers.IsEnabled = false; CancelSignIn.IsVisible = true;
            ShowError("Complete sign-in in your browser. You can return to Mail when you’re done.");
            await Service.ValidateAsync(account, "", stop.Token, value =>
            {
                stage = value;
                ShowError(value == "incoming" ? "Connecting to your inbox…" :
                    value == "outgoing" ? "Checking sending…" : "Finishing sign-in…");
            });
            stage = "saving";
            if (Connected is null) throw new InvalidOperationException("Mail could not finish adding this account. Please try again.");
            await Connected(account);
            connected = true;
            SignInStatus.IsVisible = false;
        }
        catch (OperationCanceledException) { ShowError("Sign-in cancelled. Choose a provider to try again."); }
        catch (MailSignInUnavailableException ex) { ShowError(ex.Message); }
        catch (Exception ex)
        {
            // Record only our stage and exception type, never provider responses or credentials.
            try
            {
                var path = Path.Combine(Service.Storage.Root, "last-sign-in-error.txt");
                File.WriteAllText(path, $"{DateTimeOffset.UtcNow:O} stage={stage} type={ex.GetType().Name}\n");
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { }
            ShowError(stage switch
            {
                "keyring" => "Mail couldn’t save your sign-in. Unlock your system keyring and try again.",
                "incoming" => "Mail couldn’t open your inbox. Sign in again and allow access to read and send email. For a school or work account, your administrator may need to allow IMAP access.",
                "outgoing" => "Mail couldn’t connect to send email. Try again. For a school or work account, your administrator may need to allow SMTP access.",
                "identity" => "Mail couldn’t verify your account. Check your computer’s date and time, then try again.",
                "authorization" => "The provider couldn’t finish authorizing Mail. Please sign in again.",
                "saving" => "Mail couldn’t save your account. Check available disk space and try again.",
                _ => "Mail couldn’t complete browser sign-in. Please try again."
            });
        }
        finally
        {
            if (!connected && account is not null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await MailSecrets.RunAsync("clear", account.Id, null, cleanup.Token); } catch { }
            }
            Providers.IsEnabled = true; CancelSignIn.IsVisible = false; _signIn = null;
        }
    }
    private async void GoogleClicked(object? sender, RoutedEventArgs e) => await SignInAsync("Google");
    private async void MicrosoftClicked(object? sender, RoutedEventArgs e) => await SignInAsync("Microsoft");
    private void CancelClicked(object? sender, RoutedEventArgs e) => Cancel();
}
