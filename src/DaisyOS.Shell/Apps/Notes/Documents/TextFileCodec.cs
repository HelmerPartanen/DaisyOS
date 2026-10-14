using System.Text;

namespace DaisyOS.Shell.Apps.Notes.Documents;

public enum LineEnding
{
    Lf,
    CrLf,
    Cr
}

public enum IndentStyle
{
    Unknown,
    Tabs,
    Spaces
}

/// <summary>
/// Everything needed to write a text file back exactly as it was read.
/// <see cref="Text"/> is never normalised: mixed line endings, trailing
/// whitespace and a missing final newline all survive an unedited save.
/// </summary>
public sealed record TextFileContent(
    string Text,
    Encoding Encoding,
    bool HasByteOrderMark,
    LineEnding LineEnding,
    bool HasMixedLineEndings,
    IndentStyle IndentStyle,
    int IndentSize)
{
    public string EncodingDisplayName => TextFileCodec.GetDisplayName(Encoding, HasByteOrderMark);
    public string LineEndingDisplayName => LineEnding switch
    {
        LineEnding.CrLf => "CRLF",
        LineEnding.Cr => "CR",
        _ => "LF"
    };
}

/// <summary>
/// Decodes text files with BOM detection, strict UTF-8 validation and a
/// lossless single-byte fallback, and encodes them back byte-for-byte.
/// </summary>
public static class TextFileCodec
{
    private const int SniffLength = 8192;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    static TextFileCodec()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Returns true when the bytes look like binary data rather than text.</summary>
    public static bool IsProbablyBinary(ReadOnlySpan<byte> bytes)
    {
        if (DetectBom(bytes, out _, out _)) return false;
        var sample = bytes[..Math.Min(bytes.Length, SniffLength)];
        if (sample.IsEmpty) return false;
        if (LooksLikeBomlessUtf16(sample, out _)) return false;

        var suspicious = 0;
        foreach (var b in sample)
        {
            if (b == 0) return true;
            // Control characters other than tab, LF, FF, CR and ESC are rare in text.
            if (b < 0x20 && b is not (0x09 or 0x0A or 0x0C or 0x0D or 0x1B)) suspicious++;
        }
        return suspicious * 10 > sample.Length;
    }

    public static TextFileContent Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Encoding encoding;
        bool hasBom;
        string text;

        if (DetectBom(bytes, out encoding, out var bomLength))
        {
            hasBom = true;
            text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        }
        else if (LooksLikeBomlessUtf16(bytes.AsSpan(0, Math.Min(bytes.Length, SniffLength)), out var utf16))
        {
            hasBom = false;
            encoding = utf16;
            text = encoding.GetString(bytes);
        }
        else
        {
            hasBom = false;
            try
            {
                text = StrictUtf8.GetString(bytes);
                encoding = Utf8NoBom;
            }
            catch (DecoderFallbackException)
            {
                (encoding, text) = DecodeSingleByte(bytes);
            }
        }

        var (lineEnding, mixed) = DetectLineEndings(text);
        var (indentStyle, indentSize) = DetectIndentation(text);
        return new TextFileContent(text, encoding, hasBom, lineEnding, mixed, indentStyle, indentSize);
    }

    public static byte[] Encode(string text, Encoding encoding, bool includeByteOrderMark)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoding);
        var preamble = includeByteOrderMark ? GetPreamble(encoding) : [];
        var body = encoding.GetBytes(text);
        if (preamble.Length == 0) return body;
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    public static byte[] Encode(TextFileContent content, string text) =>
        Encode(text, content.Encoding, content.HasByteOrderMark);

    /// <summary>Converts every line break to <paramref name="lineEnding"/>.</summary>
    public static string NormalizeLineEndings(string text, LineEnding lineEnding)
    {
        var target = lineEnding switch { LineEnding.CrLf => "\r\n", LineEnding.Cr => "\r", _ => "\n" };
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                builder.Append(target);
            }
            else if (c == '\n') builder.Append(target);
            else builder.Append(c);
        }
        return builder.ToString();
    }

    public static (LineEnding LineEnding, bool Mixed) DetectLineEndings(string text)
    {
        int lf = 0, crlf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                else cr++;
            }
            else if (text[i] == '\n') lf++;
        }

        var kinds = (lf > 0 ? 1 : 0) + (crlf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        var dominant = crlf > lf && crlf >= cr ? LineEnding.CrLf
            : cr > lf && cr > crlf ? LineEnding.Cr
            : lf == 0 && crlf == 0 && cr == 0 ? DefaultLineEnding
            : LineEnding.Lf;
        return (dominant, kinds > 1);
    }

    public static LineEnding DefaultLineEnding => OperatingSystem.IsWindows() ? LineEnding.CrLf : LineEnding.Lf;

    public static (IndentStyle Style, int Size) DetectIndentation(string text)
    {
        int tabLines = 0, spaceLines = 0, scanned = 0, previousWidth = 0;
        var widthCounts = new Dictionary<int, int>();

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            if (++scanned > 2000) break;
            if (line.IsWhiteSpace()) continue;
            if (line[0] == '\t') { tabLines++; continue; }
            var width = 0;
            while (width < line.Length && line[width] == ' ') width++;
            if (width > 0)
            {
                spaceLines++;
                var delta = Math.Abs(width - previousWidth);
                if (delta is >= 2 and <= 8) widthCounts[delta] = widthCounts.GetValueOrDefault(delta) + 1;
            }
            previousWidth = width;
        }

        if (tabLines == 0 && spaceLines == 0) return (IndentStyle.Unknown, 4);
        if (tabLines >= spaceLines) return (IndentStyle.Tabs, 4);
        var size = widthCounts.Count == 0 ? 4 : widthCounts.MaxBy(pair => (pair.Value, -pair.Key)).Key;
        return (IndentStyle.Spaces, size);
    }

    public static string GetDisplayName(Encoding encoding, bool hasBom)
    {
        var name = encoding.CodePage switch
        {
            65001 => "UTF-8",
            1200 => "UTF-16 LE",
            1201 => "UTF-16 BE",
            12000 => "UTF-32 LE",
            12001 => "UTF-32 BE",
            1252 => "Windows-1252",
            28591 => "ISO-8859-1",
            _ => encoding.WebName.ToUpperInvariant()
        };
        return hasBom && encoding.CodePage == 65001 ? "UTF-8 with BOM" : name;
    }

    private static bool DetectBom(ReadOnlySpan<byte> bytes, out Encoding encoding, out int length)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]))
        { encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: true); length = 4; return true; }
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
        { encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true); length = 4; return true; }
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        { encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true); length = 3; return true; }
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        { encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true); length = 2; return true; }
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        { encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: true); length = 2; return true; }
        encoding = Utf8NoBom;
        length = 0;
        return false;
    }

    /// <summary>
    /// BOM-less UTF-16 text in Latin scripts has a NUL in almost every
    /// high (or low) byte. Require a strong, one-sided pattern so genuinely
    /// binary files are not misread as text.
    /// </summary>
    private static bool LooksLikeBomlessUtf16(ReadOnlySpan<byte> sample, out Encoding encoding)
    {
        encoding = Utf8NoBom;
        if (sample.Length < 8) return false;
        int evenZeros = 0, oddZeros = 0, pairs = sample.Length / 2;
        for (var i = 0; i + 1 < sample.Length; i += 2)
        {
            if (sample[i] == 0) evenZeros++;
            if (sample[i + 1] == 0) oddZeros++;
        }
        if (oddZeros * 10 >= pairs * 7 && evenZeros * 20 <= pairs)
        { encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false); return true; }
        if (evenZeros * 10 >= pairs * 7 && oddZeros * 20 <= pairs)
        { encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: false); return true; }
        return false;
    }

    /// <summary>
    /// Windows-1252 displays legacy Western files correctly (€, “ ”, …) but
    /// leaves five byte values undefined. Use it only when it round-trips;
    /// otherwise ISO-8859-1, which maps every byte, guarantees a lossless save.
    /// </summary>
    private static (Encoding Encoding, string Text) DecodeSingleByte(byte[] bytes)
    {
        try
        {
            var windows1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var text = windows1252.GetString(bytes);
            if (windows1252.GetBytes(text).AsSpan().SequenceEqual(bytes))
                return (Encoding.GetEncoding(1252), text);
        }
        catch (Exception ex) when (ex is DecoderFallbackException or EncoderFallbackException or ArgumentException or NotSupportedException)
        {
            // Fall through to the lossless Latin-1 mapping.
        }
        return (Encoding.Latin1, Encoding.Latin1.GetString(bytes));
    }

    private static byte[] GetPreamble(Encoding encoding) => encoding.CodePage switch
    {
        65001 => [0xEF, 0xBB, 0xBF],
        1200 => [0xFF, 0xFE],
        1201 => [0xFE, 0xFF],
        12000 => [0xFF, 0xFE, 0x00, 0x00],
        12001 => [0x00, 0x00, 0xFE, 0xFF],
        _ => encoding.GetPreamble()
    };
}
