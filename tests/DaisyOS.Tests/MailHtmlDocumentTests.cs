using DaisyOS.Shell.Apps.Mail;
using MimeKit;
using Xunit;

namespace DaisyOS.Tests;

public sealed class MailHtmlDocumentTests
{
    [Fact]
    public void StylesTablesAndEmbeddedImagesSurviveWithoutExecutableMarkup()
    {
        var builder = new BodyBuilder
        {
            TextBody = "Plain fallback",
            HtmlBody = "<html><head><style>.hero{background:#123456}</style></head><body><table class='hero'><tr><td><img src='cid:logo@sender' onerror='steal()'><a href='https://example.org/sale'>Shop</a></td></tr></table><script>alert('bad')</script><iframe src='https://example.org/track'></iframe><meta http-equiv='refresh' content='0;url=https://example.org'><img src='file:///etc/passwd'><a href='javascript:alert(1)'>Bad</a></body></html>"
        };
        var image = builder.LinkedResources.Add("logo.png", new byte[] { 1, 2, 3 }); image.ContentId = "logo@sender";
        using var message = new MimeMessage { Body = builder.ToMessageBody() };
        var document = MailHtmlDocument.Create(message);
        Assert.Contains("background:#123456", document);
        Assert.Contains("<table", document);
        Assert.Contains("data:image/png;base64,AQID", document);
        Assert.Contains("https://example.org/sale", document);
        Assert.Contains("script-src 'none'", document);
        Assert.DoesNotContain("onerror", document);
        Assert.DoesNotContain("<script", document);
        Assert.DoesNotContain("<iframe", document);
        Assert.DoesNotContain("http-equiv=\"refresh\"", document);
        Assert.DoesNotContain("file:///", document);
        Assert.DoesNotContain("javascript:", document);
    }

    [Theory]
    [InlineData("https://example.org/news", true)]
    [InlineData("mailto:hello@example.org", true)]
    [InlineData("file:///home/user/private", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,bad", false)]
    public void OnlyBrowserAndMailLinksCanLeaveTheReader(string uri, bool allowed) =>
        Assert.Equal(allowed, MailHtmlDocument.IsExternalLink(uri));
}
