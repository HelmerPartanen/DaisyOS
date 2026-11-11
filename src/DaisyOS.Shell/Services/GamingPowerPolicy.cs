namespace DaisyOS.Shell.Services;

/// <summary>
/// Detects external power without changing platform policy. Unknown/desktop
/// systems remain enabled for compatibility; a detected battery session is
/// opt-in because GameMode may request the performance governor.
/// </summary>
internal static class GamingPowerPolicy
{
    public const string BatteryBoostPreferenceKey = "gaming.allow-battery-boost";
    public const string PerformanceProfilePreferenceKey = "gaming.use-performance-profile";
}
