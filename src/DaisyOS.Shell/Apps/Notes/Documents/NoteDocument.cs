using System.Text;
using System.Text.Json.Serialization;

namespace DaisyOS.Shell.Apps.Notes.Documents;

public enum BlockKind
{
    Paragraph,
    Heading,
    BulletedList,
    NumberedList,
    CheckList,
    Quote,
    Code,
    Divider,
    Image
}

public enum BaselineShift
{
    Normal,
    Superscript,
    Subscript
}

public enum ImageAlignment
{
    // Center must stay the zero value: JSON omits default values.
    Center,
    Left,
    Right,
    FullWidth
}

public enum ImageWrap
{
    /// <summary>The image occupies its own line.</summary>
    None,
    /// <summary>Image on the left, following text flows on its right.</summary>
    Left,
    /// <summary>Image on the right, following text flows on its left.</summary>
    Right
}

/// <summary>
/// Character-level formatting. Null members inherit the block/default style,
/// so a run only stores what the user explicitly changed. Records give value
/// equality, which lets adjacent runs with identical formatting merge.
/// </summary>
public sealed record RunStyle
{
    public static readonly RunStyle Default = new();

    public string? FontFamily { get; init; }
    public double? FontSize { get; init; }
    /// <summary>CSS-style weight 1–1000 (400 regular, 700 bold).</summary>
    public int? FontWeight { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }
    /// <summary>#RRGGBB or #AARRGGBB, or a theme token such as "accent".</summary>
    public string? Foreground { get; init; }
    public string? Highlight { get; init; }
    /// <summary>Extra spacing between letters in logical pixels.</summary>
    public double? LetterSpacing { get; init; }
    public BaselineShift Baseline { get; init; }
    public string? Link { get; init; }
    /// <summary>Inline code formatting (monospace chip).</summary>
    public bool Code { get; init; }

    [JsonIgnore]
    public bool IsDefault => Equals(Default);
}

public sealed class StyledRun
{
    public StyledRun() { }

    public StyledRun(string text, RunStyle? style = null)
    {
        Text = text;
        Style = style ?? RunStyle.Default;
    }

    public string Text { get; set; } = string.Empty;
    public RunStyle Style { get; set; } = RunStyle.Default;
}

public sealed class NoteImage
{
    /// <summary>File name inside the package's <c>assets/</c> folder.</summary>
    public string Asset { get; set; } = string.Empty;
    /// <summary>Natural pixel size of the stored image.</summary>
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    /// <summary>Displayed width in logical pixels; null means natural size capped to the column.</summary>
    public double? Width { get; set; }
    public double CornerRadius { get; set; }
    public ImageAlignment Alignment { get; set; } = ImageAlignment.Center;
    public ImageWrap Wrap { get; set; }
    public string? Caption { get; set; }
    public string? AltText { get; set; }
}

public sealed class NoteBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public BlockKind Kind { get; set; }
    /// <summary>Heading level (1–3) or list nesting depth (0-based).</summary>
    public int Level { get; set; }
    public bool Checked { get; set; }
    /// <summary>Highlighting language for code blocks.</summary>
    public string? Language { get; set; }
    public TextAlignmentKind TextAlignment { get; set; }
    public List<StyledRun> Runs { get; set; } = [];
    public NoteImage? Image { get; set; }

    [JsonIgnore]
    public bool IsTextBlock => Kind is not (BlockKind.Divider or BlockKind.Image);

    [JsonIgnore]
    public string PlainText
    {
        get
        {
            if (Runs.Count == 1) return Runs[0].Text;
            var builder = new StringBuilder();
            foreach (var run in Runs) builder.Append(run.Text);
            return builder.ToString();
        }
    }

    [JsonIgnore]
    public int TextLength => Runs.Sum(run => run.Text.Length);

    public static NoteBlock Paragraph(string text = "", RunStyle? style = null) =>
        new() { Kind = BlockKind.Paragraph, Runs = text.Length == 0 ? [] : [new StyledRun(text, style)] };

    public static NoteBlock Heading(int level, string text) =>
        new() { Kind = BlockKind.Heading, Level = Math.Clamp(level, 1, 3), Runs = [new StyledRun(text)] };

    /// <summary>Merges adjacent runs with equal formatting and drops empty runs.</summary>
    public void Normalize()
    {
        if (Runs.Count == 0) return;
        var merged = new List<StyledRun>(Runs.Count);
        foreach (var run in Runs)
        {
            if (run.Text.Length == 0) continue;
            if (merged.Count > 0 && merged[^1].Style.Equals(run.Style))
                merged[^1] = new StyledRun(merged[^1].Text + run.Text, run.Style);
            else
                merged.Add(new StyledRun(run.Text, run.Style));
        }
        Runs = merged;
    }

    public NoteBlock Clone() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Kind = Kind,
        Level = Level,
        Checked = Checked,
        Language = Language,
        TextAlignment = TextAlignment,
        Runs = Runs.Select(run => new StyledRun(run.Text, run.Style)).ToList(),
        Image = Image is null ? null : new NoteImage
        {
            Asset = Image.Asset, PixelWidth = Image.PixelWidth, PixelHeight = Image.PixelHeight,
            Width = Image.Width, CornerRadius = Image.CornerRadius, Alignment = Image.Alignment,
            Wrap = Image.Wrap, Caption = Image.Caption, AltText = Image.AltText
        }
    };
}

public enum TextAlignmentKind
{
    Start,
    Center,
    End,
    Justify
}

/// <summary>Document-wide defaults that runs inherit from.</summary>
public sealed class NoteDefaults
{
    public string? FontFamily { get; set; }
    public double FontSize { get; set; } = 16;
    public int FontWeight { get; set; } = 400;
    public double LineHeight { get; set; } = 1.5;
}

/// <summary>A rich note: an ordered list of blocks with styled text runs.</summary>
public sealed class NoteDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string? Title { get; set; }
    public bool Pinned { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public NoteDefaults Defaults { get; set; } = new();
    public List<NoteBlock> Blocks { get; set; } = [];

    public static NoteDocument CreateEmpty() => new() { Blocks = [NoteBlock.Paragraph()] };

    /// <summary>Builds a rich note from plain text, one paragraph per line.</summary>
    public static NoteDocument FromPlainText(string text)
    {
        var document = new NoteDocument();
        foreach (var line in text.AsSpan().EnumerateLines())
            document.Blocks.Add(NoteBlock.Paragraph(line.ToString()));
        if (document.Blocks.Count == 0) document.Blocks.Add(NoteBlock.Paragraph());
        return document;
    }

    /// <summary>Explicit title, else the first non-empty text block.</summary>
    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title)) return Title.Trim();
            foreach (var block in Blocks)
            {
                if (!block.IsTextBlock) continue;
                var text = block.PlainText.Trim();
                if (text.Length > 0) return text.Length > 80 ? text[..80] + "…" : text;
            }
            return string.Empty;
        }
    }

    public string ToPlainText()
    {
        var builder = new StringBuilder();
        var number = 0;
        var firstBlock = true;
        foreach (var block in Blocks)
        {
            if (!firstBlock) builder.Append('\n');
            firstBlock = false;
            number = block.Kind == BlockKind.NumberedList ? number + 1 : 0;
            var indent = new string(' ', Math.Max(0, block.Level) * 2);
            switch (block.Kind)
            {
                case BlockKind.BulletedList: builder.Append(indent).Append("• "); break;
                case BlockKind.NumberedList: builder.Append(indent).Append(number).Append(". "); break;
                case BlockKind.CheckList: builder.Append(indent).Append(block.Checked ? "☑ " : "☐ "); break;
                case BlockKind.Quote: builder.Append("“"); break;
                case BlockKind.Divider: builder.Append("———"); continue;
                case BlockKind.Image:
                    builder.Append('[').Append(block.Image?.AltText ?? block.Image?.Caption ?? "Image").Append(']');
                    continue;
            }
            builder.Append(block.PlainText);
            if (block.Kind == BlockKind.Quote) builder.Append('”');
        }
        return builder.ToString();
    }

    /// <summary>Asset file names referenced by image blocks.</summary>
    public IEnumerable<string> ReferencedAssets() => Blocks
        .Where(block => block.Kind == BlockKind.Image && !string.IsNullOrEmpty(block.Image?.Asset))
        .Select(block => block.Image!.Asset)
        .Distinct(StringComparer.Ordinal);
}
