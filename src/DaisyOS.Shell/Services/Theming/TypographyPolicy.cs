using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace DaisyOS.Shell.Services.Theming;

/// <summary>Integer type sizes and font-measured line/control heights shared by every app.</summary>
public static class TypographyPolicy
{
    // Request proportional alternates explicitly; some UI fonts default to tabular digits.
    public static FontFeatureCollection ProportionalNumerals { get; } = FontFeatureCollection.Parse("tnum=0,pnum=1");

    public const string DefaultFamilyName = "Inter";
    public const string BundledDefaultFamilyName = "avares://DaisyOS.Shell/Assets/fonts/Inter#Inter";

    public static FontFamily ResolveFontFamily(string? family)
    {
        family = string.IsNullOrWhiteSpace(family) ? DefaultFamilyName : family.Trim();
        var primary = family.Equals(DefaultFamilyName, StringComparison.OrdinalIgnoreCase)
            ? BundledDefaultFamilyName
            : family.Equals("Atkinson Hyperlegible", StringComparison.OrdinalIgnoreCase)
                ? "avares://DaisyOS.Shell/Assets/fonts/AtkinsonHyperlegible#Atkinson Hyperlegible" : family;
        return new FontFamily(primary + ", Noto Sans, DejaVu Sans");
    }

    private static readonly (string Role, double Size, double Line)[] Roles =
    [
        ("Caption", 12, 16), ("BodySmall", 13, 18), ("Body", 14, 20), ("BodyLarge", 16, 24),
        ("Title", 20, 28), ("Heading", 24, 32), ("Display", 32, 40), ("DisplayLarge", 44, 52), ("DisplayHero", 104, 112)
    ];
    private const string MetricSample = "ÅÉgjpq 日本語 العربية हिन्दी";

    public static T BindHeight<T>(T control) where T : Control
    {
        var resourceKey = control is TextBox or ComboBox
            ? "InputHeightStandard"
            : control.Classes.Contains("Small") || control.Classes.Contains("Compact")
                ? "ButtonHeightSmall"
                : control.Classes.Contains("Medium")
                    ? "ButtonHeightMedium"
                    : control.Classes.Contains("Large")
                        ? "ButtonHeightLarge"
                        : "ControlHeightStandard";
        control.Bind(Control.HeightProperty, control.GetResourceObservable(resourceKey));
        return control;
    }

    public static TextBlock Bind(TextBlock text, string role)
    {
        text.Bind(TextBlock.FontSizeProperty, text.GetResourceObservable("TypeSize" + role));
        text.Bind(TextBlock.LineHeightProperty, text.GetResourceObservable("TypeLine" + role));
        return text;
    }

    public static void Apply(IResourceDictionary resources, string family, int scalePercent)
    {
        family = string.IsNullOrWhiteSpace(family) ? DefaultFamilyName : family.Trim();
        var font = ResolveFontFamily(family);
        var typeface = new Typeface(font);
        var scale = Math.Clamp(scalePercent, 90, 150) / 100d;
        resources["ShellFontFamily"] = font;
        resources["SystemFontFamilyName"] = family;
        // Shape once to resolve the multilingual fallback faces. Scale their
        // individual design metrics in the same order as TextLine, rather than
        // scaling a combined height: floating-point reassociation can otherwise
        // change Math.Ceiling by a pixel at integer boundaries.
        using var measured = new TextLayout(MetricSample, typeface, 64);
        var metrics = new List<FontMetrics> { typeface.GlyphTypeface.Metrics };
        foreach (var formattedLine in measured.TextLines)
        foreach (var run in formattedLine.TextRuns)
        {
            if (run is not ShapedTextRun shaped) continue;
            var fallback = shaped.GlyphRun.GlyphTypeface.Metrics;
            if (!metrics.Contains(fallback)) metrics.Add(fallback);
        }
        foreach (var (role, baseSize, baseLine) in Roles)
        {
            var size = Math.Round(baseSize * scale, MidpointRounding.AwayFromZero);
            resources["TypeSize" + role] = size;
            resources["TypeLine" + role] = Math.Ceiling(Math.Max(baseLine * scale, ScaleLineHeight(metrics, size)));
        }
        // Inputs use Body; buttons use BodySmall. Leave an equal vertical inset
        // around the taller line, and round once to the shared 4px geometry grid.
        var line = Math.Max((double)resources["TypeLineBody"]!, (double)resources["TypeLineBodySmall"]!);
        var height = Math.Max(32, Math.Ceiling((line + 10) / 4) * 4);
        resources["ButtonHeightSmall"] = height;
        resources["RadiusButtonSmall"] = new CornerRadius(height / 2.0);
        resources["RadiusButtonSmallValue"] = height / 2.0;
        resources["ButtonHeightMedium"] = height + 4;
        resources["ButtonHeightLarge"] = height + 10;
        resources["QuickSettingsControlHeight"] = height + 12;
        foreach (var key in new[] { "ControlHeightCompact", "ControlHeightStandard", "ControlHeightComfortable",
            "HitTargetCompact", "HitTargetStandard", "HitTargetComfortable" })
            resources[key] = height;
        foreach (var key in new[] { "InputHeightStandard", "SearchHeightStandard", "SearchHeightProminent" })
            resources[key] = height + 8;
        resources["ShellChromeHeight"] = height + 20;
        resources["KWinDecorationTitleFontPixels"] = Math.Round(13 * scale, MidpointRounding.AwayFromZero);
        foreach (var (legacy, role) in new[] { ("Caption", "Caption"), ("Body", "BodySmall"),
            ("Subtitle", "BodyLarge"), ("Title", "Title"), ("Display", "Display") })
            resources["Typography" + legacy + "Size"] = resources["TypeSize" + role];
    }

    private static double ScaleLineHeight(IReadOnlyList<FontMetrics> metrics, double size)
    {
        var first = metrics[0];
        var scale = size / first.DesignEmHeight;
        var ascent = first.Ascent * scale;
        var descent = first.Descent * scale;
        var lineGap = first.LineGap * scale;
        for (var index = 1; index < metrics.Count; index++)
        {
            var font = metrics[index];
            scale = size / font.DesignEmHeight;
            ascent = Math.Min(ascent, font.Ascent * scale);
            descent = Math.Max(descent, font.Descent * scale);
            lineGap = Math.Max(lineGap, font.LineGap * scale);
        }
        return descent - ascent + lineGap;
    }
}
