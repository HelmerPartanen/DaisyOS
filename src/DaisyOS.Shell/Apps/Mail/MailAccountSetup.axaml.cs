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
        try
        {
            account = MailOAuth.CreateAccount(provider);
            Providers.IsEnabled = false; CancelSignIn.IsVisible = true;
            ShowError("Complete sign-in in your browser. You can return to Mail when you’re done.");
            await Service.ValidateAsync(account, "", stop.Token);
            if (Connected is null) throw new InvalidOperationException("Mail could not finish adding this account. Please try again.");
            await Connected(account);
            connected = true;
            SignInStatus.IsVisible = false;
        }
        catch (OperationCanceledException) { ShowError("Sign-in cancelled. Choose a provider to try again."); }
        catch (MailSignInUnavailableException ex) { ShowError(ex.Message); }
        catch { ShowError("Couldn’t connect your account. Please try again. Your school or workplace may need to allow access to Mail."); }
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
