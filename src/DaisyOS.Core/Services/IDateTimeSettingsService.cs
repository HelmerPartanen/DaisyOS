using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IDateTimeSettingsService
{
    Task<DateTimeSettingsStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<DateTimeSettingsResult> SetTimeZoneAsync(
        string timeZoneId,
        CancellationToken cancellationToken = default);

    Task<DateTimeSettingsResult> SetAutomaticTimeAsync(
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<DateTimeSettingsResult> SetLocalTimeAsync(
        DateTime localTime,
        CancellationToken cancellationToken = default);
}
