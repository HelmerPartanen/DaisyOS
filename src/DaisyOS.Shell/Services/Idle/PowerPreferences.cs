using System;
using System.Collections.Generic;

namespace DaisyOS.Shell.Services.Idle;

/// <summary>
/// Preference keys and timeout choices for Settings › System › Power.
/// Values are stored as stable English option strings (the same contract as
/// other shell pickers) and parsed to seconds here.
/// </summary>
public static class PowerPreferences
{
    public const string LockTimeoutKey = "power.lock-timeout";
    public const string ScreenOffTimeoutKey = "power.screen-off-timeout";
    public const string SuspendTimeoutKey = "power.suspend-timeout";
    public const string SuspendOnBatteryTimeoutKey = "power.suspend-timeout-battery";
    public const string LockOnSuspendKey = "power.lock-on-sleep";
    public const string PauseLiveWallpaperOnBatteryKey = "power.pause-live-wallpaper-on-battery";
    // Retain the persisted key for compatibility; gaming now suspends explicit
    // maintenance even when hidden-desktop reclamation is enabled.
    public const string ReleaseMemoryWhenGamingKey = "power.release-memory-when-gaming";

    /// <summary>Legacy key used before the Power page existed (lock timeout).</summary>
    public const string LegacyScreenTimeoutKey = "power.screen-timeout";

    public const string Never = "Never";

    public const string DefaultLockTimeout = "10 minutes";
    public const string DefaultScreenOffTimeout = "5 minutes";
    public const string DefaultSuspendTimeout = Never;
    public const string DefaultSuspendOnBatteryTimeout = "15 minutes";

    public static readonly string[] LockTimeoutOptions =
        ["1 minute", "2 minutes", "5 minutes", "10 minutes", "15 minutes", "30 minutes", "1 hour", Never];

    public static readonly string[] ScreenOffTimeoutOptions =
        ["1 minute", "2 minutes", "5 minutes", "10 minutes", "15 minutes", "30 minutes", "1 hour", Never];

    public static readonly string[] SuspendTimeoutOptions =
        ["5 minutes", "10 minutes", "15 minutes", "30 minutes", "1 hour", "2 hours", Never];

    private static readonly Dictionary<string, uint> Seconds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1 minute"] = 60,
        ["2 minutes"] = 120,
        ["5 minutes"] = 300,
        ["10 minutes"] = 600,
        ["15 minutes"] = 900,
        ["30 minutes"] = 1800,
        ["1 hour"] = 3600,
        ["2 hours"] = 7200,
        [Never] = 0,
    };

    /// <summary>Returns seconds for a stored option, or <paramref name="fallbackSeconds"/> if unknown.</summary>
    public static uint ParseSeconds(string? option, uint fallbackSeconds) =>
        option is not null && Seconds.TryGetValue(option.Trim(), out var value) ? value : fallbackSeconds;

    public static bool ParseBool(string? value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;

    public static bool IsPowerKey(string key) => key.StartsWith("power.", StringComparison.Ordinal);
}
