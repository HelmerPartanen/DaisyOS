using System.Globalization;

namespace DaisyOS.Core.Models;

/// <summary>Resolves translated desktop-entry presentation without changing launch identifiers.</summary>
public static class DesktopEntryLocalization
{
    public static string? GetValue(IReadOnlyDictionary<string, string> entry, string key, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        for (var current = culture; !string.IsNullOrEmpty(current.Name); current = current.Parent)
        {
            var localizedKey = $"{key}[{current.Name.Replace('-', '_')}]";
            if (entry.TryGetValue(localizedKey, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        }
        return entry.TryGetValue(key, out var fallback) && !string.IsNullOrWhiteSpace(fallback) ? fallback : null;
    }
}
