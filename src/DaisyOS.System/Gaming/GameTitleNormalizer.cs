using System.Text.RegularExpressions;

namespace DaisyOS.System.Gaming;

public static class GameTitleNormalizer
{
    private static readonly Regex SuffixRegex = new(
        @"\s*(\((DX\d+|Vulkan|OpenGL|x64|x86|Linux|Windows|Steam|Epic|GOG|v?\d+(\.\d+)*)\)|\[(GOG|Steam|Epic)\]|- (Steam|Epic|GOG)|Game of the Year Edition|GOTY Edition|Deluxe Edition|Digital Deluxe Edition)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SymbolRegex = new(@"[™®©]", RegexOptions.Compiled);
    private static readonly Regex VersionRegex = new(@"\bv?\d+(\.\d+)+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExtraSpaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly char[] TrailingPunctuation = [' ', '-', ':', ',', '|', '/'];

    public static string Normalize(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return string.Empty;
        }

        var cleaned = rawTitle;

        // Remove trademark / copyright symbols
        cleaned = SymbolRegex.Replace(cleaned, string.Empty);

        // Strip launcher/DX/platform/version suffixes
        cleaned = SuffixRegex.Replace(cleaned, string.Empty);

        // Strip stray version patterns
        cleaned = VersionRegex.Replace(cleaned, string.Empty);

        // Normalize whitespaces and trim trailing punctuation/dashes
        cleaned = ExtraSpaceRegex.Replace(cleaned, " ").Trim(TrailingPunctuation);

        return cleaned;
    }

    public static double CalculateConfidence(string queryTitle, string candidateTitle)
    {
        var normQuery = Normalize(queryTitle).ToLowerInvariant();
        var normCandidate = Normalize(candidateTitle).ToLowerInvariant();

        if (string.Equals(normQuery, normCandidate, StringComparison.Ordinal))
        {
            return 1.0;
        }

        if (normQuery.Length > 0 && normCandidate.Length > 0)
        {
            if (normQuery.Contains(normCandidate, StringComparison.Ordinal) ||
                normCandidate.Contains(normQuery, StringComparison.Ordinal))
            {
                var minLength = (double)Math.Min(normQuery.Length, normCandidate.Length);
                var maxLength = (double)Math.Max(normQuery.Length, normCandidate.Length);
                return minLength / maxLength;
            }
        }

        return 0.0;
    }
}
