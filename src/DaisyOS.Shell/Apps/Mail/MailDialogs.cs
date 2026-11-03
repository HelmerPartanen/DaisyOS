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
    internal static TextBox Field(StackPanel panel, string label, string text = "")
    {
        var group = new StackPanel(); MailDialogLayout.Token(group, StackPanel.SpacingProperty, "Space1");
        var caption = MailDialogLayout.Note(label); group.Children.Add(caption);
        var box = new TextBox { Name = new string(label.Where(char.IsLetterOrDigit).ToArray()) + "Field", Text = text };
        AutomationProperties.SetName(box, label);
        group.Children.Add(box); panel.Children.Add(group); return box;
    }
    public static async Task<MailAccount?> AddAccountAsync(Window owner, MailService service, Func<MailAccount, Task> save)
    {
        var dialog = Window("Add account", height: 560); FollowTheme(dialog, owner);
        var setup = new MailAccountSetup { Service = service };
        setup.Connected = async account => { await save(account); dialog.Close(account); };
        dialog.Content = setup;
        dialog.Closed += (_, _) => setup.Cancel();
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
