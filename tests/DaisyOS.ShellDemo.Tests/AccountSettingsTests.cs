using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.System.Accounts;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class AccountSettingsTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeAccountFieldsValidateAndSaveWithoutLeavingSettings(bool dark)
    {
        var service = new AccountStub();
        var view = new SettingsView { AccountService = service };
        var owner = new Window { Content = view, Width = 1000, Height = 1000, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        owner.Show();
        try
        {
            view.SelectCategory("Password"); await view.AccountPageReady;
            var name = Find<TextBox>(view, "AccountFullName");
            Assert.Equal("Test User", name.Text);
            name.Text = "Updated User";
            Find<Button>(view, "SaveAccountName").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => service.Current.FullName == "Updated User");
            var password = Find<TextBox>(view, "AccountNewPassword");
            var repeat = Find<TextBox>(view, "AccountConfirmPassword");
            var save = Find<Button>(view, "SaveAccountPassword");
            Assert.Equal('●', password.PasswordChar);
            Assert.False(password.IsUndoEnabled);
            Assert.False(repeat.IsUndoEnabled);
            password.Text = "short"; repeat.Text = "short";
            Assert.False(save.IsEnabled);
            password.Text = "Test-password-123"; repeat.Text = "Mismatch";
            Assert.False(save.IsEnabled);
            repeat.Text = password.Text;
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => service.PasswordWrites == 1);
            Assert.Equal("", password.Text);
            Assert.Equal("", repeat.Text);
            Assert.Equal("Account settings saved.", Find<TextBlock>(view, "AccountChangeStatus").Text);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), text => (text.Text ?? "").Contains("KDE"));
            owner.UpdateLayout();
            owner.CaptureRenderedFrame()!.Save($"/tmp/daisy-native-account-{dark}.png");
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task RejectedAutomaticLoginRestoresTheActualValueAndPasswordRemovalRequiresConfirmation()
    {
        var service = new AccountStub { RejectChanges = true };
        var view = new SettingsView { AccountService = service };
        var owner = new Window { Content = view, Width = 1000, Height = 1000 };
        owner.Show();
        try
        {
            view.SelectCategory("Password"); await view.AccountPageReady;
            var automatic = Find<ToggleSwitch>(view, "AccountAutomaticLogin");
            automatic.IsChecked = true;
            automatic.RaiseEvent(new RoutedEventArgs(ToggleSwitch.ClickEvent));
            await WaitFor(() => service.AutomaticWrites == 1);
            Assert.False(automatic.IsChecked);
            Assert.False(service.Current.AutomaticLogin);
            Assert.Contains("not completed", Find<TextBlock>(view, "AccountChangeStatus").Text);
            service.RejectChanges = false;
            Find<Button>(view, "RemoveAccountPassword").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, service.Removals);
            Find<Button>(view, "ConfirmRemoveAccountPassword").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => service.Removals == 1);
            Assert.Equal(2, service.Current.PasswordMode);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task UnavailableAccountServiceCanBeRetriedAndLeavingThePageClearsPasswordFields()
    {
        var service = new AccountStub
        {
            NextRead = Task.FromResult(new AccountReadResult(null, "Account settings are unavailable right now. Try again."))
        };
        var view = new SettingsView { AccountService = service };
        var owner = new Window { Content = view, Width = 900, Height = 900 };
        owner.Show();
        try
        {
            view.SelectCategory("Password"); await view.AccountPageReady;
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBox>(), input => input.Name == "AccountNewPassword");
            Assert.NotNull(Find<TextBox>(view, "PasswordHint"));
            view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Try again"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.AccountPageReady;
            var password = Find<TextBox>(view, "AccountNewPassword");
            var repeat = Find<TextBox>(view, "AccountConfirmPassword");
            password.Text = repeat.Text = "Unsaved-test-password";
            view.SelectCategory("Notifications");
            Assert.Equal("", password.Text);
            Assert.Equal("", repeat.Text);
            Assert.Equal(0, service.PasswordWrites);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task LateAccountReadCannotPopulateAnotherCategory()
    {
        var pending = new TaskCompletionSource<AccountReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new AccountStub { NextRead = pending.Task };
        var view = new SettingsView { AccountService = service };
        var owner = new Window { Content = view };
        owner.Show();
        try
        {
            view.SelectCategory("Password");
            var loading = view.AccountPageReady;
            view.SelectCategory("Notifications");
            pending.SetResult(new(service.Current));
            await loading;
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBox>(), input => input.Name == "AccountFullName");
        }
        finally { owner.Close(); }
    }

    private static T Find<T>(Control view, string name) where T : Control => view.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class AccountStub : IUserAccountService
    {
        public UserAccount Current = new("test-user", "Test User", false, 0, false, true);
        public bool RejectChanges;
        public int PasswordWrites, AutomaticWrites, Removals;
        public Task<AccountReadResult>? NextRead;
        public Task<AccountReadResult> GetCurrentAccountAsync(CancellationToken cancellationToken = default)
        {
            var next = NextRead; NextRead = null;
            return next ?? Task.FromResult(new AccountReadResult(Current));
        }
        public Task<AccountChangeResult> SetFullNameAsync(string name, CancellationToken cancellationToken = default)
        { Current = Current with { FullName = name }; return Task.FromResult(new AccountChangeResult(true)); }
        public Task<AccountChangeResult> SetPasswordAsync(string password, string hint, CancellationToken cancellationToken = default)
        { PasswordWrites++; return Task.FromResult(new AccountChangeResult(true)); }
        public Task<AccountChangeResult> RemovePasswordAsync(CancellationToken cancellationToken = default)
        { Removals++; Current = Current with { PasswordMode = 2 }; return Task.FromResult(new AccountChangeResult(true)); }
        public Task<AccountChangeResult> SetAutomaticLoginAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            AutomaticWrites++;
            if (RejectChanges) return Task.FromResult(new AccountChangeResult(false, "The account change was not completed. Check authorization and try again."));
            Current = Current with { AutomaticLogin = enabled };
            return Task.FromResult(new AccountChangeResult(true));
        }
    }
}
