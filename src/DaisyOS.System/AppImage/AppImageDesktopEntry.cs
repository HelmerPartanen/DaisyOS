using System.Text;

namespace DaisyOS.System.AppImage;

/// <summary>Reads and writes the small desktop-entry surface owned by AppImage installs.</summary>
internal static class AppImageDesktopEntry
{
    internal const string OwnershipKey = "X-DaisyOS-AppImage";

    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var inMainGroup = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            if (line[0] == '[')
            {
                inMainGroup = line == "[Desktop Entry]";
                continue;
            }
            if (!inMainGroup) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            if (key.Length == 0) continue;
            values[key] = line[(separator + 1)..].Trim();
        }
        return values;
    }

    public static string Build(AppImageInstallation installation)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[Desktop Entry]");
        builder.AppendLine("Type=Application");
        builder.AppendLine("Version=1.0");
        builder.Append("Name=").AppendLine(CleanValue(installation.Name, "AppImage"));
        if (!string.IsNullOrWhiteSpace(installation.Comment))
            builder.Append("Comment=").AppendLine(CleanValue(installation.Comment, ""));
        builder.Append("Exec=").AppendLine(QuoteExec(installation.AppImagePath));
        builder.Append("Icon=").AppendLine(string.IsNullOrWhiteSpace(installation.IconPath)
            ? "application-x-executable"
            : CleanValue(installation.IconPath, "application-x-executable"));
        builder.AppendLine("Terminal=false");
        builder.AppendLine("StartupNotify=true");
        if (!string.IsNullOrWhiteSpace(installation.StartupWMClass))
            builder.Append("StartupWMClass=").AppendLine(CleanValue(installation.StartupWMClass, ""));
        if (!string.IsNullOrWhiteSpace(installation.Categories))
            builder.Append("Categories=").AppendLine(CleanCategories(installation.Categories));
        builder.Append(OwnershipKey).Append('=').AppendLine(installation.DesktopId);
        if (!string.IsNullOrWhiteSpace(installation.Version))
            builder.Append("X-AppImage-Version=").AppendLine(CleanValue(installation.Version, ""));
        return builder.ToString();
    }

    public static bool IsOwnedBy(string desktopText, string desktopId) =>
        Parse(desktopText).TryGetValue(OwnershipKey, out var value) &&
        string.Equals(value, desktopId, StringComparison.Ordinal);

    public static string QuoteExec(string path)
    {
        // Desktop Entry Exec values use double quotes; these characters are
        // reserved inside them. A literal percent sign starts a field code.
        var escaped = path
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }

    public static string CleanValue(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var cleaned = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
        return cleaned.Length == 0 ? fallback : cleaned.Length > 512 ? cleaned[..512] : cleaned;
    }

    public static string CleanCategories(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or ';')
                builder.Append(character);
        }
        return builder.ToString().Trim(';');
    }
}
