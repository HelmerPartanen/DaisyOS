using System.Xml;
using System.Xml.Linq;
using DaisyOS.Shell.Services.Appearance;

namespace DaisyOS.Shell.Services.Wallpaper;

internal sealed class HeicWallpaperSchedule
{
    private readonly Dictionary<string, Dictionary<string, object?>> _metadata = new(StringComparer.Ordinal);

    internal static HeicWallpaperSchedule FromXmp(string xmp)
    {
        var schedule = new HeicWallpaperSchedule();
        try
        {
            using var reader = XmlReader.Create(new StringReader(xmp), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024
            });
            var document = XDocument.Load(reader);
            var fields = document.Descendants().Select(element => (element.Name, element.Value))
                .Concat(document.Descendants().Attributes().Select(attribute => (attribute.Name, attribute.Value)));
            foreach (var (name, value) in fields)
            {
                if (name.NamespaceName != "http://ns.apple.com/namespace/1.0/" || name.LocalName is not ("apr" or "h24" or "solar")) continue;
                try { schedule._metadata[name.LocalName] = WallpaperPropertyList.Read(Convert.FromBase64String(value)); }
                catch (Exception error) when (error is FormatException or InvalidDataException or OverflowException or ArgumentException or IndexOutOfRangeException) { }
            }
        }
        catch (XmlException) { }
        return schedule;
    }

    internal int SelectFrame(int count, int primary, bool dark, DateTimeOffset now)
    {
        if (_metadata.TryGetValue("h24", out var timed) && timed.GetValueOrDefault("ti") is object?[] times)
        {
            var entries = times.OfType<Dictionary<string, object?>>()
                .Where(entry => Index(entry.GetValueOrDefault("i"), count) >= 0 && Number(entry.GetValueOrDefault("t")) is >= 0 and < 1)
                .OrderBy(entry => Number(entry["t"])).ToArray();
            if (entries.Length > 0)
            {
                var fraction = now.LocalDateTime.TimeOfDay.TotalDays;
                return Index((entries.LastOrDefault(entry => Number(entry["t"]) <= fraction) ?? entries[^1])["i"], count);
            }
        }
        if (_metadata.TryGetValue("solar", out var solar) && solar.GetValueOrDefault("si") is object?[] positions)
        {
            var entries = positions.OfType<Dictionary<string, object?>>()
                .Where(entry => Index(entry.GetValueOrDefault("i"), count) >= 0 &&
                    Number(entry.GetValueOrDefault("a")) is >= -90 and <= 90 &&
                    Number(entry.GetValueOrDefault("z")) is >= 0 and <= 360).ToArray();
            if (entries.Length > 0)
            {
                var sun = SunPosition(now, SolarSchedule.LocalCoordinates());
                var selected = entries.MaxBy(entry => Similarity(sun, (Number(entry["a"]), Number(entry["z"]))));
                return Index(selected!["i"], count);
            }
        }
        foreach (var key in new[] { "apr", "h24", "solar" })
        {
            if (!_metadata.TryGetValue(key, out var metadata)) continue;
            var appearance = metadata.GetValueOrDefault("ap") as Dictionary<string, object?> ?? metadata;
            var index = Index(appearance.GetValueOrDefault(dark ? "d" : "l"), count);
            if (index >= 0) return index;
        }
        return primary;
    }

    private static double Number(object? value) => value is double number && double.IsFinite(number) ? number : double.NaN;
    private static int Index(object? value, int count) => Number(value) is var number && number >= 0 && number < count && number == Math.Truncate(number) ? (int)number : -1;
    private static double Rad(double degrees) => degrees * Math.PI / 180;
    private static double Deg(double radians) => radians * 180 / Math.PI;
    private static double Similarity((double Altitude, double Azimuth) a, (double Altitude, double Azimuth) b) =>
        Math.Sin(Rad(a.Altitude)) * Math.Sin(Rad(b.Altitude)) + Math.Cos(Rad(a.Altitude)) * Math.Cos(Rad(b.Altitude)) * Math.Cos(Rad(a.Azimuth - b.Azimuth));

    // Solar coordinates use the same timezone-derived location as automatic appearance.
    internal static (double Altitude, double Azimuth) SunPosition(DateTimeOffset time, (double Latitude, double Longitude) location)
    {
        var days = time.ToUnixTimeMilliseconds() / 86400000d + 2440587.5 - 2451545;
        var meanLongitude = Rad((280.46 + 0.9856474 * days) % 360);
        var anomaly = Rad((357.528 + 0.9856003 * days) % 360);
        var longitude = meanLongitude + Rad(1.915) * Math.Sin(anomaly) + Rad(0.020) * Math.Sin(2 * anomaly);
        var obliquity = Rad(23.439 - 0.0000004 * days);
        var ascension = Math.Atan2(Math.Cos(obliquity) * Math.Sin(longitude), Math.Cos(longitude));
        var declination = Math.Asin(Math.Sin(obliquity) * Math.Sin(longitude));
        var hourAngle = Rad((280.46061837 + 360.98564736629 * days + location.Longitude) % 360) - ascension;
        var latitude = Rad(location.Latitude);
        var altitude = Math.Asin(Math.Sin(latitude) * Math.Sin(declination) + Math.Cos(latitude) * Math.Cos(declination) * Math.Cos(hourAngle));
        var azimuth = Math.Atan2(Math.Sin(hourAngle), Math.Cos(hourAngle) * Math.Sin(latitude) - Math.Tan(declination) * Math.Cos(latitude));
        return (Deg(altitude), (Deg(azimuth) + 540) % 360);
    }
}
