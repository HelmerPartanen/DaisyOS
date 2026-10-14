using System.Globalization;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Time;

public sealed class LinuxDateTimeSettingsService(ICommandRunner commandRunner) : IDateTimeSettingsService
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ChangeTimeout = TimeSpan.FromSeconds(20);

    public async Task<DateTimeSettingsStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var statusTask = commandRunner.RunAsync(
            "timedatectl",
            ["show", "--property=Timezone", "--property=NTP", "--no-pager"],
            QueryTimeout,
            cancellationToken);
        var zonesTask = commandRunner.RunAsync(
            "timedatectl",
            ["list-timezones", "--no-pager"],
            QueryTimeout,
            cancellationToken);
        await Task.WhenAll(statusTask, zonesTask);

        var status = await statusTask;
        var zones = await zonesTask;
        var values = ParseProperties(status.StandardOutput);
        var timeZoneId = values.GetValueOrDefault("Timezone");
        if (string.IsNullOrWhiteSpace(timeZoneId)) timeZoneId = TimeZoneInfo.Local.Id;

        var availableZones = zones.Succeeded
            ? zones.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : TimeZoneInfo.GetSystemTimeZones().Select(zone => zone.Id).ToArray();

        return new DateTimeSettingsStatus(
            status.Succeeded,
            timeZoneId,
            string.Equals(values.GetValueOrDefault("NTP"), "yes", StringComparison.OrdinalIgnoreCase),
            DateTimeOffset.Now,
            availableZones.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            status.Succeeded
                ? "Date, time, and time zone are provided by the system."
                : "Date and time controls are unavailable on this system.");
    }

    public async Task<DateTimeSettingsResult> SetTimeZoneAsync(
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeTimeZoneId(timeZoneId))
            return new DateTimeSettingsResult(false, "Choose a valid time zone.");

        var result = await commandRunner.RunAsync(
            "timedatectl",
            ["set-timezone", timeZoneId],
            ChangeTimeout,
            cancellationToken);
        return ToResult(result, "Time zone changed.", "Couldn’t change the time zone.");
    }

    public async Task<DateTimeSettingsResult> SetAutomaticTimeAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var result = await commandRunner.RunAsync(
            "timedatectl",
            ["set-ntp", enabled ? "true" : "false"],
            ChangeTimeout,
            cancellationToken);
        return ToResult(
            result,
            enabled ? "Automatic date and time turned on." : "Automatic date and time turned off.",
            "Couldn’t change automatic date and time.");
    }

    public async Task<DateTimeSettingsResult> SetLocalTimeAsync(
        DateTime localTime,
        CancellationToken cancellationToken = default)
    {
        var value = localTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var result = await commandRunner.RunAsync(
            "timedatectl",
            ["set-time", value],
            ChangeTimeout,
            cancellationToken);
        return ToResult(result, "Date and time changed.", "Couldn’t change the date and time.");
    }

    private static Dictionary<string, string> ParseProperties(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

    private static bool IsSafeTimeZoneId(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.StartsWith("/", StringComparison.Ordinal)
        && !value.Split('/').Any(segment => segment is "." or "..")
        && value.All(character => char.IsLetterOrDigit(character) || character is '/' or '_' or '-' or '+');

    private static DateTimeSettingsResult ToResult(
        CommandResult result,
        string successMessage,
        string failureMessage) =>
        new(result.Succeeded, result.Succeeded ? successMessage : failureMessage);
}
