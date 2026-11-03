using System.Net;
using System.Text.RegularExpressions;
using MimeKit;
using MimeKit.Text;

namespace DaisyOS.Shell.Apps.Mail;

internal static partial class MailHtmlDocument
{
    private static readonly HashSet<string> Removed = new(StringComparer.OrdinalIgnoreCase)
    { "script", "iframe", "frame", "frameset", "object", "embed", "applet", "form", "input", "button", "textarea", "select", "video", "audio", "source", "canvas", "svg", "math", "meta", "base" };

    public static string Create(MimeMessage message)
    {
        var html = message.HtmlBody ?? throw new InvalidOperationException("No HTML body.");
        if (html.Length > 8 * 1024 * 1024) throw new IOException("This message is too large for styled view.");
        var images = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var part in message.BodyParts.OfType<MimePart>().Where(p => p.ContentType.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase)))
        {
            if (part.Content is not { Stream: { CanSeek: true } source } content || source.Length > 4 * 1024 * 1024 || total > 8 * 1024 * 1024) continue;
            using var stream = new MemoryStream(); content.DecodeTo(stream);
            total += stream.Length;
            if (stream.Length > 4 * 1024 * 1024 || total > 8 * 1024 * 1024) continue;
            var data = "data:" + part.ContentType.MimeType + ";base64," + Convert.ToBase64String(stream.ToArray());
            if (!string.IsNullOrEmpty(part.ContentId)) images["cid:" + part.ContentId] = data;
            if (part.ContentLocation is { } location) images[location.ToString()] = data;
        }
        string Resolve(string value)
        {
            var decoded = WebUtility.HtmlDecode(value);
            return images.GetValueOrDefault(decoded) ?? images.GetValueOrDefault(Uri.UnescapeDataString(decoded)) ?? decoded;
        }
        // Resolve CID references in styles as well as <img src> attributes.
        html = CidUrl().Replace(html, match => Resolve(match.Value));
        var converter = new HtmlToHtml
        {
            FilterComments = true,
            HtmlTagCallback = (tag, writer) =>
            {
                if (Removed.Contains(tag.TagName))
                {
                    tag.DeleteTag = tag.DeleteEndTag = true;
                    if (tag.TagName is "script" or "iframe" or "object" or "svg" or "math") tag.SuppressInnerContent = true;
                    return;
                }
                tag.WriteTag(writer, false);
                if (tag.IsEndTag) return;
                foreach (var attribute in tag.Attributes)
                {
                    var name = attribute.Name.ToLowerInvariant();
                    if (name.StartsWith("on", StringComparison.Ordinal) || name is "srcdoc" or "srcset" or "action" or "formaction" or "ping" or "nonce" or "integrity") continue;
                    var value = attribute.Value ?? "";
                    if (name is "src" or "href" or "background")
                    {
                        value = Resolve(value);
                        if (!(IsExternalLink(value) || value.StartsWith('#') || name != "href" && value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))) continue;
                    }
                    writer.WriteAttribute(name, value);
                }
            }
        };
        // The first policy cannot be loosened by a sender's HTML. No script,
        // forms, frames, local files, storage APIs or in-app navigation.
        const string prefix = "<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; script-src 'none'; style-src 'unsafe-inline' https: http:; img-src data: https: http:; font-src data: https:; connect-src 'none'; frame-src 'none'; object-src 'none'; form-action 'none'; base-uri 'none'\"><meta name=\"referrer\" content=\"no-referrer\"><style>html{color-scheme:light;background:#fff}body{margin:16px;color:#202124;font:16px/1.5 sans-serif;overflow-wrap:anywhere}img{max-width:100%;height:auto}a{color:#065fcb}</style></head><body>";
        return prefix + converter.Convert(html) + "</body></html>";
    }

    internal static bool IsExternalLink(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http" or "mailto";

    [GeneratedRegex("cid:[^\\s'\"<>\\)]+", RegexOptions.IgnoreCase)]
    private static partial Regex CidUrl();
}
