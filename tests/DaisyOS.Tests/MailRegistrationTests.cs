using DaisyOS.Shell.Apps.Mail;
using Xunit;

namespace DaisyOS.Tests;

public sealed class MailRegistrationTests
{
    [Theory]
    [InlineData("Google", "openid email profile")]
    [InlineData("Google", "https://mail.google.com/.invalid")]
    [InlineData("Microsoft", "https://outlook.office.com/IMAP.AccessAsUser.All")]
    public void PartialConsentCannotConnectMailbox(string provider, string scopes)
    {
        Assert.Throws<MailSignInUnavailableException>(() => MailOAuth.RequireMailPermission(provider, scopes));
    }

    [Theory]
    [InlineData("Google", "openid email https://mail.google.com/")]
    [InlineData("Microsoft", "https://outlook.office.com/SMTP.Send https://outlook.office.com/IMAP.AccessAsUser.All")]
    public void MailConsentAllowsMailboxValidation(string provider, string scopes)
    {
        MailOAuth.RequireMailPermission(provider, scopes);
    }

    [Theory]
    [InlineData("Google", "127.0.0.1")]
    [InlineData("Microsoft", "localhost")]
    public void BrowserCallbackUsesProviderCompatibleLoopback(string provider, string host)
    {
        var callback = new Uri(MailOAuth.CallbackUri(provider, 54321));
        Assert.Equal("http", callback.Scheme);
        Assert.Equal(host, callback.Host);
        Assert.Equal(54321, callback.Port);
        Assert.True(callback.IsLoopback);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"GoogleClientId\":null,\"MicrosoftClientId\":null}")]
    [InlineData("{\"GoogleClientId\":\"not-a-client\",\"MicrosoftClientId\":\"not-a-client\"}")]
    public void MissingOrInvalidRegistrationsCannotEnableSignIn(string json)
    {
        WithRegistration(json, config =>
        {
            Assert.False(config.GoogleConfigured);
            Assert.False(config.MicrosoftConfigured);
        });
    }

    [Fact]
    public void ProvisionedRegistrationDoesNotRequireDesktopEnvironmentVariables()
    {
        WithRegistration("{\"googleClientId\":\" 123-test.apps.googleusercontent.com \",\"microsoftClientId\":\"11111111-1111-1111-1111-111111111111\"}", config =>
        {
            Assert.True(config.GoogleConfigured);
            Assert.True(config.MicrosoftConfigured);
            Assert.Equal("123-test.apps.googleusercontent.com", config.GoogleClientId);
            Assert.Equal("", config.GoogleClientSecret);
        });
    }
    private static void WithRegistration(string json, Action<MailProviderRegistration> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "daisy-mail-registration-" + Guid.NewGuid().ToString("N") + ".json");
        try { File.WriteAllText(path, json); check(MailProviderRegistration.Load(path, useEnvironment: false)); }
        finally { File.Delete(path); }
    }
}
