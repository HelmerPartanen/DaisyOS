namespace DaisyOS.Shell.Services;

/// <summary>
/// Detects external power without changing platform policy. Unknown/desktop
/// systems remain enabled for compatibility; a detected battery session is
/// opt-in because GameMode may request the performance governor.
/// </summary>
internal static class GamingPowerPolicy
{
    public const string BatteryBoostPreferenceKey = "gaming.allow-battery-boost";

    public static bool ShouldEnablePerformanceBoost(bool allowBatteryBoost)
    {
        try
        {
            var supplies = Directory.Exists("/sys/class/power_supply")
                ? Directory.EnumerateDirectories("/sys/class/power_supply").ToArray()
                : [];
            var hasBattery = supplies.Any(path => Read(Path.Combine(path, "type")) == "Battery");
            var onlineMains = supplies.Any(path =>
                Read(Path.Combine(path, "type")) == "Mains" && Read(Path.Combine(path, "online")) == "1");

            if (onlineMains || !hasBattery) return true;
            return allowBatteryBoost;
        }
        catch (IOException)
        {
            // Preserve Gaming Mode on systems whose power_supply sysfs is
            // unavailable; do not infer battery operation from missing data.
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string Read(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }
}
