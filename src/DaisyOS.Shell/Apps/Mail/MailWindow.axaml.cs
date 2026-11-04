using Avalonia;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Motion;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MailKit;
using MimeKit;
using MimeKit.Text;

namespace DaisyOS.Shell.Apps.Mail;

public partial class MailWindow : Window
{
    private readonly MailStorage _storage = new();
    private readonly MailService _service;
    private readonly SystemAppWindowChrome _chrome;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(45) };
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private List<MailAccount> _accounts = [];
    private List<MailItem> _items = [];
    private MimeMessage? _message;
    private MailDraft? _pendingDraft;
    private int _limit = 100;
    private string? _serverQuery;
    private int _generation;
    private bool _busy;
    private bool _plainText;
    private bool _htmlReady;
    private bool _ready;
    private bool _updatingMessages;
    private MailAccount? Account => Accounts.SelectedItem as MailAccount;
    private MailFolder? Folder => Folders.SelectedItem as MailFolder;
    private MailItem? Selected => Messages.SelectedItem as MailItem;

    public MailWindow()
    {
        InitializeComponent();
        _service = new MailService(_storage);
        _chrome = new SystemAppWindowChrome(this, Chrome, MainPaneLayout, normalRowDefinitions: "Auto,*", fullscreenRowDefinitions: "0,*");
        HtmlBody.Rendered += success =>
        {
            _htmlReady = success;
            Body.IsVisible = !success || _plainText;
            HtmlBody.IsVisible = success && !_plainText;
            if (!success && _message?.HtmlBody is not null) Status.Text = "Styled view unavailable. Showing message text.";
        };
        HtmlBody.LinkClicked += async value =>
        {
            try
            {
                if (!MailHtmlDocument.IsExternalLink(value)) return;
                if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) await OpenMailtoAsync(value);
                else global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo(value) { UseShellExecute = true });
            }
            catch { Status.Text = "Couldn’t open this link."; }
        };
        Composer.Dismissed += () => { Composer.IsVisible = false; ReaderPane.IsVisible = true; UpdateDraftAvailability(); };
        Composer.DraftSaved += UpdateDraftAvailability;
        Composer.Sent += value => Status.Text = value;
        Closing += (_, e) => { if (!Composer.TryClose()) e.Cancel = true; };
        AccountSetup.Service = _service;
        AccountSetup.Connected = SaveAccountAsync;
        Search.PropertyChanged += (_, change) =>
        {
            if (change.Property == SearchBar.TextProperty) SearchChanged();
        };
        Search.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || _busy) return;
            _serverQuery = Search.Text?.Trim(); _limit = 100;
            await RefreshAsync(); e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F5) { _ = RefreshAsync(); e.Handled = true; }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.N) { _ = ComposeAsync(); e.Handled = true; }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F) { Search.FocusInput(); e.Handled = true; }
        };
        Opened += async (_, _) =>
        {
            try
            {
                _accounts = _storage.Read("accounts", MailJsonContext.Default.ListMailAccount, []);
                _ready = true; Accounts.ItemsSource = _accounts; Accounts.SelectedIndex = _accounts.Count > 0 ? 0 : -1;
                UpdateAccountPresentation();
                _refresh.Start();
            }
            catch { AccountSetup.ShowError("Couldn’t load your accounts. Please close Mail and try again."); }
            await Task.CompletedTask;
        };
        _refresh.Tick += async (_, _) => { if (!_busy && Account is not null) await RefreshAsync(); };
        Closed += async (_, _) => { AccountSetup.Cancel(); _refresh.Stop(); _lifetime.Cancel(); _operation?.Cancel(); _chrome.Dispose(); HtmlBody.Dispose(); await _service.DisposeAsync(); _lifetime.Dispose(); };
    }
    private async Task RunAsync(string label, Func<CancellationToken, Task> action)
    {
        if (_busy) return;
        _busy = true; Status.Text = label; CancelOperation.IsVisible = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2)); _operation = timeout;
        try { await action(timeout.Token); }
        catch (OperationCanceledException) { Status.Text = "Cancelled or timed out. Cached messages are still available."; }
        catch (Exception ex) { Status.Text = "Could not complete the operation: " + ex.Message; }
        finally { _busy = false; _operation = null; CancelOperation.IsVisible = false; }
    }
    private void ClearReader()
    {
        HtmlBody.Clear(); HtmlBody.IsVisible = false; Body.IsVisible = true;
        _plainText = _htmlReady = false; TextViewToggle.IsVisible = false; TextViewToggle.Header = "View plain text";
        _message = null; MessageActions.IsEnabled = false; Attachments.Children.Clear();
        GuiMotion.Cancel(ReaderContent);
        MessageActions.IsVisible = false; Sender.IsVisible = false;
        ReaderContent.IsVisible = false;
        Subject.Text = Sender.Text = Body.Text = "";
    }
    private async void AccountChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _generation++; _operation?.Cancel();
        UpdateDraftAvailability();
        _items = []; ApplyFilter(); Folders.ItemsSource = null; ClearReader();
        var a = Account; if (a is null) return;
        // A cancelled request releases its connection before the next account starts.
        while (_busy && !_lifetime.IsCancellationRequested) await Task.Delay(25);
        if (Account != a || _lifetime.IsCancellationRequested) return;
        var cachedFolders = _storage.Read("folders-" + a.Id, MailJsonContext.Default.ListMailFolder, []);
        if (cachedFolders.Count > 0) { Folders.ItemsSource = cachedFolders; Folders.SelectedIndex = 0; }
        List<MailFolder>? folders = null;
        await RunAsync("Connecting to " + a.Email + "…", async ct => { folders = await _service.FoldersAsync(a, ct); });
        if (Account != a) return;
        if (folders is not null && cachedFolders.Count > 0 && folders.Select(x => x.Path).SequenceEqual(cachedFolders.Select(x => x.Path))) return;
        Folders.ItemsSource = folders ?? _storage.Read("folders-" + a.Id, MailJsonContext.Default.ListMailFolder, [new MailFolder("Inbox", "INBOX")]); Folders.SelectedIndex = 0;
    }
    private async void FolderChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _generation++; _operation?.Cancel(); _limit = 100; _serverQuery = null; Messages.SelectedItem = null; ClearReader();
        RefreshButton.IsEnabled = LoadOlderButton.IsEnabled = MessageFilters.IsEnabled = Folder is not null && Account is not null;
        if (Folder is not { } f || Account is not { } a) return;
        FolderTitle.Text = f.Name;
        try { _items = _storage.Read(MailStorage.CacheKey(a.Id, f.Path), MailJsonContext.Default.ListMailItem, []); ApplyFilter(); }
        catch (Exception ex) { _items = []; ApplyFilter(); Status.Text = "Could not read cached mail: " + ex.Message; }
        var gen = _generation;
        while (_busy && !_lifetime.IsCancellationRequested) await Task.Delay(25);
        if (gen == _generation && !_lifetime.IsCancellationRequested) await RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        if (Account is not { } a || Folder is not { } f) return;
        var gen = _generation; var query = _serverQuery;
        var knownUid = _items.Count == 0 ? 0u : _items.Max(i => i.Uid);
        await RunAsync("Fetching messages…", async ct =>
        {
            var items = await _service.FetchAsync(a, f.Path, _limit, ct, query);
            if (gen != _generation || query != _serverQuery) return;
            _items = items; ApplyFilter();
            if (knownUid > 0 && query is null && f.Path == (Folders.Items.OfType<MailFolder>().FirstOrDefault()?.Path ?? "INBOX") && !IsActive)
                MailNotifier.Notify(a, items.Where(i => i.Uid > knownUid && i.Unread).ToList());
            Status.Text = $"{_items.Count} messages · {_items.Count(i => i.Unread)} unread · Updated {DateTime.Now:HH:mm}";
        });
    }
    private void ApplyFilter()
    {
        var text = Search.Text?.Trim() ?? "";
        var visible = _items.Where(i => (!UnreadOnly.IsChecked.GetValueOrDefault() || i.Unread) && (!StarredOnly.IsChecked.GetValueOrDefault() || i.Starred) &&
            (_serverQuery == text || text.Length == 0 || (i.Subject + " " + i.Sender + " " + i.Address + " " + i.Preview).Contains(text, StringComparison.OrdinalIgnoreCase))).ToArray();
        var previous = Selected;
        _updatingMessages = true;
        try
        {
            Messages.ItemsSource = visible;
            Messages.SelectedItem = previous is null ? null : visible.FirstOrDefault(i => i.Uid == previous.Uid && i.UidValidity == previous.UidValidity);
        }
        finally { _updatingMessages = false; }
        if (previous is not null && Selected is null) ClearReader();
        if (Messages.SelectedItem is null && visible.Length > 0) Messages.SelectedItem = visible[0];
    }
    private async void SearchChanged()
    {
        if (!_ready) return;
        var wasServerSearch = _serverQuery is not null;
        _serverQuery = null; ApplyFilter();
        if (wasServerSearch && string.IsNullOrWhiteSpace(Search.Text))
        {
            while (_busy && !_lifetime.IsCancellationRequested) await Task.Delay(25);
            if (!_lifetime.IsCancellationRequested) await RefreshAsync();
        }
    }
    private void FilterChanged(object? sender, RoutedEventArgs e) { if (_ready) ApplyFilter(); }
    private async void MessageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingMessages) return;
        if (Selected is not { } item || Account is not { } a || Folder is not { } f) { ClearReader(); return; }
        var gen = _generation; ClearReader(); Subject.Text = item.Subject; Sender.Text = item.Sender + " <" + item.Address + "> · " + item.Stamp;
        // Selection may change while a refresh is holding the connection.
        while (_busy && !_lifetime.IsCancellationRequested) await Task.Delay(25);
        if (gen != _generation || Selected != item || _lifetime.IsCancellationRequested) return;
        await RunAsync("Opening message…", async ct =>
        {
            var message = await _service.MessageAsync(a, f.Path, item.Uid, ct, item.UidValidity);
            if (gen != _generation || Selected != item) return;
            _message = message; ReaderContent.IsVisible = true; MessageActions.IsEnabled = true; MessageActions.IsVisible = true; Sender.IsVisible = true;
            Body.Text = MailText.Read(message);
            if (!string.IsNullOrWhiteSpace(message.HtmlBody))
            {
                TextViewToggle.IsVisible = true;
                HtmlBody.IsVisible = true;
                HtmlBody.Show(message);
            }
            Sender.Text = message.From + "\nTo: " + message.To + (message.Cc.Count > 0 ? "\nCc: " + message.Cc : "") + "\n" + message.Date.ToLocalTime().ToString("f");
            foreach (var attachment in message.Attachments)
            {
                var name = attachment.ContentDisposition?.FileName ?? attachment.ContentType.Name ?? "attachment";
                var button = MailDialogLayout.Action("Save " + name);
                button.Click += async (_, _) => await SaveAttachmentAsync(attachment, name);
                Attachments.Children.Add(button);
            }
            _ = GuiMotion.RevealAsync(ReaderContent, new Point(0, 4),
                ReaderContent.TryFindResource("MotionDurationBase", out var motionDuration) && motionDuration is TimeSpan duration ? duration : null);
            Status.Text = "Message opened.";
            if (item.Unread)
            {
                try { await _service.FlagAsync(a, f.Path, item.Uid, MessageFlags.Seen, true, ct, item.UidValidity); item.Unread = false; SaveCache(a, f); }
                catch (Exception ex) { Status.Text = "Message opened; could not mark it read: " + ex.Message; }
            }
        });
    }
    private void TextViewClicked(object? sender, RoutedEventArgs e)
    {
        _plainText = !_plainText;
        TextViewToggle.Header = _plainText ? "View styled message" : "View plain text";
        Body.IsVisible = _plainText || !_htmlReady;
        HtmlBody.IsVisible = !_plainText && _htmlReady;
    }
    private async void CopyTextClicked(object? sender, RoutedEventArgs e)
    {
        try { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(Body.Text ?? ""); }
        catch { Status.Text = "Couldn’t copy message text."; }
    }
    private void SaveCache(MailAccount a, MailFolder f)
    {
        var key = MailStorage.CacheKey(a.Id, f.Path);
        if (string.IsNullOrWhiteSpace(_serverQuery)) _storage.Write(key, _items, MailJsonContext.Default.ListMailItem);
        else
        {
            var cache = _storage.Read(key, MailJsonContext.Default.ListMailItem, []);
            var updates = _items.ToDictionary(i => i.Uid);
            _storage.Write(key, cache.Select(i => updates.GetValueOrDefault(i.Uid, i)).ToList(), MailJsonContext.Default.ListMailItem);
        }
    }
    private void RemoveFromCache(MailAccount a, MailFolder f, uint uid)
    {
        var key = MailStorage.CacheKey(a.Id, f.Path);
        var cache = _storage.Read(key, MailJsonContext.Default.ListMailItem, []);
        cache.RemoveAll(i => i.Uid == uid); _storage.Write(key, cache, MailJsonContext.Default.ListMailItem);
    }
    private async Task SaveAttachmentAsync(MimeEntity attachment, string name)
    {
        try
        {
            var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save attachment", SuggestedFileName = Path.GetFileName(name), ShowOverwritePrompt = true });
            if (target is null) return;
            await using var stream = await target.OpenWriteAsync();
            if (attachment is MimePart part) await (part.Content ?? throw new IOException("Attachment has no content.")).DecodeToAsync(stream);
            else if (attachment is MessagePart message) await (message.Message ?? throw new IOException("Attached message has no content.")).WriteToAsync(stream);
            Status.Text = "Attachment saved.";
        }
        catch (Exception ex) { Status.Text = "Could not save attachment: " + ex.Message; }
    }
    private async Task FlagAsync(MessageFlags flag, bool add)
    {
        if (Selected is not { } item || Account is not { } a || Folder is not { } f) return;
        await RunAsync("Updating message…", async ct =>
        {
            await _service.FlagAsync(a, f.Path, item.Uid, flag, add, ct, item.UidValidity);
            if (flag == MessageFlags.Flagged) item.Starred = add; else item.Unread = !add;
            SaveCache(a, f); ApplyFilter(); Status.Text = "Message updated.";
        });
    }
    private async Task MoveAsync(SpecialFolder target)
    {
        if (Selected is not { } item || Account is not { } a || Folder is not { } f) return;
        await RunAsync("Moving message…", async ct =>
        {
            await _service.MoveAsync(a, f.Path, item.Uid, target, ct, item.UidValidity); _items.Remove(item); RemoveFromCache(a, f, item.Uid); ApplyFilter(); ClearReader(); Status.Text = "Message moved to " + target + ".";
        });
    }
    private async Task ComposeAsync(string mode = "new", MailDraft? draft = null)
    {
        if (Account is not { } a)
        {
            _pendingDraft = draft ?? new MailDraft();
            await AddAccountAsync();
            return;
        }
        if (!Composer.TryClose()) return;
        draft ??= new MailDraft { AccountId = a.Id };
        if (_message is { } message && mode != "new")
        {
            var recipients = message.ReplyTo.Count > 0 ? message.ReplyTo : message.From;
            draft.To = mode == "forward" ? "" : recipients.ToString();
            if (mode == "all")
            {
                var addresses = recipients.Mailboxes.Select(m => m.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
                addresses.Add(a.Email);
                draft.Cc = new InternetAddressList(message.To.Mailboxes.Concat(message.Cc.Mailboxes).Where(m => addresses.Add(m.Address))).ToString();
            }
            if (mode == "forward" && message.Attachments.Any())
            {
                // Preserve the complete original, including embedded messages and inline attachments.
                var originalPath = Path.Combine(_storage.Root, "cache-" + a.Id + "-forward-" + draft.Id + ".eml");
                try
                {
                    await using var original = new FileStream(originalPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(originalPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    await message.WriteToAsync(original, _lifetime.Token);
                    draft.Attachments.Add(originalPath);
                }
                catch (Exception ex) { Status.Text = "Could not preserve forwarded attachments: " + ex.Message; return; }
            }
            var prefix = mode == "forward" ? "Fwd:" : "Re:";
            draft.Subject = (message.Subject ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? message.Subject ?? "" : prefix + " " + message.Subject;
            draft.InReplyTo = mode == "forward" ? null : message.MessageId;
            draft.QuotedBody = "On " + message.Date.ToLocalTime().ToString("f") + ", " + message.From + " wrote:\n" +
                string.Join("\n", (Body.Text ?? "").Split('\n').Select(l => "> " + l));
        }
        Composer.IsVisible = true; ReaderPane.IsVisible = false;
        Composer.Open(_service, _storage, a, draft, mode switch { "reply" => "Reply", "all" => "Reply to all", "forward" => "Forward", _ => "New message" });
        UpdateDraftAvailability();
    }
    public async Task OpenMailtoAsync(string value)
    {
        try
        {
            var draft = MailText.ParseMailto(value);
            await ComposeAsync(draft: draft);
        }
        catch (Exception ex) { Status.Text = "Could not open mail link: " + ex.Message; }
    }
    private void UpdateDraftAvailability()
    {
        try { DraftsNavigation.IsEnabled = Account is { } a && _storage.Read("drafts", MailJsonContext.Default.ListMailDraft, []).Any(d => d.AccountId == a.Id); }
        catch { DraftsNavigation.IsEnabled = false; }
    }
    private void UpdateAccountPresentation()
    {
        var hasAccounts = _accounts.Count > 0;
        NavigationSidebar.IsVisible = hasAccounts;
        Grid.SetColumn(MainPaneLayout, hasAccounts ? 1 : 0);
        Grid.SetColumnSpan(MainPaneLayout, hasAccounts ? 1 : 2);
        MailWorkspace.IsVisible = hasAccounts;
        AccountSetup.IsVisible = !hasAccounts;
        if (!hasAccounts) ClearReader();
    }
    private Task SaveAccountAsync(MailAccount account)
    {
        if (_accounts.Any(a => a.Email.Equals(account.Email, StringComparison.OrdinalIgnoreCase)))
            throw new MailSignInUnavailableException("This account is already connected. Choose a different account in your browser.");
        var accounts = _accounts.Append(account).ToList();
        _storage.Write("accounts", accounts, MailJsonContext.Default.ListMailAccount);
        _accounts = accounts;
        Accounts.ItemsSource = _accounts.ToArray(); Accounts.SelectedItem = account;
        UpdateAccountPresentation();
        if (_pendingDraft is { } pending)
        {
            _pendingDraft = null;
            Dispatcher.UIThread.Post(async () => await ComposeAsync(draft: pending), DispatcherPriority.Background);
        }
        return Task.CompletedTask;
    }
    private async Task AddAccountAsync()
    {
        if (_accounts.Count == 0) { UpdateAccountPresentation(); return; }
        try { await MailDialogs.AddAccountAsync(this, _service, SaveAccountAsync); }
        catch { Status.Text = "Couldn’t add your account. Please try again."; }
    }
    private async void AccountsClicked(object? sender, RoutedEventArgs e)
    {
        if (!Composer.TryClose()) return;
        var dialog = MailDialogs.Window("Mail accounts", 520, 440); MailDialogs.FollowTheme(dialog, this);
        var layout = new MailDialogLayout(dialog, "Mail accounts"); var panel = layout.Form;
        var list = new ListBox { Classes = { "MailList" }, ItemsSource = _accounts.ToArray(), MaxHeight = 220 }; panel.Children.Add(list);
        panel.Children.Add(MailDialogLayout.Note("Removing an account clears its local cache and sign-in. Mail on the server is kept."));
        var remove = MailDialogLayout.Action("Remove account", "Danger"); remove.IsEnabled = false; layout.Actions.Children.Add(remove);
        list.SelectionChanged += (_, _) => remove.IsEnabled = list.SelectedItem is MailAccount;
        var close = MailDialogLayout.Action("Done"); close.IsCancel = true; close.Click += (_, _) => dialog.Close(); layout.Actions.Children.Add(close);
        remove.Click += async (_, _) =>
        {
            if (list.SelectedItem is not MailAccount a) return;
            remove.IsEnabled = false;
            _operation?.Cancel();
            while (_busy) await Task.Delay(25);
            try
            {
                await MailSecrets.RunAsync("clear", a.Id, null, _lifetime.Token);
                _accounts.Remove(a); _storage.Write("accounts", _accounts, MailJsonContext.Default.ListMailAccount);
                foreach (var file in Directory.EnumerateFiles(_storage.Root, "cache-" + a.Id + "-*")) File.Delete(file);
                File.Delete(Path.Combine(_storage.Root, "folders-" + a.Id + ".json"));
                var drafts = _storage.Read("drafts", MailJsonContext.Default.ListMailDraft, []); drafts.RemoveAll(d => d.AccountId == a.Id); _storage.Write("drafts", drafts, MailJsonContext.Default.ListMailDraft);
                Accounts.ItemsSource = _accounts.ToArray(); Accounts.SelectedIndex = _accounts.Count > 0 ? 0 : -1;
                list.ItemsSource = _accounts.ToArray(); Status.Text = "Account removed.";
                UpdateAccountPresentation(); if (_accounts.Count == 0) dialog.Close();
            }
            catch (Exception ex) { Status.Text = "Could not remove account: " + ex.Message; }
            finally { remove.IsEnabled = list.SelectedItem is MailAccount; }
        };
        await dialog.ShowDialog(this);
    }
    private async void DraftsClicked(object? sender, RoutedEventArgs e)
    {
        if (!Composer.TryClose()) return;
        if (Account is not { } a) return;
        try
        {
            var dialog = MailDialogs.Window("Saved drafts", 540, 420); MailDialogs.FollowTheme(dialog, this);
            var layout = new MailDialogLayout(dialog, "Local drafts"); var panel = layout.Form;
            var drafts = _storage.Read("drafts", MailJsonContext.Default.ListMailDraft, []);
            if (!drafts.Any(d => d.AccountId == a.Id)) { UpdateDraftAvailability(); return; }
            var list = new ListBox { Classes = { "MailList" }, ItemsSource = drafts.Where(d => d.AccountId == a.Id).ToArray(), MaxHeight = 240 }; panel.Children.Add(list);
            var delete = MailDialogLayout.Action("Delete draft", "Subtle"); delete.IsEnabled = false; layout.Actions.Children.Add(delete);
            var close = MailDialogLayout.Action("Close"); close.IsCancel = true; close.Click += (_, _) => dialog.Close(); layout.Actions.Children.Add(close);
            var open = MailDialogLayout.Action("Continue writing", "Primary"); open.IsEnabled = false; layout.Actions.Children.Add(open);
            list.SelectionChanged += (_, _) => open.IsEnabled = delete.IsEnabled = list.SelectedItem is MailDraft;
            MailDraft? selected = null;
            open.Click += (_, _) => { selected = list.SelectedItem as MailDraft; if (selected is not null) dialog.Close(); };
            delete.Click += (_, _) => { if (list.SelectedItem is MailDraft d) { drafts.Remove(d); _storage.Write("drafts", drafts, MailJsonContext.Default.ListMailDraft); list.ItemsSource = drafts.Where(d => d.AccountId == a.Id).ToArray(); UpdateDraftAvailability(); if (!drafts.Any(d => d.AccountId == a.Id)) dialog.Close(); } };
            await dialog.ShowDialog(this); if (selected is not null) await ComposeAsync(draft: selected);
        }
        catch (Exception ex) { Status.Text = "Could not open drafts: " + ex.Message; }
    }
    private async void MoveClicked(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } item || Account is not { } a || Folder is not { } f) return;
        var dialog = MailDialogs.Window("Move message", 460, 440); MailDialogs.FollowTheme(dialog, this);
        var layout = new MailDialogLayout(dialog, "Move message"); var panel = layout.Form;
        panel.Children.Add(MailDialogLayout.Note("Choose a destination mailbox."));
        var folders = new ListBox { Classes = { "MailList" }, ItemsSource = (Folders.ItemsSource as IEnumerable<MailFolder>)?.Where(x => x.Path != f.Path).ToArray(), MaxHeight = 280 }; panel.Children.Add(folders);
        var cancel = MailDialogLayout.Action("Cancel"); cancel.IsCancel = true; cancel.Click += (_, _) => dialog.Close(); layout.Actions.Children.Add(cancel);
        var move = MailDialogLayout.Action("Move", "Primary"); move.IsEnabled = false; layout.Actions.Children.Add(move);
        folders.SelectionChanged += (_, _) => move.IsEnabled = folders.SelectedItem is MailFolder;
        string? destination = null; move.Click += (_, _) => { destination = (folders.SelectedItem as MailFolder)?.Path; if (destination is not null) dialog.Close(); };
        await dialog.ShowDialog(this); if (destination is null) return;
        await RunAsync("Moving message…", async ct => { await _service.MoveToAsync(a, f.Path, item.Uid, destination, ct, item.UidValidity); _items.Remove(item); RemoveFromCache(a, f, item.Uid); ApplyFilter(); ClearReader(); Status.Text = "Message moved."; });
    }
    private async void AddAccountClicked(object? s, RoutedEventArgs e) => await AddAccountAsync();
    private async void ComposeClicked(object? s, RoutedEventArgs e) => await ComposeAsync();
    private async void ReplyClicked(object? s, RoutedEventArgs e) => await ComposeAsync("reply");
    private async void ReplyAllClicked(object? s, RoutedEventArgs e) => await ComposeAsync("all");
    private async void ForwardClicked(object? s, RoutedEventArgs e) => await ComposeAsync("forward");
    private async void RefreshClicked(object? s, RoutedEventArgs e) => await RefreshAsync();
    private async void LoadMoreClicked(object? s, RoutedEventArgs e) { _limit += 100; await RefreshAsync(); }
    private async void StarClicked(object? s, RoutedEventArgs e) => await FlagAsync(MessageFlags.Flagged, !(Selected?.Starred ?? false));
    private async void UnreadClicked(object? s, RoutedEventArgs e) => await FlagAsync(MessageFlags.Seen, false);
    private async void ArchiveClicked(object? s, RoutedEventArgs e) => await MoveAsync(SpecialFolder.Archive);
    private async void TrashClicked(object? s, RoutedEventArgs e) => await MoveAsync(SpecialFolder.Trash);
    private void CancelClicked(object? s, RoutedEventArgs e) => _operation?.Cancel();
}
