using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DaisyOS.Shell.Controls;

namespace DaisyOS.Shell.Apps.Mail;

public partial class MailComposer : UserControl
{
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(3) };
    private MailService? _service;
    private MailStorage? _storage;
    private MailAccount? _account;
    private MailDraft? _draft;
    private bool _loading;
    private bool _dirty;
    private bool _sending;
    public bool IsOpen => _draft is not null;
    public bool IsSending => _sending;
    public event Action? Dismissed;
    public event Action? DraftSaved;
    public event Action<string>? Sent;

    public MailComposer()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, ComposerKeyDown, RoutingStrategies.Tunnel);
        foreach (var field in new[] { ToField, CcField, BccField, SubjectField, ComposeBody })
            field.TextChanged += (_, _) => { if (!_loading && IsOpen) { _dirty = true; DraftStatus.Text = "Saving…"; Error(null); } };
        _autosave.Tick += (_, _) => { if (!_sending && _dirty) TrySave(); };
        DetachedFromVisualTree += (_, _) => _autosave.Stop();
    }
    public void Open(MailService service, MailStorage storage, MailAccount account, MailDraft draft, string heading)
    {
        _service = service; _storage = storage; _account = account; _draft = draft;
        draft.SeparateLegacyQuote();
        _loading = true;
        ToField.Text = draft.To; CcField.Text = draft.Cc; BccField.Text = draft.Bcc;
        SubjectField.Text = draft.Subject; ComposeBody.Text = draft.Body;
        ComposeBody.SelectionStart = ComposeBody.SelectionEnd = ComposeBody.CaretIndex = draft.Body.Length;
        CopyRow.IsVisible = draft.Cc.Length > 0; HiddenRow.IsVisible = draft.Bcc.Length > 0;
        QuoteToggle.IsVisible = !string.IsNullOrWhiteSpace(draft.QuotedBody);
        QuoteToggle.Content = "Show original message"; QuotePreview.IsVisible = false; QuotedText.Text = draft.QuotedBody;
        Heading.Text = heading; FromLabel.Text = "From " + account.Email;
        _dirty = false; _loading = false; Error(null); DraftStatus.Text = "Draft";
        UpdateAttachments(); _autosave.Start();
        Dispatcher.UIThread.Post(() => { if (IsOpen) { if (string.IsNullOrWhiteSpace(draft.To)) ToField.Focus(); else ComposeBody.Focus(); } }, DispatcherPriority.Loaded);
    }
    private void Capture()
    {
        if (_draft is null || _account is null) return;
        _draft.AccountId = _account.Id; _draft.To = ToField.Text ?? ""; _draft.Cc = CcField.Text ?? "";
        _draft.Bcc = BccField.Text ?? ""; _draft.Subject = SubjectField.Text ?? ""; _draft.Body = ComposeBody.Text ?? "";
    }
    private bool TrySave(bool force = false)
    {
        if (_draft is null || _storage is null || !_dirty && !force) return true;
        try
        {
            Capture();
            var drafts = _storage.Read("drafts", MailJsonContext.Default.ListMailDraft, []);
            drafts.RemoveAll(d => d.Id == _draft.Id); drafts.Add(_draft);
            _storage.Write("drafts", drafts, MailJsonContext.Default.ListMailDraft);
            _dirty = false; DraftStatus.Text = "Saved"; DraftSaved?.Invoke(); return true;
        }
        catch { Error("Couldn’t save your draft. Keep Mail open and try again."); return false; }
    }
    public bool TryClose()
    {
        if (_sending) return false;
        if (!IsOpen) return true;
        if (!TrySave()) return false;
        Finish(); return true;
    }
    private void Finish()
    {
        _autosave.Stop(); _draft = null; _account = null;
        _loading = true;
        ToField.Text = CcField.Text = BccField.Text = SubjectField.Text = ComposeBody.Text = QuotedText.Text = "";
        _loading = false; AttachmentChips.Children.Clear(); Dismissed?.Invoke();
    }
    private void Error(string? message) { ComposeError.Text = message; ComposeError.IsVisible = !string.IsNullOrWhiteSpace(message); }
    private void CloseClicked(object? sender, RoutedEventArgs e) => TryClose();
    private void CopyRecipientsClicked(object? sender, RoutedEventArgs e) { CopyRow.IsVisible = true; Dispatcher.UIThread.Post(() => CcField.Focus(), DispatcherPriority.Background); }
    private void HiddenRecipientsClicked(object? sender, RoutedEventArgs e) { HiddenRow.IsVisible = true; Dispatcher.UIThread.Post(() => BccField.Focus(), DispatcherPriority.Background); }
    private void QuoteClicked(object? sender, RoutedEventArgs e)
    {
        QuotePreview.IsVisible = !QuotePreview.IsVisible;
        QuoteToggle.Content = QuotePreview.IsVisible ? "Hide original message" : "Show original message";
    }
    private void ComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { TryClose(); e.Handled = true; }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S) { TrySave(); e.Handled = true; }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Enter) { _ = SendAsync(); e.Handled = true; }
    }
    private void UpdateAttachments()
    {
        AttachmentChips.Children.Clear();
        if (_draft is null) return;
        foreach (var path in _draft.Attachments.ToArray())
        {
            var button = new Button { Content = Path.GetFileName(path) + "  ×", MaxWidth = 220, Classes = { "Subtle", "Small", "Flexible" } };
            ToolTip.SetTip(button, "Remove " + Path.GetFileName(path));
            button.Click += (_, _) => { if (_sending || _draft is null) return; _draft.Attachments.Remove(path); _dirty = true; UpdateAttachments(); };
            AttachmentChips.Children.Add(button);
        }
    }
    private async void AttachClicked(object? sender, RoutedEventArgs e)
    {
        if (_sending || _draft is not { } draft || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Attach files", AllowMultiple = true });
            if (_draft != draft) return;
            foreach (var file in files)
                if (file.TryGetLocalPath() is { } path && !draft.Attachments.Contains(path)) { draft.Attachments.Add(path); _dirty = true; }
            UpdateAttachments();
        }
        catch { Error("Couldn’t attach these files. Please try again."); }
    }
    private void DiscardClicked(object? sender, RoutedEventArgs e)
    {
        if (_sending || _draft is null || _storage is null) return;
        try
        {
            var drafts = _storage.Read("drafts", MailJsonContext.Default.ListMailDraft, []);
            drafts.RemoveAll(d => d.Id == _draft.Id); _storage.Write("drafts", drafts, MailJsonContext.Default.ListMailDraft);
            Finish(); DraftSaved?.Invoke();
        }
        catch { Error("Couldn’t discard this draft. Please try again."); }
    }
    private async void SendClicked(object? sender, RoutedEventArgs e) => await SendAsync();
    private async Task SendAsync()
    {
        if (_sending || _draft is not { } draft || _service is null || _storage is null || _account is null) return;
        var attempted = false;
        try
        {
            Capture();
            if (string.IsNullOrWhiteSpace(draft.To) && string.IsNullOrWhiteSpace(draft.Cc) && string.IsNullOrWhiteSpace(draft.Bcc)) { Error("Add an email address in To."); ToField.Focus(); return; }
            try { _ = MailService.BuildMessage(_account, draft); }
            catch (FormatException) { Error("Check the email addresses. Separate multiple addresses with commas."); return; }
            if (!TrySave(force: true)) return;
            _sending = true; SetEditing(false); Error(null); DraftStatus.Text = "Sending…";
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            attempted = true;
            var warning = await _service.SendAsync(_account, draft, timeout.Token);
            // SMTP acceptance is final even if removing the local draft fails.
            _sending = false;
            try
            {
                var drafts = _storage.Read("drafts", MailJsonContext.Default.ListMailDraft, []);
                drafts.RemoveAll(d => d.Id == draft.Id); _storage.Write("drafts", drafts, MailJsonContext.Default.ListMailDraft);
            }
            catch { warning = "Message sent. Its saved draft could not be removed; do not resend it."; }
            Finish(); Sent?.Invoke(warning ?? "Message sent."); DraftSaved?.Invoke();
        }
        catch { Error(attempted ? "Couldn’t finish sending. Check Sent before trying again if your connection was interrupted." : "Couldn’t prepare this message. Check the addresses and attachments."); DraftStatus.Text = "Saved"; }
        finally { _sending = false; SetEditing(true); }
    }
    private void SetEditing(bool enabled)
    {
        AddressFields.IsEnabled = ComposeBody.IsEnabled = AttachButton.IsEnabled = AttachmentChips.IsEnabled = SendButton.IsEnabled = DiscardButton.IsEnabled = CloseComposer.IsEnabled = enabled;
    }
}
