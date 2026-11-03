using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace DaisyOS.Shell.Apps.Mail;

public sealed class MailService : IAsyncDisposable
{
    private readonly ImapClient _imap = new() { Timeout = 30000 };
    private readonly SemaphoreSlim _gate = new(1);
    private MailAccount? _account;
    private string? _accessToken;
    private DateTimeOffset _tokenTime;
    public MailStorage Storage { get; }
    public MailService(MailStorage storage) => Storage = storage;
    private async Task AuthenticateAsync(MailAccount a, MailKit.IMailService client, CancellationToken ct)
    {
        var secret = await MailSecrets.RunAsync("lookup", a.Id, null, ct);
        if (string.IsNullOrEmpty(secret)) throw new IOException("Your sign-in is missing. Remove this account and add it again.");
        if (a.Provider == "IMAP") await client.AuthenticateAsync(a.Email, secret, ct);
        else
        {
            if (_accessToken is null || DateTimeOffset.UtcNow - _tokenTime > TimeSpan.FromMinutes(40))
            { _accessToken = await MailOAuth.RefreshAsync(a, secret, ct); _tokenTime = DateTimeOffset.UtcNow; }
            await client.AuthenticateAsync(new SaslMechanismOAuth2(a.Email, _accessToken), ct);
        }
    }
    private async Task ConnectAsync(MailAccount a, CancellationToken ct)
    {
        if (_account?.Id != a.Id)
        {
            if (_imap.IsConnected) await _imap.DisconnectAsync(true, ct);
            _account = a; _accessToken = null;
        }
        if (!_imap.IsConnected) await _imap.ConnectAsync(a.ImapHost, a.ImapPort, SecureSocketOptions.SslOnConnect, ct);
        if (!_imap.IsAuthenticated) await AuthenticateAsync(a, _imap, ct);
    }
    public async Task ValidateAsync(MailAccount a, string password, CancellationToken ct, Action<string>? progress = null)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Select the account before accepting its token, so ConnectAsync keeps it.
            if (_imap.IsConnected) await _imap.DisconnectAsync(true, ct);
            _account = a; _accessToken = null;
            if (a.Provider != "IMAP") { _accessToken = await MailOAuth.SignInAsync(a, ct, progress); _tokenTime = DateTimeOffset.UtcNow; }
            else await MailSecrets.RunAsync("store", a.Id, password, ct);
            progress?.Invoke("incoming");
            await ConnectAsync(a, ct);
            progress?.Invoke("outgoing");
            using var smtp = new SmtpClient { Timeout = 30000 };
            await smtp.ConnectAsync(a.SmtpHost, a.SmtpPort, a.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
            await AuthenticateAsync(a, smtp, ct);
            await smtp.DisconnectAsync(true, ct);
        }
        finally { _gate.Release(); }
    }
    public async Task<List<MailFolder>> FoldersAsync(MailAccount a, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await ConnectAsync(a, ct);
            var folders = new List<MailFolder> { new("Inbox", _imap.Inbox.FullName) };
            foreach (var ns in _imap.PersonalNamespaces)
                foreach (var f in await _imap.GetFoldersAsync(ns, cancellationToken: ct))
                    if (f.FullName != _imap.Inbox.FullName && !f.Attributes.HasFlag(FolderAttributes.NoSelect)) folders.Add(new(f.Name, f.FullName));
            Storage.Write("folders-" + a.Id, folders, MailJsonContext.Default.ListMailFolder);
            return folders;
        }
        finally { _gate.Release(); }
    }
    public async Task<List<MailItem>> FetchAsync(MailAccount a, string path, int limit, CancellationToken ct, string? query = null)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await ConnectAsync(a, ct);
            var folder = await _imap.GetFolderAsync(path, ct); await folder.OpenAsync(FolderAccess.ReadOnly, ct);
            // Cache identity includes UIDVALIDITY: recycled UIDs must never open a different cached message.
            var key = MailStorage.CacheKey(a.Id, path);
            var validity = Path.Combine(Storage.Root, key + ".validity");
            var current = folder.UidValidity.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
            if (!File.Exists(validity) || File.ReadAllText(validity) != current)
            {
                foreach (var file in Directory.EnumerateFiles(Storage.Root, key + "-*.eml")) File.Delete(file);
                File.WriteAllText(validity, current);
            }
            if (folder.Count == 0) { Storage.Write(key, new List<MailItem>(), MailJsonContext.Default.ListMailItem); return []; }
            var fields = MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.PreviewText;
            IList<IMessageSummary> summaries;
            if (!string.IsNullOrWhiteSpace(query))
            {
                var matches = await folder.SearchAsync(SearchQuery.SubjectContains(query).Or(SearchQuery.FromContains(query)).Or(SearchQuery.BodyContains(query)), ct);
                var ids = matches.OrderBy(uid => uid.Id).TakeLast(limit).ToList();
                summaries = ids.Count == 0 ? new List<IMessageSummary>() : await folder.FetchAsync(ids, fields, ct);
            }
            else summaries = await folder.FetchAsync(Math.Max(0, folder.Count - limit), -1, fields, ct);
            var items = summaries.Reverse().Select(s => new MailItem { Uid = s.UniqueId.Id, UidValidity = folder.UidValidity, Subject = s.Envelope?.Subject ?? "(No subject)",
                Sender = s.Envelope?.From?.Mailboxes.FirstOrDefault()?.Name ?? s.Envelope?.From?.ToString() ?? "Unknown sender", Address = s.Envelope?.From?.Mailboxes.FirstOrDefault()?.Address ?? "",
                Date = s.Envelope?.Date ?? DateTimeOffset.MinValue, Unread = !s.Flags.GetValueOrDefault().HasFlag(MessageFlags.Seen),
                Starred = s.Flags.GetValueOrDefault().HasFlag(MessageFlags.Flagged), Preview = s.PreviewText ?? "" }).ToList();
            if (string.IsNullOrWhiteSpace(query)) Storage.Write(key, items, MailJsonContext.Default.ListMailItem);
            return items;
        }
        finally { _gate.Release(); }
    }
    public async Task<MimeMessage> MessageAsync(MailAccount a, string path, uint uid, CancellationToken ct, uint expectedValidity = 0)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var file = Path.Combine(Storage.Root, MailStorage.CacheKey(a.Id, path) + "-" + uid + ".eml");
            if (File.Exists(file)) return await MimeMessage.LoadAsync(file, ct);
            await ConnectAsync(a, ct);
            var folder = await _imap.GetFolderAsync(path, ct); await folder.OpenAsync(FolderAccess.ReadOnly, ct);
            VerifyValidity(folder, expectedValidity);
            var message = await folder.GetMessageAsync(new UniqueId(uid), ct);
            var temporary = file + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    await message.WriteToAsync(stream, ct);
                }
                File.Move(temporary, file, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return message;
        }
        finally { _gate.Release(); }
    }
    public async Task FlagAsync(MailAccount a, string path, uint uid, MessageFlags flag, bool add, CancellationToken ct, uint expectedValidity = 0)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await ConnectAsync(a, ct); var folder = await _imap.GetFolderAsync(path, ct); await folder.OpenAsync(FolderAccess.ReadWrite, ct);
            VerifyValidity(folder, expectedValidity);
            if (add) await folder.AddFlagsAsync(new UniqueId(uid), flag, true, ct);
            else await folder.RemoveFlagsAsync(new UniqueId(uid), flag, true, ct);
        }
        finally { _gate.Release(); }
    }
    public async Task MoveAsync(MailAccount a, string path, uint uid, SpecialFolder target, CancellationToken ct, uint expectedValidity = 0)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await ConnectAsync(a, ct);
            var destination = _imap.GetFolder(target) ?? throw new IOException("This server has no " + target + " folder. Choose a destination with Move instead.");
            var folder = await _imap.GetFolderAsync(path, ct); await folder.OpenAsync(FolderAccess.ReadWrite, ct);
            VerifyValidity(folder, expectedValidity);
            if (folder.FullName == destination.FullName) throw new IOException("The message is already in " + target + ".");
            await folder.MoveToAsync(new UniqueId(uid), destination, ct);
        }
        finally { _gate.Release(); }
    }
    public async Task MoveToAsync(MailAccount a, string path, uint uid, string destination, CancellationToken ct, uint expectedValidity = 0)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await ConnectAsync(a, ct); var folder = await _imap.GetFolderAsync(path, ct); await folder.OpenAsync(FolderAccess.ReadWrite, ct);
            VerifyValidity(folder, expectedValidity);
            await folder.MoveToAsync(new UniqueId(uid), await _imap.GetFolderAsync(destination, ct), ct);
        }
        finally { _gate.Release(); }
    }
    public static MimeMessage BuildMessage(MailAccount a, MailDraft draft)
    {
        var message = new MimeMessage(); message.From.Add(new MailboxAddress(a.Name, a.Email));
        var options = ParserOptions.Default.Clone(); options.AllowAddressesWithoutDomain = false;
        if (!string.IsNullOrWhiteSpace(draft.To)) message.To.AddRange(InternetAddressList.Parse(options, draft.To));
        if (!string.IsNullOrWhiteSpace(draft.Cc)) message.Cc.AddRange(InternetAddressList.Parse(options, draft.Cc));
        if (!string.IsNullOrWhiteSpace(draft.Bcc)) message.Bcc.AddRange(InternetAddressList.Parse(options, draft.Bcc));
        if (!message.To.Mailboxes.Concat(message.Cc.Mailboxes).Concat(message.Bcc.Mailboxes).Any())
            throw new FormatException("Add at least one email address.");
        message.Subject = draft.Subject; message.InReplyTo = draft.InReplyTo;
        message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId();
        var body = new BodyBuilder { TextBody = draft.Body + (string.IsNullOrEmpty(draft.QuotedBody) ? "" : "\n\n" + draft.QuotedBody) };
        foreach (var path in draft.Attachments) body.Attachments.Add(path);
        message.Body = body.ToMessageBody(); return message;
    }
    public async Task<string?> SendAsync(MailAccount a, MailDraft draft, CancellationToken ct)
    {
        var message = BuildMessage(a, draft);
        await _gate.WaitAsync(ct);
        try
        {
            await ConnectAsync(a, ct);
            using var smtp = new SmtpClient { Timeout = 60000 };
            await smtp.ConnectAsync(a.SmtpHost, a.SmtpPort, a.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
            await AuthenticateAsync(a, smtp, ct);
            await smtp.SendAsync(message, ct);
            try { await smtp.DisconnectAsync(true, ct); } catch { /* SMTP already accepted the message. */ }
            try
            {
                var sent = _imap.GetFolder(SpecialFolder.Sent);
                // Gmail and Outlook file submitted messages themselves.
                if (a.Provider == "IMAP")
                {
                    if (sent is null) return "Message sent. This server has no designated Sent folder, so a copy was not saved.";
                    await sent.AppendAsync(message, MessageFlags.Seen, ct);
                }
                return null;
            }
            catch { return "Message sent, but a copy could not be saved to Sent. Do not send it again."; }
        }
        finally { _gate.Release(); }
    }
    private static void VerifyValidity(IMailFolder folder, uint expected)
    {
        if (expected != 0 && folder.UidValidity != expected) throw new IOException("The server reset this mailbox. Refresh it before opening or changing this message.");
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { _imap.Dispose(); }
        finally { _gate.Release(); }
    }
}
