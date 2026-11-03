using System.Text.Json;
using DaisyOS.Shell.Apps.Mail;
using Xunit;

namespace DaisyOS.Tests;

public sealed class MailComposeTests
{
    private static readonly MailAccount Account = new() { Name = "Me", Email = "me@example.org" };

    [Fact]
    public void ReplyKeepsEditableTextSeparateAndSendsOriginalOnce()
    {
        var draft = new MailDraft { To = "Alex <alex@example.org>", Body = "Thanks!", QuotedBody = "On Tuesday, Alex wrote:\n> Original message", InReplyTo = "original@example.org", Cc = "copy@example.org", Bcc = "hidden@example.org" };
        using var message = MailService.BuildMessage(Account, draft);
        Assert.Equal("Thanks!\n\nOn Tuesday, Alex wrote:\n> Original message", message.TextBody);
        Assert.Equal("Thanks!", draft.Body);
        Assert.Equal("original@example.org", message.InReplyTo);
        Assert.Single(message.Cc.Mailboxes); Assert.Single(message.Bcc.Mailboxes);
    }

    [Fact]
    public void ExistingDraftWithoutSeparateQuoteRetainsItsCompleteBody()
    {
        var draft = JsonSerializer.Deserialize<MailDraft>("{\"To\":\"alex@example.org\",\"Body\":\"My reply\\n\\n> Old quote\"}")!;
        using var message = MailService.BuildMessage(Account, draft);
        Assert.Equal("", draft.QuotedBody);
        Assert.Equal(draft.Body, message.TextBody);
    }

    [Theory]
    [InlineData("")]
    [InlineData("My reply")]
    public void OlderReplyDraftsCollapseTheirOriginalWithoutChangingSentText(string reply)
    {
        var original = reply + "\n\nOn Tuesday, Alex wrote:\n> Original message";
        var draft = new MailDraft { To = "alex@example.org", InReplyTo = "original@example.org", Body = original };
        draft.SeparateLegacyQuote();
        Assert.Equal(reply, draft.Body);
        Assert.StartsWith("On Tuesday", draft.QuotedBody);
        using var message = MailService.BuildMessage(Account, draft);
        Assert.Equal(original, message.TextBody);
    }

    [Theory]
    [InlineData("a name without an address")]
    [InlineData("invalid")]
    [InlineData("")]
    public void InvalidRecipientsAreRejectedBeforeConnecting(string recipient) =>
        Assert.ThrowsAny<FormatException>(() => MailService.BuildMessage(Account, new MailDraft { To = recipient, Body = "Draft remains editable" }));

    [Fact]
    public void HiddenRecipientsDoNotRequireAFakeToAddress()
    {
        using var message = MailService.BuildMessage(Account, new MailDraft { Bcc = "hidden@example.org", Body = "Hello" });
        Assert.Empty(message.To); Assert.Single(message.Bcc.Mailboxes);
    }
}
