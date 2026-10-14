using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace DaisyOS.Shell.Services.Theming;

/// <summary>Integer type sizes and font-measured line/control heights shared by every app.</summary>
public static class TypographyPolicy
{
    private static readonly (string Role, double Size, double Line)[] Roles =
    [
        ("Caption", 12, 16), ("BodySmall", 13, 18), ("Body", 14, 20), ("BodyLarge", 16, 24),
        ("Title", 20, 28), ("Heading", 24, 32), ("Display", 32, 40), ("DisplayLarge", 44, 52), ("DisplayHero", 104, 112)
    ];
    private const string MetricSample = "ÅÉgjpq 日本語 العربية हिन्दी";

    public static T BindHeight<T>(T control) where T : Control
    {
        control.Bind(Control.HeightProperty, control.GetResourceObservable(control is TextBox or ComboBox ? "InputHeightStandard" : "ControlHeightStandard"));
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
        family = string.IsNullOrWhiteSpace(family) ? "Inter" : family.Trim();
        var primary = family == "Inter" ? "avares://DaisyOS.Shell/Assets/fonts#Inter" : family;
        var font = new FontFamily(primary + ", Noto Sans, DejaVu Sans");
        var typeface = new Typeface(font);
        var scale = Math.Clamp(scalePercent, 90, 150) / 100d;
        resources["ShellFontFamily"] = font;
        resources["SystemFontFamilyName"] = family;
        foreach (var (role, baseSize, baseLine) in Roles)
        {
            var size = Math.Round(baseSize * scale, MidpointRounding.AwayFromZero);
            using var measured = new TextLayout(MetricSample, typeface, size);
            resources["TypeSize" + role] = size;
            resources["TypeLine" + role] = Math.Ceiling(Math.Max(baseLine * scale, measured.Height));
        }
        // Inputs use Body; buttons use BodySmall. Leave an equal vertical inset
        // around the taller line, and round once to the shared 4px geometry grid.
        var line = Math.Max((double)resources["TypeLineBody"]!, (double)resources["TypeLineBodySmall"]!);
        var height = Math.Max(32, Math.Ceiling((line + 10) / 4) * 4);
        resources["ButtonHeightSmall"] = height;
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
}
