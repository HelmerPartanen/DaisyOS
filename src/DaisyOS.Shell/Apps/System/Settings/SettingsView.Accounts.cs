using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.System.Accounts;

namespace DaisyOS.Shell.Apps.System.Settings;

public partial class SettingsView
{
    internal IUserAccountService AccountService { get; set; } = new LinuxUserAccountService();
    public Task AccountPageReady { get; private set; } = Task.CompletedTask;
    private CancellationTokenSource? _accountLoad;
    private StackPanel? _accountHost;
    private bool _accountWriteBusy;

    private void BuildAccountSettings(int version)
    {
        _accountLoad?.Dispose();
        _accountLoad = CancellationTokenSource.CreateLinkedTokenSource(_displayLifetime.Token);
        var host = _accountHost = new StackPanel { Spacing = 24 };
        host.Children.Add(Secondary(Loc.Get("Reading account details…")));
        CategoryContent.Children.Add(host);
        AccountPageReady = LoadAccountSettingsAsync(host, version, _accountLoad.Token);
    }

    private async Task LoadAccountSettingsAsync(StackPanel host, int version, CancellationToken token)
    {
        try
        {
            var result = await AccountService.GetCurrentAccountAsync(token);
            if (token.IsCancellationRequested || version != _categoryBuildVersion) return;
            if (result.Account is { } account) RenderAccountSettings(host, account, version);
            else
            {
                host.Children.Clear();
                host.Children.Add(Secondary(Loc.Get(result.Error ?? "Account settings are unavailable right now. Try again.")));
                var retry = new Button { Content = Loc.Get("Try again"), Classes = { "Secondary", "Small" }, HorizontalAlignment = HorizontalAlignment.Left };
                retry.Click += (_, _) => AccountPageReady = LoadAccountSettingsAsync(host, version, token);
                host.Children.Add(retry);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (version == _categoryBuildVersion && !token.IsCancellationRequested)
            {
                host.Children.Clear();
                host.Children.Add(Secondary(Loc.Get("Account settings are unavailable right now. Try again.")));
            }
        }
    }

    private void RenderAccountSettings(StackPanel host, UserAccount account, int version, string? savedMessage = null)
    {
        host.Children.Clear();
        host.IsEnabled = !_accountWriteBusy;
        var status = Secondary(savedMessage ?? "");
        status.Name = "AccountChangeStatus";
        status.IsVisible = savedMessage is not null;
        async Task<bool> SaveAsync(Func<Task<AccountChangeResult>> operation)
        {
            if (_accountWriteBusy) return false;
            _accountWriteBusy = true;
            host.IsEnabled = false;
            try
            {
                var result = await operation();
                if (version != _categoryBuildVersion) return result.Succeeded;
                if (result.Succeeded)
                {
                    var fresh = await AccountService.GetCurrentAccountAsync(_displayLifetime.Token);
                    if (version != _categoryBuildVersion) return true;
                    if (fresh.Account is { } updated)
                        RenderAccountSettings(host, updated, version, Loc.Get("Account settings saved."));
                    else
                    {
                        status.Text = Loc.Get("The change was saved, but account details could not be refreshed.");
                        status.IsVisible = true;
                    }
                    return true;
                }
                else
                {
                    status.Text = Loc.Get(result.Error ?? "The account change was not completed. Check authorization and try again.");
                    status.IsVisible = true;
                    return false;
                }
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception)
            {
                // Do not log exceptions from operations carrying account credentials.
                if (version == _categoryBuildVersion)
                {
                    status.Text = Loc.Get("The account change was not completed. Check authorization and try again.");
                    status.IsVisible = true;
                }
                return false;
            }
            finally
            {
                _accountWriteBusy = false;
                if (_accountHost is { } current)
                {
                    current.IsEnabled = true;
                    if (!ReferenceEquals(current, host) && _activeCategory == "Password" && _accountLoad is { IsCancellationRequested: false } load)
                        AccountPageReady = LoadAccountSettingsAsync(current, _categoryBuildVersion, load.Token);
                }
            }
        }

        var name = AccountTextInput("AccountFullName", account.FullName, width: 220);
        name.MaxLength = 128;
        var saveName = new Button { Name = "SaveAccountName", Content = Loc.Get("Save"), Classes = { "Secondary", "Small" },
            VerticalAlignment = VerticalAlignment.Center, IsEnabled = false };
        name.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) saveName.IsEnabled = !string.IsNullOrWhiteSpace(name.Text) && name.Text!.Trim() != account.FullName; };
        saveName.Click += async (_, _) => await SaveAsync(() => AccountService.SetFullNameAsync(name.Text ?? "", _displayLifetime.Token));
        var nameEditor = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { name, saveName } };
        host.Children.Add(CreateSection(Loc.Get("ACCOUNT"), null,
            InfoRow(Loc.Get("User name"), account.UserName),
            PreferenceRow(Loc.Get("Full name"), null, nameEditor),
            InfoRow(Loc.Get("Account type"), Loc.Get(account.IsAdministrator ? "Administrator" : "Standard user"))));

        var password = AccountTextInput("AccountNewPassword", "");
        password.PasswordChar = '●';
        password.IsUndoEnabled = false;
        password.DetachedFromVisualTree += (_, _) => password.Text = "";
        password.MaxLength = 512;
        var repeat = AccountTextInput("AccountConfirmPassword", "");
        repeat.PasswordChar = '●';
        repeat.IsUndoEnabled = false;
        repeat.DetachedFromVisualTree += (_, _) => repeat.Text = "";
        repeat.MaxLength = 512;
        var passwordHelp = Secondary(Loc.Get("Use at least 8 characters."));
        var savePassword = new Button { Name = "SaveAccountPassword", Content = Loc.Get(account.PasswordMode == 2 ? "Set password" : "Change password"),
            Classes = { "Primary", "Small" }, HorizontalAlignment = HorizontalAlignment.Right, IsEnabled = false };
        void ValidatePassword()
        {
            var matches = password.Text == repeat.Text;
            savePassword.IsEnabled = account.IsLocalAccount && password.Text is { Length: >= 8 } && matches;
            passwordHelp.Text = Loc.Get(!string.IsNullOrEmpty(repeat.Text) && !matches ? "The passwords do not match." : "Use at least 8 characters.");
        }
        password.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ValidatePassword(); };
        repeat.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ValidatePassword(); };
        savePassword.Click += async (_, _) =>
        {
            if (!savePassword.IsEnabled) return;
            var value = password.Text ?? "";
            password.Text = repeat.Text = "";
            await SaveAsync(() => AccountService.SetPasswordAsync(value,
                (Application.Current as App)?.GetPreference("security.password-hint", "") ?? "", _displayLifetime.Token));
        };
        var passwordActions = new StackPanel { Spacing = 8, Margin = new Thickness(20, 12), Children = { passwordHelp, savePassword } };
        var signInRows = new List<Control>
        {
            InfoRow(Loc.Get("Password"), Loc.Get(account.PasswordMode switch
            {
                2 => "No password is set", 1 => "A password must be set at next sign-in", _ => "A password is set"
            }))
        };
        if (account.IsLocalAccount)
        {
            signInRows.Add(AccountInputRow(Loc.Get("New password"), password));
            signInRows.Add(AccountInputRow(Loc.Get("Confirm password"), repeat));
            signInRows.Add(passwordActions);
        }
        else signInRows.Add(InfoRow(Loc.Get("Password management"), Loc.Get("Your sign-in provider manages this account’s password.")));

        var automatic = new ToggleSwitch { Name = "AccountAutomaticLogin", IsChecked = account.AutomaticLogin, VerticalAlignment = VerticalAlignment.Center };
        automatic.Click += async (_, _) =>
        {
            var requested = automatic.IsChecked == true;
            if (!await SaveAsync(() => AccountService.SetAutomaticLoginAsync(requested, _displayLifetime.Token)))
                automatic.IsChecked = account.AutomaticLogin;
        };
        signInRows.Add(PreferenceRow(Loc.Get("Automatic sign-in"), Loc.Get("Sign in to this account without entering a password when the computer starts."), automatic));
        if (account.IsLocalAccount && account.PasswordMode != 2)
        {
            var remove = new Button { Name = "RemoveAccountPassword", Content = Loc.Get("Remove password"), Classes = { "Danger", "Small" }, HorizontalAlignment = HorizontalAlignment.Left };
            var warning = Secondary(Loc.Get("Removing your password allows access to this account without a password."));
            warning.IsVisible = false;
            var confirm = new Button { Name = "ConfirmRemoveAccountPassword", Content = Loc.Get("Remove password"), Classes = { "Danger", "Small" } };
            var cancel = new Button { Content = Loc.Get("Cancel"), Classes = { "Secondary", "Small" } };
            var confirmation = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, IsVisible = false, Children = { cancel, confirm } };
            remove.Click += (_, _) => { remove.IsVisible = false; warning.IsVisible = confirmation.IsVisible = true; };
            cancel.Click += (_, _) => { remove.IsVisible = true; warning.IsVisible = confirmation.IsVisible = false; };
            confirm.Click += async (_, _) => await SaveAsync(() => AccountService.RemovePasswordAsync(_displayLifetime.Token));
            var removal = new StackPanel { Spacing = 8, Margin = new Thickness(20, 12), Children = { remove, warning, confirmation } };
            signInRows.Add(removal);
        }
        host.Children.Add(CreateSection(Loc.Get("SIGN-IN"), Loc.Get("You may be asked to authenticate before account changes are saved."), signInRows.ToArray()));
        host.Children.Add(status);
    }

    private static TextBox AccountTextInput(string name, string text, double width = 320) => new()
    {
        Name = name, Text = text, Width = width, Classes = { "SettingsTextInput" },
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private Border AccountInputRow(string title, TextBox input)
    {
        var row = PreferenceRow(title, null, input);
        row.SizeChanged += (_, _) => input.MaxWidth = Math.Max(0, row.Bounds.Width - row.Padding.Left - row.Padding.Right);
        return row;
    }
}
