namespace DaisyOS.Core.Models;

public sealed record DateTimeSettingsStatus(
    bool IsAvailable,
    string TimeZoneId,
    bool AutomaticTime,
    DateTimeOffset LocalTime,
    IReadOnlyList<string> AvailableTimeZones,
    string Detail);

public sealed record DateTimeSettingsResult(bool Succeeded, string Message);
