namespace DaisyOS.Core.Models;

public sealed record OnboardingPreferences
{
    public const int CurrentVersion = 1;
    public const string DefaultLanguageTag = "en-US";
    public const string DefaultRegionCode = "US";
    public const string DefaultKeyboardLayout = "us";

    public int CompletedVersion { get; init; }

    public string LanguageTag { get; init; } = DefaultLanguageTag;

    public string RegionCode { get; init; } = DefaultRegionCode;

    public string KeyboardLayout { get; init; } = DefaultKeyboardLayout;

    public bool OptionalDiagnosticsEnabled { get; init; }

    public static OnboardingPreferences Normalize(OnboardingPreferences? preferences)
    {
        preferences ??= new OnboardingPreferences();
        return preferences with
        {
            CompletedVersion = Math.Max(0, preferences.CompletedVersion),
            LanguageTag = NormalizeValue(preferences.LanguageTag, DefaultLanguageTag),
            RegionCode = NormalizeValue(preferences.RegionCode, DefaultRegionCode).ToUpperInvariant(),
            KeyboardLayout = NormalizeValue(preferences.KeyboardLayout, DefaultKeyboardLayout).ToLowerInvariant()
        };
    }

    private static string NormalizeValue(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
