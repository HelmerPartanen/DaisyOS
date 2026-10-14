using System.Text;

namespace DaisyOS.System.Processes;

public static class DesktopExecParser
{
    public static IReadOnlyList<string>? Parse(string exec)
    {
        var withoutFieldCodes = RemoveFieldCodes(exec);
        var tokens = SplitArguments(withoutFieldCodes);
        return tokens.Count == 0 ? null : tokens;
    }

    private static string RemoveFieldCodes(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 1 < value.Length)
            {
                var code = value[i + 1];
                if (code == '%')
                {
                    builder.Append('%');
                }

                i++;
                continue;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> SplitArguments(string value)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        var escaping = false;

        foreach (var character in value)
        {
            if (escaping)
            {
                current.Append(character);
                escaping = false;
                continue;
            }

            if (character == '\\')
            {
                escaping = true;
                continue;
            }

            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                AddToken();
                continue;
            }

            current.Append(character);
        }

        if (escaping)
        {
            current.Append('\\');
        }

        if (quote != '\0')
        {
            return [];
        }

        AddToken();
        return tokens;

        void AddToken()
        {
            if (current.Length == 0)
            {
                return;
            }

            tokens.Add(current.ToString());
            current.Clear();
        }
    }
}
