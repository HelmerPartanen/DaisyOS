using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using MimeKit;
using Avalonia.Automation;
using DaisyOS.Shell.Controls;

namespace DaisyOS.Shell.Apps.Mail;

internal static class MailDialogs
{
    internal static Window Window(string title, int width = 540, int height = 650)
    {
        var window = new Window
        {
            Title = title, Width = width, Height = height, MinWidth = 440, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        window.Bind(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, window.GetResourceObservable("SurfaceBaseSolidBrush"));
        window.Bind(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, window.GetResourceObservable("ContentPrimaryBrush"));
        return window;
    }
    internal static void FollowTheme(Window dialog, Window owner)
    {
        var binding = dialog.Bind(TopLevel.RequestedThemeVariantProperty, owner.GetObservable(TopLevel.ActualThemeVariantProperty));
        dialog.Closed += (_, _) => binding.Dispose();
    }
    internal static TextBox Field(StackPanel panel, string label, string text = "", bool password = false)
    {
        var group = new StackPanel(); MailDialogLayout.Token(group, StackPanel.SpacingProperty, "Space1");
        var caption = MailDialogLayout.Note(label); group.Children.Add(caption);
        var box = new TextBox { Name = new string(label.Where(char.IsLetterOrDigit).ToArray()) + "Field", Text = text, PasswordChar = password ? '●' : '\0' };
        AutomationProperties.SetName(box, label);
        group.Children.Add(box); panel.Children.Add(group); return box;
    }
    public static async Task<MailAccount?> AddAccountAsync(Window owner, MailService service)
    {
        var dialog = Window("Add mail account", height: 720); FollowTheme(dialog, owner);
        var layout = new MailDialogLayout(dialog, "Add account"); var panel = layout.Form;
        panel.Children.Add(MailDialogLayout.Note("Choose Google, Microsoft, or your provider’s IMAP settings."));
        var provider = new ComboBox { ItemsSource = new[] { "Google", "Microsoft", "IMAP" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(provider);
        var email = Field(panel, "Email address"); var name = Field(panel, "Your name");
        var oauthFields = MailDialogLayout.Stack(); panel.Children.Add(oauthFields);
        var client = Field(oauthFields, "OAuth client ID", Environment.GetEnvironmentVariable("DAISYOS_MAIL_GOOGLE_CLIENT_ID") ?? "");
        var passwordFields = MailDialogLayout.Stack(); panel.Children.Add(passwordFields);
        var password = Field(passwordFields, "App password / password", password: true);
        var servers = MailDialogLayout.Stack();
        var incoming = Field(servers, "Incoming IMAP server (TLS)", "imap.gmail.com"); var imapPort = Field(servers, "IMAP port", "993");
        var outgoing = Field(servers, "Outgoing SMTP server", "smtp.gmail.com"); var smtpPort = Field(servers, "SMTP port (587 STARTTLS or 465 TLS)", "587");
        var advanced = new Expander { Header = "Server settings", Content = servers, HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(advanced);
        var note = MailDialogLayout.Note(); note.Name = "AccountStatus"; panel.Children.Add(note);
        var cancel = MailDialogLayout.Action("Cancel"); cancel.IsCancel = true; layout.Actions.Children.Add(cancel);
        var connect = MailDialogLayout.Action("Continue", "Primary"); connect.Name = "ConnectAccount"; layout.Actions.Children.Add(connect);
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        cancel.Click += (_, _) => { stop.Cancel(); dialog.Close(); };
        dialog.Closed += (_, _) => stop.Cancel();
        void Update()
        {
            var p = provider.SelectedItem?.ToString(); var google = p == "Google";
            incoming.Text = p == "IMAP" ? "" : google ? "imap.gmail.com" : "outlook.office365.com";
            outgoing.Text = p == "IMAP" ? "" : google ? "smtp.gmail.com" : "smtp.office365.com";
            client.Text = Environment.GetEnvironmentVariable(google ? "DAISYOS_MAIL_GOOGLE_CLIENT_ID" : "DAISYOS_MAIL_MICROSOFT_CLIENT_ID") ?? "";
            oauthFields.IsVisible = p != "IMAP" && string.IsNullOrWhiteSpace(client.Text);
            passwordFields.IsVisible = p == "IMAP"; advanced.IsExpanded = p == "IMAP";
            connect.Content = p == "IMAP" ? "Connect account" : "Sign in with your browser";
            note.Text = p == "IMAP" ? "Ask your provider for mail server settings. Some providers require an app password." :
                string.IsNullOrWhiteSpace(client.Text) ? "Browser sign-in needs a registered OAuth client ID. Setup instructions: docs/Mail.md." : "Continue securely in your browser. Mail never sees your provider password.";
        }
        provider.SelectionChanged += (_, _) => Update(); Update();
        connect.Click += async (_, _) =>
        {
            MailAccount? account = null;
            try
            {
                email.Classes.Remove("Error");
                if (string.IsNullOrWhiteSpace(email.Text))
                {
                    email.Classes.Add("Error"); email.Focus(); note.Text = "Enter your email address."; return;
                }
                var address = MailboxAddress.Parse(email.Text ?? "");
                if (address.Address != email.Text?.Trim()) throw new FormatException("Enter an email address without a display name.");
                if (!int.TryParse(imapPort.Text, out var ip) || ip is < 1 or > 65535 || !int.TryParse(smtpPort.Text, out var sp) || sp is < 1 or > 65535) throw new FormatException("Enter valid server ports.");
                if (string.IsNullOrWhiteSpace(incoming.Text) || string.IsNullOrWhiteSpace(outgoing.Text)) throw new FormatException("Enter both mail servers.");
                account = new MailAccount { Email = address.Address, Name = name.Text ?? "", Provider = provider.SelectedItem!.ToString()!, ClientId = client.Text?.Trim() ?? "", ImapHost = incoming.Text.Trim(), ImapPort = ip, SmtpHost = outgoing.Text.Trim(), SmtpPort = sp };
                connect.IsEnabled = false; provider.IsEnabled = false; note.Text = "Connecting… Complete sign-in in your browser if prompted.";
                await service.ValidateAsync(account, password.Text ?? "", stop.Token);
                dialog.Close(account);
            }
            catch (Exception ex)
            {
                if (account is not null) { try { await MailSecrets.RunAsync("clear", account.Id, null, CancellationToken.None); } catch { } }
                note.Text = ex is OperationCanceledException ? "Sign-in cancelled or timed out." : ex.Message;
                connect.IsEnabled = true; provider.IsEnabled = true;
            }
        };
        return await dialog.ShowDialog<MailAccount?>(owner);
    }

    public static async Task ComposeAsync(Window owner, MailService service, MailStorage storage, MailAccount account, MailDraft draft, Action<string> status)
    {
        var dialog = Window("New message — " + account.Email, 760, 780); FollowTheme(dialog, owner);
        var layout = new MailDialogLayout(dialog, "New message"); var panel = layout.Form;
        panel.Children.Add(MailDialogLayout.Note("From " + account.Email));
        var to = Field(panel, "To", draft.To);
        var copies = new StackPanel(); MailDialogLayout.Token(copies, StackPanel.SpacingProperty, "Space3");
        var cc = Field(copies, "Cc", draft.Cc); var bcc = Field(copies, "Bcc", draft.Bcc);
        panel.Children.Add(new Expander { Header = "Cc and Bcc", Content = copies, IsExpanded = draft.Cc.Length > 0 || draft.Bcc.Length > 0, HorizontalAlignment = HorizontalAlignment.Stretch });
        var subject = Field(panel, "Subject", draft.Subject);
        var body = new TextBox { Name = "ComposeBody", Text = draft.Body, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 260 }; panel.Children.Add(body);
        var files = new ListBox { Classes = { "MailList" }, ItemsSource = draft.Attachments.ToArray(), MaxHeight = 110, IsVisible = draft.Attachments.Count > 0 }; panel.Children.Add(files);
        var attachments = new WrapPanel(); MailDialogLayout.Token(attachments, WrapPanel.ItemSpacingProperty, "Space2"); MailDialogLayout.Token(attachments, WrapPanel.LineSpacingProperty, "Space2"); panel.Children.Add(attachments);
        var attach = MailDialogLayout.Action("Attach files…"); attachments.Children.Add(attach);
        var remove = MailDialogLayout.Action("Remove selected", "Subtle"); remove.IsVisible = draft.Attachments.Count > 0; remove.IsEnabled = false; attachments.Children.Add(remove);
        files.SelectionChanged += (_, _) => remove.IsEnabled = files.SelectedItem is string;
        var discard = MailDialogLayout.Action("Discard", "Subtle"); layout.Actions.Children.Add(discard);
        var save = MailDialogLayout.Action("Save & close"); layout.Actions.Children.Add(save);
        var send = MailDialogLayout.Action("Send", "Primary"); layout.Actions.Children.Add(send);
        var error = MailDialogLayout.Note(); panel.Children.Add(error);
        var completed = false; var sending = false;
        void Save()
        {
            draft.AccountId = account.Id; draft.To = to.Text ?? ""; draft.Cc = cc.Text ?? ""; draft.Bcc = bcc.Text ?? ""; draft.Subject = subject.Text ?? ""; draft.Body = body.Text ?? "";
            var drafts = storage.Read("drafts", MailJsonContext.Default.ListMailDraft, []);
            drafts.RemoveAll(d => d.Id == draft.Id); drafts.Add(draft); storage.Write("drafts", drafts, MailJsonContext.Default.ListMailDraft);
        }
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { try { if (!sending) Save(); } catch (Exception ex) { error.Text = "Could not save draft: " + ex.Message; } }; timer.Start();
        dialog.Closing += (_, e) =>
        {
            if (sending) { e.Cancel = true; return; }
            if (!completed) { try { Save(); } catch (Exception ex) { e.Cancel = true; error.Text = "Could not save draft: " + ex.Message; } }
        };
        dialog.Closed += (_, _) => timer.Stop();
        save.Click += (_, _) => dialog.Close();
        attach.Click += async (_, _) =>
        {
            try
            {
                var selected = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Attach files", AllowMultiple = true });
                foreach (var f in selected) if (f.TryGetLocalPath() is { } path && !draft.Attachments.Contains(path)) draft.Attachments.Add(path);
                files.ItemsSource = draft.Attachments.ToArray(); files.IsVisible = remove.IsVisible = draft.Attachments.Count > 0;
            }
            catch (Exception ex) { error.Text = "Could not attach files: " + ex.Message; }
        };
        remove.Click += (_, _) => { if (files.SelectedItem is string path) draft.Attachments.Remove(path); files.ItemsSource = draft.Attachments.ToArray(); files.IsVisible = remove.IsVisible = draft.Attachments.Count > 0; };
        discard.Click += (_, _) =>
        {
            try
            {
                var drafts = storage.Read("drafts", MailJsonContext.Default.ListMailDraft, []);
                drafts.RemoveAll(d => d.Id == draft.Id); storage.Write("drafts", drafts, MailJsonContext.Default.ListMailDraft);
                completed = true; dialog.Close();
            }
            catch (Exception ex) { error.Text = "Could not discard draft: " + ex.Message; }
        };
        send.Click += async (_, _) =>
        {
            var attempted = false;
            try
            {
                Save();
                if (string.IsNullOrWhiteSpace(draft.To)) throw new FormatException("Add at least one recipient.");
                _ = MailService.BuildMessage(account, draft);
                sending = true; send.IsEnabled = false; save.IsEnabled = false; discard.IsEnabled = false; panel.IsEnabled = false;
                error.Text = "Sending…";
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                attempted = true;
                var warning = await service.SendAsync(account, draft, timeout.Token);
                // Once SMTP accepts a message, never offer Send again, including after local-storage failures.
                completed = true; sending = false;
                try { var drafts = storage.Read("drafts", MailJsonContext.Default.ListMailDraft, []); drafts.RemoveAll(d => d.Id == draft.Id); storage.Write("drafts", drafts, MailJsonContext.Default.ListMailDraft); }
                catch { warning = "Message sent. Its local draft could not be removed; do not resend it."; }
                status(warning ?? "Message sent."); dialog.Close();
            }
            catch (Exception ex) { sending = false; send.IsEnabled = true; save.IsEnabled = true; discard.IsEnabled = true; panel.IsEnabled = true; error.Text = ex.Message + (attempted ? " Check Sent before retrying if delivery was interrupted." : ""); }
        };
        await dialog.ShowDialog(owner);
    }
}
