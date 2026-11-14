using System.Globalization;
using System.Text.RegularExpressions;

namespace DaisyOS.Core.Search;

public sealed record SearchFileFilters(string? Extension = null, string? Location = null,
    long? MinimumSize = null, long? MaximumSize = null, bool? IsDirectory = null)
{
    public bool IsActive => Extension is not null || Location is not null || MinimumSize is not null || MaximumSize is not null || IsDirectory is not null;
    internal static (string Text, SearchFileFilters Filters) Extract(string text)
    {
        string? extension = null, location = null;
        long? min = null, max = null;
        bool? directory = null;
        var rest = Regex.Replace(text, "(?i)\\b(?<key>ext|type|in|kind|size):(?<value>\"[^\"]*\"|[^\\s]+)", match =>
        {
            var value = match.Groups["value"].Value.Trim('"');
            switch (match.Groups["key"].Value.ToLowerInvariant())
            {
                case "ext": case "type":
                    if (value.All(char.IsLetterOrDigit)) extension = "." + value.ToLowerInvariant(); else return match.Value;
                    break;
                case "in": location = value; break;
                case "kind":
                    if (value.Equals("folder", StringComparison.OrdinalIgnoreCase)) directory = true;
                    else if (value.Equals("file", StringComparison.OrdinalIgnoreCase)) directory = false;
                    else return match.Value;
                    break;
                case "size":
                    var size = Regex.Match(value, "^(?<op>[<>])(?<amount>[0-9]+(?:\\.[0-9]+)?)(?<unit>B|KB|MB|GB)?$", RegexOptions.IgnoreCase);
                    if (!size.Success || !double.TryParse(size.Groups["amount"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)) return match.Value;
                    var factor = size.Groups["unit"].Value.ToUpperInvariant() switch { "KB" => 1024d, "MB" => 1048576d, "GB" => 1073741824d, _ => 1d };
                    if (amount * factor > long.MaxValue) return match.Value;
                    if (size.Groups["op"].Value == ">") min = (long)(amount * factor); else max = (long)(amount * factor);
                    break;
            }
            return " ";
        });
        return (rest, new(extension, location, min, max, directory));
    }
}
