using System.Text;

namespace DaisyOS.Core.Search;

/// <summary>Precomputed catalog text; never reads the filesystem while searching.</summary>
public sealed class SearchDocument
{
    public string UpperName { get; }
    public string[] NameWords { get; }
    public string NameAcronym { get; }
    public string CamelAcronym { get; }
    public string[] UpperSearchTerms { get; }
    public string SearchKey { get; }
    public SearchDocument(string name, string description, IEnumerable<string>? terms = null)
    {
        UpperName = SearchQuery.Normalize(name);
        NameWords = ExtractUpperWords(UpperName);
        NameAcronym = BuildInitials(NameWords);
        CamelAcronym = BuildInitials(ExtractCamelWords(name));
        UpperSearchTerms = (terms ?? []).Select(SearchQuery.Normalize).Distinct(StringComparer.Ordinal).ToArray();
        SearchKey = string.Join("\n", new[] { UpperName, SearchQuery.Normalize(description) }.Concat(UpperSearchTerms));
    }
    private static string[] ExtractUpperWords(string upperText)
    {
        var words = new List<string>(4);
        var start = -1;
        for (var i = 0; i < upperText.Length; i++)
        {
            if (char.IsLetterOrDigit(upperText[i]))
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                words.Add(upperText[start..i]);
                start = -1;
            }
        }
        if (start >= 0) words.Add(upperText[start..]);
        return words.Count == 0 ? Array.Empty<string>() : words.ToArray();
    }

    private static string[] ExtractCamelWords(string text)
    {
        var words = new List<string>(6);
        var current = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (!char.IsLetterOrDigit(ch))
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString().ToUpperInvariant());
                    current.Clear();
                }
                continue;
            }

            if (current.Length > 0 && char.IsUpper(ch) && char.IsLower(text[i - 1]))
            {
                words.Add(current.ToString().ToUpperInvariant());
                current.Clear();
            }
            current.Append(ch);
        }
        if (current.Length > 0) words.Add(current.ToString().ToUpperInvariant());
        return words.Count == 0 ? Array.Empty<string>() : words.ToArray();
    }

    private static string BuildInitials(string[] words)
    {
        if (words.Length == 0) return string.Empty;
        Span<char> chars = words.Length <= 16 ? stackalloc char[words.Length] : new char[words.Length];
        for (var i = 0; i < words.Length; i++) chars[i] = words[i][0];
        return new string(chars);
    }

}
