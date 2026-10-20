using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using DaisyOS.Shell.Services.Theming;
using DaisyOS.Shell.Views.Components.SystemBar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class ProportionalNumeralsTests
{
    [AvaloniaTheory]
    [InlineData(FontWeight.Normal)]
    [InlineData(FontWeight.Bold)]
    public void DigitsUseProportionalGlyphsAndAdvances(FontWeight weight)
    {
        var face = new Typeface(TypographyPolicy.ResolveFontFamily(null), weight: weight);
        using var narrow = new TextLayout("1111", face, 32, fontFeatures: TypographyPolicy.ProportionalNumerals);
        using var wide = new TextLayout("8888", face, 32, fontFeatures: TypographyPolicy.ProportionalNumerals);
        Assert.True(narrow.Width < wide.Width, $"Expected proportional widths, got {narrow.Width} and {wide.Width}.");
        using var badge = new TextLayout("20", face, 32, fontFeatures: TypographyPolicy.ProportionalNumerals);
        using var tabular = new TextLayout("20", face, 32, fontFeatures: FontFeatureCollection.Parse("pnum=0,tnum=1"));
        static ushort[] Glyphs(TextLayout layout) => layout.TextLines.SelectMany(line => line.TextRuns)
            .OfType<ShapedTextRun>().SelectMany(run => run.GlyphRun.GlyphInfos).Select(glyph => glyph.GlyphIndex).ToArray();
        Assert.False(Glyphs(badge).SequenceEqual(Glyphs(tabular)), "The badge must use the proportional digit alternatives.");
    }

    [AvaloniaFact]
    public void NotificationBadgeAndEditableTextInheritProportionalNumerals()
    {
        var bar = new SystemBarView();
        var input = new TextBox { Text = "120" };
        var button = new Button { Content = "20" };
        var window = new Window { Width = 500, Height = 140,
            Content = new StackPanel { Children = { bar, input, button } } };
        window.Show();
        try
        {
            typeof(SystemBarView).GetMethod("UpdateNotificationBadge", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bar, [20]);
            window.UpdateLayout();
            var label = bar.FindControl<TextBlock>("NotificationBadgeCount")!;
            Assert.Equal("20", label.Text);
            foreach (var control in new Control[] { label, input, button })
            {
                var features = TextElement.GetFontFeatures(control)!;
                Assert.Contains(features, feature => feature.Tag == "tnum" && feature.Value == 0);
                Assert.Contains(features, feature => feature.Tag == "pnum" && feature.Value == 1);
            }
            window.CaptureRenderedFrame()!.Save("/tmp/daisy-proportional-badge.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }
}
