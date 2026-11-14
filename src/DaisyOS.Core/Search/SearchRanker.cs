namespace DaisyOS.Core.Search;

public static class SearchRanker
{
    public static int Score(SearchDocument item, SearchQuery searchQuery)
    {
        var query = searchQuery.NormalizedText;
        var tokens = searchQuery.Tokens;
        if (tokens.Count == 0) return 0;
        return ScoreMatch(item, query, tokens);
    }

    private static int ScoreMatch(SearchDocument item, string query, IReadOnlyList<string> tokens)
    {
        var upperName = item.UpperName;
        if (string.Equals(upperName, query, StringComparison.Ordinal))
        {
            return 1000;
        }

        if (upperName.StartsWith(query, StringComparison.Ordinal))
        {
            return 860 - Math.Min(40, upperName.Length - query.Length);
        }

        var words = item.NameWords;
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].StartsWith(query, StringComparison.Ordinal))
            {
                return 740 - Math.Min(30, i * 8);
            }
        }

        if (query.Length >= 2)
        {
            if (string.Equals(item.NameAcronym, query, StringComparison.Ordinal) ||
                string.Equals(item.CamelAcronym, query, StringComparison.Ordinal))
            {
                return 680;
            }
            if (item.NameAcronym.StartsWith(query, StringComparison.Ordinal) ||
                item.CamelAcronym.StartsWith(query, StringComparison.Ordinal))
            {
                return 640;
            }
        }

        if (tokens.Count > 1 && MatchesWordPrefixesInOrder(words, tokens))
        {
            return 700;
        }

        var nameHit = upperName.IndexOf(query, StringComparison.Ordinal);
        if (nameHit >= 0)
        {
            return 520 - Math.Min(60, nameHit * 2);
        }

        foreach (var term in item.UpperSearchTerms)
        {
            if (string.Equals(term, query, StringComparison.Ordinal)) return 460;
            if (term.StartsWith(query, StringComparison.Ordinal)) return 410;
        }

        if (item.SearchKey.Contains(query, StringComparison.Ordinal))
        {
            return 220;
        }

        if (tokens.Count > 1)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!item.SearchKey.Contains(tokens[i], StringComparison.Ordinal))
                {
                    return 0;
                }
            }
            return 200;
        }

        if (tokens.Count == 1 && query.Length is >= 4 and <= 64)
            for (var i = 0; i < Math.Min(8, words.Length); i++)
                if (IsWithinOneEdit(query, words[i])) return 150;
        return 0;
    }

    private static bool IsWithinOneEdit(string left, string right)
    {
        if (right.Length > 64 || Math.Abs(left.Length - right.Length) > 1) return false;
        var i = 0;
        while (i < left.Length && i < right.Length && left[i] == right[i]) i++;
        if (i == Math.Min(left.Length, right.Length)) return true;
        if (left.Length == right.Length)
        {
            if (left.AsSpan(i + 1).SequenceEqual(right.AsSpan(i + 1))) return true;
            return i + 1 < left.Length && left[i] == right[i + 1] && left[i + 1] == right[i] &&
                left.AsSpan(i + 2).SequenceEqual(right.AsSpan(i + 2));
        }
        return left.Length > right.Length
            ? left.AsSpan(i + 1).SequenceEqual(right.AsSpan(i))
            : left.AsSpan(i).SequenceEqual(right.AsSpan(i + 1));
    }

    private static bool MatchesWordPrefixesInOrder(string[] words, IReadOnlyList<string> tokens)
    {
        var wordIndex = 0;
        for (var t = 0; t < tokens.Count; t++)
        {
            var matched = false;
            while (wordIndex < words.Length)
            {
                if (words[wordIndex++].StartsWith(tokens[t], StringComparison.Ordinal))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched) return false;
        }
        return true;
    }

}
