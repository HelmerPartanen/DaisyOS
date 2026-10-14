using Avalonia.Styling;
using Avalonia.Threading;

namespace DaisyOS.Shell.Services.Appearance;

/// <summary>Resolves light or dark mode from the local timezone's sunrise and sunset.</summary>
public sealed class AutomaticThemeScheduler : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly Action<ThemeVariant> _applyTheme;
    private ThemeVariant? _lastAppliedTheme;

    public AutomaticThemeScheduler(Action<ThemeVariant> applyTheme)
    {
        _applyTheme = applyTheme;
        _timer.Tick += (_, _) => ApplyCurrentTheme();
    }

    public void Start()
    {
        ApplyCurrentTheme(force: true);
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public string GetScheduleDescription(DateTimeOffset now)
    {
        var schedule = SolarSchedule.ForLocalTimeZone(now);
        return $"Light from {schedule.Sunrise.ToString("t")} to {schedule.Sunset.ToString("t")} today";
    }

    private void ApplyCurrentTheme(bool force = false)
    {
        var theme = SolarSchedule.ForLocalTimeZone(DateTimeOffset.Now).IsDaylight(DateTimeOffset.Now)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        if (force || _lastAppliedTheme != theme)
        {
            _lastAppliedTheme = theme;
            _applyTheme(theme);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}

public readonly record struct SolarDaySchedule(DateTimeOffset Sunrise, DateTimeOffset Sunset)
{
    public bool IsDaylight(DateTimeOffset time) => time >= Sunrise && time < Sunset;
}

internal static class SolarSchedule
{
    private const double DefaultLatitude = 0;
    private const double DefaultLongitude = 0;

    public static SolarDaySchedule ForLocalTimeZone(DateTimeOffset time)
    {
        var zone = TimeZoneInfo.Local;
        var location = TryReadZoneLocation(zone.Id) ?? (DefaultLatitude, DefaultLongitude);
        var local = TimeZoneInfo.ConvertTime(time, zone);
        return Calculate(local.Date, zone, location.Item1, location.Item2);
    }

    private static SolarDaySchedule Calculate(DateTime date, TimeZoneInfo zone, double latitude, double longitude)
    {
        // NOAA's civil-sunrise equation. A timezone location is used instead of network geolocation.
        var day = date.DayOfYear;
        var longitudeHour = longitude / 15d;
        var sunriseUtc = CalculateUtcHour(day, latitude, longitudeHour, true);
        var sunsetUtc = CalculateUtcHour(day, latitude, longitudeHour, false);
        var offset = zone.GetUtcOffset(new DateTimeOffset(date, zone.GetUtcOffset(date)));

        var sunrise = new DateTimeOffset(date, TimeSpan.Zero).AddHours(sunriseUtc).ToOffset(offset);
        var sunset = new DateTimeOffset(date, TimeSpan.Zero).AddHours(sunsetUtc).ToOffset(offset);
        return new SolarDaySchedule(sunrise, sunset);
    }

    private static double CalculateUtcHour(int day, double latitude, double longitudeHour, bool sunrise)
    {
        var approximateTime = day + ((sunrise ? 6d : 18d) - longitudeHour) / 24d;
        var meanAnomaly = (0.9856d * approximateTime) - 3.289d;
        var trueLongitude = NormalizeDegrees(meanAnomaly + (1.916d * Math.Sin(ToRadians(meanAnomaly))) + (0.020d * Math.Sin(2 * ToRadians(meanAnomaly))) + 282.634d);
        var rightAscension = NormalizeDegrees(ToDegrees(Math.Atan(0.91764d * Math.Tan(ToRadians(trueLongitude)))));
        rightAscension += Math.Floor(trueLongitude / 90d) * 90d - Math.Floor(rightAscension / 90d) * 90d;
        rightAscension /= 15d;

        var sinDeclination = 0.39782d * Math.Sin(ToRadians(trueLongitude));
        var cosDeclination = Math.Cos(Math.Asin(sinDeclination));
        var cosineHourAngle = (Math.Cos(ToRadians(90.833d)) - (sinDeclination * Math.Sin(ToRadians(latitude)))) /
                              (cosDeclination * Math.Cos(ToRadians(latitude)));

        // Polar-day/night fallback: switch at a predictable local time instead of leaving automatic mode stuck.
        if (cosineHourAngle > 1d || cosineHourAngle < -1d)
        {
            return sunrise ? 7d - longitudeHour : 19d - longitudeHour;
        }

        var hourAngle = sunrise
            ? 360d - ToDegrees(Math.Acos(cosineHourAngle))
            : ToDegrees(Math.Acos(cosineHourAngle));
        var localMeanTime = (hourAngle / 15d) + rightAscension - (0.06571d * approximateTime) - 6.622d;
        return NormalizeHours(localMeanTime - longitudeHour);
    }

    private static (double, double)? TryReadZoneLocation(string timeZoneId)
    {
        foreach (var path in new[] { "/usr/share/zoneinfo/zone1970.tab", "/usr/share/zoneinfo/zone.tab" })
        {
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (line.StartsWith('#')) continue;
                    var fields = line.Split('\t');
                    if (fields.Length >= 3 && string.Equals(fields[2], timeZoneId, StringComparison.Ordinal))
                    {
                        return ParseCoordinates(fields[1]);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Try the next standard tzdata source.
            }
        }

        return null;
    }

    private static (double, double)? ParseCoordinates(string coordinates)
    {
        var longitudeStart = coordinates.IndexOfAny(['+', '-'], 1);
        if (longitudeStart < 0) return null;
        return (ParseCoordinate(coordinates[..longitudeStart], true), ParseCoordinate(coordinates[longitudeStart..], false));
    }

    private static double ParseCoordinate(string value, bool latitude)
    {
        var sign = value[0] == '-' ? -1d : 1d;
        var digits = value[1..];
        var degreesLength = latitude ? 2 : 3;
        var degrees = int.Parse(digits[..degreesLength]);
        var minutes = int.Parse(digits.Substring(degreesLength, 2));
        var seconds = digits.Length > degreesLength + 2 ? int.Parse(digits[(degreesLength + 2)..]) : 0;
        return sign * (degrees + (minutes / 60d) + (seconds / 3600d));
    }

    private static double NormalizeDegrees(double value) => (value % 360d + 360d) % 360d;
    private static double NormalizeHours(double value) => (value % 24d + 24d) % 24d;
    private static double ToRadians(double value) => value * Math.PI / 180d;
    private static double ToDegrees(double value) => value * 180d / Math.PI;
}
