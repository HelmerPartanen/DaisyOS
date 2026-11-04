using System.Text.Json.Serialization;

namespace DaisyOS.Shell.Apps.Mail;

public sealed record MailAccount
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "IMAP";
    public string ImapHost { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string ClientId { get; set; } = "";
    public override string ToString() => Email;
}
public sealed record MailFolder(string Name, string Path)
{
    /// <summary>Glyph that tells the user what kind of mailbox this is, from its name.</summary>
    [JsonIgnore]
    public string Icon => Name.ToLowerInvariant() switch
    {
        var n when n.Contains("inbox") => "mail-01",
        var n when n.Contains("sent") => "arrow-up-right",
        var n when n.Contains("draft") => "file-text",
        var n when n.Contains("trash") || n.Contains("deleted") || n.Contains("bin") => "trash",
        var n when n.Contains("archive") || n.Contains("all mail") => "archive",
        var n when n.Contains("star") || n.Contains("flagged") => "star",
        var n when n.Contains("spam") || n.Contains("junk") => "shield-check",
        _ => "folder",
    };
    public override string ToString() => Name;
}
public sealed record MailItem : global::System.ComponentModel.INotifyPropertyChanged
{
    public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private bool _unread;
    private bool _starred;
    public uint Uid { get; set; }
    public uint UidValidity { get; set; }
    public string Subject { get; set; } = "";
    public string Sender { get; set; } = "";
    public string Address { get; set; } = "";
    public DateTimeOffset Date { get; set; }
    public bool Unread { get => _unread; set { _unread = value; PropertyChanged?.Invoke(this, new(nameof(Heading))); PropertyChanged?.Invoke(this, new(nameof(Unread))); PropertyChanged?.Invoke(this, new(nameof(ReadWeight))); } }
    public bool Starred { get => _starred; set { _starred = value; PropertyChanged?.Invoke(this, new(nameof(Heading))); PropertyChanged?.Invoke(this, new(nameof(Starred))); } }
    public string Preview { get; set; } = "";
    [JsonIgnore] public global::Avalonia.Media.FontWeight ReadWeight => Unread ? global::Avalonia.Media.FontWeight.SemiBold : global::Avalonia.Media.FontWeight.Normal;
    public string Heading => (Unread ? "●  " : "") + Sender + (Starred ? "  ★" : "");
    public string Stamp => Date.ToLocalTime().ToString("MMM d · HH:mm");
}
public sealed record MailDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountId { get; set; } = "";
    public string To { get; set; } = "";
    public string Cc { get; set; } = "";
    public string Bcc { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string QuotedBody { get; set; } = "";
    public string? InReplyTo { get; set; }
    public List<string> Attachments { get; set; } = [];
    internal void SeparateLegacyQuote()
    {
        if (!string.IsNullOrEmpty(QuotedBody) || string.IsNullOrEmpty(InReplyTo)) return;
        var boundary = Body.IndexOf("\n\nOn ", StringComparison.Ordinal);
        if (boundary < 0) return;
        var lineEnd = Body.IndexOf('\n', boundary + 2);
        if (lineEnd < 0 || !Body[(boundary + 2)..lineEnd].EndsWith(" wrote:", StringComparison.Ordinal)
            || lineEnd + 1 >= Body.Length || Body[lineEnd + 1] != '>') return;
        QuotedBody = Body[(boundary + 2)..]; Body = Body[..boundary];
    }
    public override string ToString() => string.IsNullOrWhiteSpace(Subject) ? "Untitled draft" : Subject;
}
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(MailProviderRegistration))]
[JsonSerializable(typeof(List<MailFolder>))]
[JsonSerializable(typeof(List<MailAccount>))]
[JsonSerializable(typeof(List<MailItem>))]
[JsonSerializable(typeof(List<MailDraft>))]
internal partial class MailJsonContext : JsonSerializerContext { }
