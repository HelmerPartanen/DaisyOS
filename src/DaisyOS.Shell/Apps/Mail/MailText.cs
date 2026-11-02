using System.Text;
using MimeKit;
using MimeKit.Text;

namespace DaisyOS.Shell.Apps.Mail;

public static class MailText
{
    public static MailDraft ParseMailto(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "mailto") throw new FormatException("Expected a mailto link.");
        var draft = new MailDraft { To = Uri.UnescapeDataString(value[7..].Split('?', 2)[0]) };
        foreach (var field in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = field.Split('=', 2); if (pair.Length != 2) continue;
            var text = Uri.UnescapeDataString(pair[1]);
            switch (pair[0].ToLowerInvariant())
            {
                case "subject": draft.Subject = text.Replace("\r", " ").Replace("\n", " "); break;
                case "body": draft.Body = text; break;
                case "cc": draft.Cc = text; break;
                case "bcc": draft.Bcc = text; break;
            }
        }
        return draft;
    }
    public static string Read(MimeMessage message) => message.TextBody ?? (message.HtmlBody is { } html ? FromHtml(html) : "This message has no text content.");
    public static string FromHtml(string html)
    {
        using var reader = new StringReader(html);
        var tokenizer = new HtmlTokenizer(reader) { DecodeCharacterReferences = true };
        var output = new StringBuilder();
        string? hidden = null;
        while (tokenizer.ReadNextToken(out var token))
        {
            if (token is HtmlTagToken tag)
            {
                var name = tag.Name.ToLowerInvariant();
                if (hidden is not null) { if (tag.IsEndTag && name == hidden) hidden = null; continue; }
                if (!tag.IsEndTag && name is "script" or "style" or "head") { hidden = name; continue; }
                if (name is "br" or "p" or "div" or "tr" or "li" or "h1" or "h2" or "blockquote") output.AppendLine();
                if (name is "td" or "th") output.Append(' ');
            }
            else if (hidden is null && token is HtmlDataToken data && token.Kind == HtmlTokenKind.Data) output.Append(data.Data);
        }
        return output.ToString().Trim();
    }
}
