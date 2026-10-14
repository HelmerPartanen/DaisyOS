using DaisyOS.Core.Models;
using DaisyOS.Core.Services;

namespace DaisyOS.System.Session;

public sealed class LinuxUserSessionService : IUserSessionService
{
    public Task<UserSessionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var userName = Environment.UserName;
        var hostName = Environment.MachineName;
        var sessionType = ReadEnvironment("XDG_SESSION_TYPE", "Unknown");
        var desktopSession = ReadEnvironment("XDG_CURRENT_DESKTOP", ReadEnvironment("DESKTOP_SESSION", "Unknown"));
        var shell = ReadEnvironment("SHELL", "Unknown");
        var detail = $"{userName}@{hostName}, {sessionType} session.";

        return Task.FromResult(new UserSessionStatus(userName, hostName, sessionType, desktopSession, shell, detail));
    }

    private static string ReadEnvironment(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
