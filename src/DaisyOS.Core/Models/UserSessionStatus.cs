namespace DaisyOS.Core.Models;

public sealed record UserSessionStatus(
    string UserName,
    string HostName,
    string SessionType,
    string DesktopSession,
    string Shell,
    string Detail);
