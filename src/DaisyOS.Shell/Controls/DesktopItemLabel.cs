using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace DaisyOS.Shell.Controls;

/// <summary>Balances desktop names without changing their typography or splitting Unicode graphemes.</summary>
public sealed class DesktopItemLabel : TextBlock
{
    public static readonly StyledProperty<string?> FullTextProperty = AvaloniaProperty.Register<DesktopItemLabel, string?>(nameof(FullText));
    public string? FullText { get => GetValue(FullTextProperty); set => SetValue(FullTextProperty, value); }
    private double? _baseSpacing;
    private bool _settingSpacing;
    private (string Text, double Width, double Size, Typeface Face, double Spacing, FlowDirection Flow)? _layoutKey;
    static DesktopItemLabel() => AffectsMeasure<DesktopItemLabel>(FullTextProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FontFeaturesProperty) _layoutKey = null;
        if (change.Property == LetterSpacingProperty && !_settingSpacing) { _baseSpacing = LetterSpacing; _layoutKey = null; }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var face = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var spacing = _baseSpacing ?? LetterSpacing;
        _baseSpacing = spacing;
        var key = (FullText ?? "", availableSize.Width, FontSize, face, spacing, FlowDirection);
        if (_layoutKey != key)
        {
            _layoutKey = key;
            double Width(string value)
            {
                using var layout = new TextLayout(value, face, FontSize, Foreground, flowDirection: FlowDirection, letterSpacing: spacing, fontFeatures: FontFeatures);
                return layout.WidthIncludingTrailingWhitespace;
            }
            var fullWidth = Width(key.Item1);
            var graphemes = StringInfo.ParseCombiningCharacters(key.Item1).Length;
            var tracking = spacing;
            var nearFit = graphemes > 1 && double.IsFinite(availableSize.Width) && availableSize.Width > 0
                && fullWidth > availableSize.Width && fullWidth <= availableSize.Width * 1.08;
            if (nearFit) tracking -= Math.Min(FontSize * 0.05, (fullWidth - availableSize.Width + 0.25) / (graphemes - 1));
            _settingSpacing = true;
            try { SetCurrentValue(LetterSpacingProperty, tracking); }
            finally { _settingSpacing = false; }
            using var fitted = new TextLayout(key.Item1, face, FontSize, Foreground, flowDirection: FlowDirection, letterSpacing: tracking, fontFeatures: FontFeatures);
            Text = fitted.WidthIncludingTrailingWhitespace <= availableSize.Width ? key.Item1 : Balance(key.Item1, availableSize.Width, Width);
        }
        return base.MeasureOverride(availableSize);
    }

    public static string Balance(string text, double width, Func<string, double> measure)
    {
        if (string.IsNullOrEmpty(text) || !double.IsFinite(width) || width <= 0 || measure(text) <= width) return text;
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        if (boundaries.Length < 4) return text;
        // Prefer real word boundaries, then balanced grapheme boundaries for long single words.
        string? Pick(bool words, bool overflow)
        {
            string? best = null; var score = double.PositiveInfinity;
            for (var i = 2; i <= boundaries.Length - 2; i++)
            {
                var at = boundaries[i];
                if (words && !char.IsWhiteSpace(text[at - 1]) && !char.IsWhiteSpace(text[at])) continue;
                var first = text[..at].TrimEnd(); var second = text[at..].TrimStart();
                if (StringInfo.ParseCombiningCharacters(first).Length < 2 || StringInfo.ParseCombiningCharacters(second).Length < 2) continue;
                var left = measure(first); var right = measure(second);
                if (left > width || (!overflow && right > width)) continue;
                // Prefer two complete lines, otherwise fill the first and ellipsize the second.
                var candidateScore = right <= width ? Math.Abs(left - right) : 1_000_000 + right;
                if (candidateScore < score) { score = candidateScore; best = first + "\n" + second; }
            }
            return best;
        }
        return Pick(true, false) ?? Pick(false, false) ?? Pick(true, true) ?? Pick(false, true) ?? text;
    }
}
