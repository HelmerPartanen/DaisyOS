using DaisyOS.System.Accounts;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxUserAccountServiceTests
{
    [Fact]
    public void PasswordHashesUseFreshSaltsAndNeverContainThePlaintext()
    {
        if (!OperatingSystem.IsLinux()) return;
        var first = LinuxUserAccountService.HashPassword("Test-password-123");
        var second = LinuxUserAccountService.HashPassword("Test-password-123");
        Assert.StartsWith("$6$rounds=100000$", first);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain("Test-password-123", first);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("password\0hidden")]
    public async Task InvalidPasswordsAreRejectedBeforeContactingAccountServices(string password)
    {
        Assert.False((await new LinuxUserAccountService().SetPasswordAsync(password, "")).Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Name:injected")]
    [InlineData("Name\nInjected")]
    public async Task InvalidNamesAreRejectedBeforeContactingAccountServices(string name)
    {
        Assert.False((await new LinuxUserAccountService().SetFullNameAsync(name)).Succeeded);
    }
}

public sealed class PasswordlessAccountAuthenticationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyPasswordsStillRequireTheSystemAuthenticationResult(bool accepted)
    {
        var auth = new AuthenticationStub(accepted);
        var service = new DaisyOS.System.Security.LinuxLockScreenService(auth);
        Assert.Equal(accepted, await service.AuthenticateAsync(""));
        Assert.Equal("", auth.Submitted);
    }

    private sealed class AuthenticationStub(bool accepted) : DaisyOS.Core.Services.IAuthenticationService
    {
        public string? Submitted;
        public bool Authenticate(string password) { Submitted = password; return accepted; }
    }
}
