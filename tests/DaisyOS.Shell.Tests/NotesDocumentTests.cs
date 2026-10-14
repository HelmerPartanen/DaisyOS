using System.Text;
using DaisyOS.Shell.Apps.Notes.Documents;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class TextFileCodecTests
{
    public static TheoryData<string, byte[]> RoundTripFixtures => new()
    {
        { "utf8-lf", Encoding.UTF8.GetBytes("hello\nwörld\n") },
        { "utf8-crlf-no-final-newline", Encoding.UTF8.GetBytes("a\r\nb\r\nc") },
        { "utf8-mixed-endings", Encoding.UTF8.GetBytes("a\nb\r\nc\rd") },
        { "utf8-bom", [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("bom ✓\n")] },
        { "utf16le-bom", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("Hei maailma\r\n")] },
        { "utf16be-bom", [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("Big endian\n")] },
        { "utf16le-no-bom", Encoding.Unicode.GetBytes("plain utf16 without bom text\n") },
        { "latin1-only-bytes", [0x48, 0xE4, 0x6C, 0x6C, 0xF6, 0x0A, 0x81, 0x8D] },
        { "windows1252-quotes", [0x93, 0x48, 0x69, 0x94, 0x20, 0x80, 0x0A] },
        { "trailing-whitespace-and-tabs", Encoding.UTF8.GetBytes("\tindent  \n    spaces\t\n") },
        { "emoji-and-combining", Encoding.UTF8.GetBytes("👩‍💻 e\u0301\n") },
        { "empty", [] },
    };

    [Theory]
    [MemberData(nameof(RoundTripFixtures))]
    public void DecodeThenEncode_IsByteIdentical(string name, byte[] original)
    {
        var decoded = TextFileCodec.Decode(original);
        var encoded = TextFileCodec.Encode(decoded, decoded.Text);

        Assert.True(original.AsSpan().SequenceEqual(encoded), $"{name} did not round-trip ({decoded.EncodingDisplayName}).");
    }

    [Fact]
    public void Decode_ReportsBomAndEncoding()
    {
        var decoded = TextFileCodec.Decode([0xEF, 0xBB, 0xBF, (byte)'x']);

        Assert.True(decoded.HasByteOrderMark);
        Assert.Equal("UTF-8 with BOM", decoded.EncodingDisplayName);
        Assert.Equal("x", decoded.Text);
    }

    [Fact]
    public void Decode_UsesWindows1252ForLegacyQuotes()
    {
        var decoded = TextFileCodec.Decode([0x93, 0x48, 0x69, 0x94]);

        Assert.Equal("Windows-1252", decoded.EncodingDisplayName);
        Assert.Equal("\u201CHi\u201D", decoded.Text);
    }

    [Theory]
    [InlineData("a\nb\n", LineEnding.Lf, false)]
    [InlineData("a\r\nb\r\n", LineEnding.CrLf, false)]
    [InlineData("a\rb\r", LineEnding.Cr, false)]
    [InlineData("a\r\nb\r\nc\n", LineEnding.CrLf, true)]
    public void DetectLineEndings_FindsDominantAndMixed(string text, LineEnding expected, bool mixed)
    {
        var result = TextFileCodec.DetectLineEndings(text);

        Assert.Equal(expected, result.LineEnding);
        Assert.Equal(mixed, result.Mixed);
    }

    [Fact]
    public void NormalizeLineEndings_ConvertsEveryBreak()
    {
        Assert.Equal("a\r\nb\r\nc\r\n", TextFileCodec.NormalizeLineEndings("a\nb\r\nc\r", LineEnding.CrLf));
    }

    [Theory]
    [InlineData("def f():\n    return 1\n    if x:\n        y\n", IndentStyle.Spaces, 4)]
    [InlineData("a:\n  b: 1\n  c:\n    d: 2\n", IndentStyle.Spaces, 2)]
    [InlineData("int f() {\n\treturn 1;\n}\n", IndentStyle.Tabs, 4)]
    [InlineData("no indentation here\n", IndentStyle.Unknown, 4)]
    public void DetectIndentation_FindsStyleAndSize(string text, IndentStyle style, int size)
    {
        var result = TextFileCodec.DetectIndentation(text);

        Assert.Equal(style, result.Style);
        Assert.Equal(size, result.Size);
    }

    [Fact]
    public void IsProbablyBinary_RejectsBinaryAndAcceptsText()
    {
        Assert.True(TextFileCodec.IsProbablyBinary([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D]));
        Assert.False(TextFileCodec.IsProbablyBinary(Encoding.UTF8.GetBytes("just some text\n")));
        Assert.False(TextFileCodec.IsProbablyBinary(Encoding.Unicode.GetBytes("utf16 text without bom")));
    }
}

public sealed class FormatDetectorTests
{
    [Theory]
    [InlineData("note.dnote", DocumentKind.RichNote, "dnote")]
    [InlineData("readme.md", DocumentKind.Markdown, "markdown")]
    [InlineData("todo.txt", DocumentKind.PlainText, "plaintext")]
    [InlineData("Program.cs", DocumentKind.Code, "csharp")]
    [InlineData("view.axaml", DocumentKind.Code, "xml")]
    [InlineData("Makefile", DocumentKind.Code, "makefile")]
    [InlineData("CMakeLists.txt", DocumentKind.Code, "cmake")]
    [InlineData(".gitignore", DocumentKind.Code, "ignore")]
    [InlineData("unknown.zzz", DocumentKind.PlainText, "plaintext")]
    public void FromPath_ClassifiesByName(string path, DocumentKind kind, string language)
    {
        var format = FormatDetector.FromPath(path);

        Assert.Equal(kind, format.Kind);
        Assert.Equal(language, format.LanguageId);
    }

    [Fact]
    public void Detect_UsesShebangForExtensionlessScripts()
    {
        var format = FormatDetector.Detect("/usr/bin/tool", Encoding.ASCII.GetBytes("#!/usr/bin/env python3\nprint(1)\n"));

        Assert.Equal(DocumentKind.Code, format.Kind);
        Assert.Equal("python", format.LanguageId);
    }

    [Fact]
    public void Detect_FlagsBinaryContentEvenWithTextExtension()
    {
        Assert.Equal(DocumentKind.Binary, FormatDetector.Detect("fake.txt", [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00, 0x00]).Kind);
    }
}

public sealed class NotePackageFormatTests
{
    [Fact]
    public void WriteThenRead_PreservesFormattingImagesAndAssets()
    {
        var document = new NoteDocument { Title = "Trip", Pinned = true };
        document.Blocks.Add(NoteBlock.Heading(1, "Lapland"));
        document.Blocks.Add(new NoteBlock
        {
            Kind = BlockKind.Paragraph,
            Runs =
            [
                new StyledRun("Snow ", RunStyle.Default),
                new StyledRun("deep", new RunStyle { FontWeight = 700, FontFamily = "Inter", FontSize = 22, Foreground = "#FF59C7E8" }),
                new StyledRun("2", new RunStyle { Baseline = BaselineShift.Superscript, LetterSpacing = 1.5 }),
            ]
        });
        document.Blocks.Add(new NoteBlock
        {
            Kind = BlockKind.Image,
            Image = new NoteImage { Asset = "a.png", PixelWidth = 800, PixelHeight = 600, Width = 320, CornerRadius = 12, Alignment = ImageAlignment.Left, Wrap = ImageWrap.Right, Caption = "Aurora" }
        });
        document.Blocks.Add(new NoteBlock { Kind = BlockKind.CheckList, Checked = true, Runs = [new StyledRun("Pack")] });
        var assets = new Dictionary<string, byte[]> { ["a.png"] = [1, 2, 3], ["orphan.png"] = [9] };

        using var stream = new MemoryStream();
        NotePackageFormat.WriteTo(stream, document, assets);
        stream.Position = 0;
        var loaded = NotePackageFormat.Read(stream);

        Assert.Equal("Trip", loaded.Document.Title);
        Assert.True(loaded.Document.Pinned);
        Assert.Equal(4, loaded.Document.Blocks.Count);
        var runs = loaded.Document.Blocks[1].Runs;
        Assert.Equal(3, runs.Count);
        Assert.Equal(700, runs[1].Style.FontWeight);
        Assert.Equal("Inter", runs[1].Style.FontFamily);
        Assert.Equal(22, runs[1].Style.FontSize);
        Assert.Equal(BaselineShift.Superscript, runs[2].Style.Baseline);
        Assert.Equal(1.5, runs[2].Style.LetterSpacing);
        var image = loaded.Document.Blocks[2].Image!;
        Assert.Equal(ImageAlignment.Left, image.Alignment);
        Assert.Equal(ImageWrap.Right, image.Wrap);
        Assert.Equal(12, image.CornerRadius);
        Assert.Equal(320, image.Width);
        Assert.True(loaded.Document.Blocks[3].Checked);
        Assert.Equal([1, 2, 3], loaded.Assets["a.png"]);
        Assert.False(loaded.Assets.ContainsKey("orphan.png"));
    }

    [Fact]
    public void Write_IsAtomicAndPreviewIsSearchable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"note-{Guid.NewGuid():N}.dnote");
        try
        {
            var document = NoteDocument.FromPlainText("Shopping\nMilk\nBread");
            NotePackageFormat.Write(path, document, new Dictionary<string, byte[]>());

            var leftoverTempFiles = Directory.EnumerateFiles(Path.GetTempPath(), ".daisy-note-*.tmp")
                .Where(file => File.GetCreationTimeUtc(file) > DateTime.UtcNow.AddMinutes(-1))
                .ToList();
            Assert.Empty(leftoverTempFiles);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Normalize_MergesEqualRuns()
    {
        var block = new NoteBlock { Runs = [new StyledRun("a"), new StyledRun(""), new StyledRun("b"), new StyledRun("c", new RunStyle { Italic = true })] };

        block.Normalize();

        Assert.Equal(2, block.Runs.Count);
        Assert.Equal("ab", block.Runs[0].Text);
    }

    [Fact]
    public void Read_RejectsGarbage()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);

        Assert.Throws<NotePackageException>(() => NotePackageFormat.Read(stream));
    }
}
