using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using DaisyOS.Shell.Apps.System.Settings;
using Avalonia.Threading;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.Shell.Services.Theming;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LanguageTypographyTests
{
    [AvaloniaTheory]
    [InlineData(FontWeight.Normal, FontStyle.Normal)]
    [InlineData(FontWeight.Bold, FontStyle.Normal)]
    [InlineData(FontWeight.Normal, FontStyle.Italic)]
    [InlineData(FontWeight.Bold, FontStyle.Italic)]
    public void DefaultFontUsesTheBundledFaceWithoutSyntheticStyles(FontWeight weight, FontStyle style)
    {
        var face = new Typeface(TypographyPolicy.ResolveFontFamily(null), style, weight).GlyphTypeface;
        Assert.Equal(TypographyPolicy.DefaultFamilyName, face.FamilyName);
        Assert.Equal(weight, face.Weight);
        Assert.Equal(style, face.Style);
        Assert.Equal(FontSimulations.None, face.FontSimulations);
        Assert.Equal(TypographyPolicy.DefaultFamilyName, new DaisyOS.Shell.Services.Appearance.AppearanceSettings().SystemFontFamily);
        foreach (var character in "Il1 O0 ÅÄÖ åäö Éé ß € £ — …")
        {
            Assert.True(face.CharacterToGlyphMap.TryGetGlyph(character, out var glyph));
            Assert.NotEqual((ushort)0, glyph);
        }
    }

    [AvaloniaTheory]
    [InlineData("Atkinson Hyperlegible", 100)]
    [InlineData("Atkinson Hyperlegible", 150)]
    [InlineData("Noto Sans", 150)]
    public void FilesSortButtonFitsAfterLanguageChanges(string family, int fontScale)
    {
        var resources = Application.Current!.Resources;
        var folder = Path.Combine(Path.GetTempPath(), "daisy-translated-toolbar-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        TypographyPolicy.Apply(resources, family, fontScale);
        var window = new DaisyOS.Shell.Apps.Files.FilesWindow(folder, selectItem: false) { Width = 740, Height = 700 };
        window.Show();
        try
        {
            foreach (var language in new[] { "en-US", "fi-FI", "en-US" })
            {
                Loc.Apply(resources, language);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var button = window.FindControl<Button>("SortButton")!;
                var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
                using var text = new TextLayout(label.Text!, new Typeface(label.FontFamily), label.FontSize);
                Assert.True(label.Bounds.Width + 0.5 >= text.WidthIncludingTrailingWhitespace,
                    $"{language}: {label.Bounds.Width} cannot fit {label.Text} ({text.WidthIncludingTrailingWhitespace}).");
                var toolbar = window.FindControl<Border>("SearchTools")!;
                var toolbarPosition = toolbar.TranslatePoint(default, window)!.Value;
                Assert.True(toolbarPosition.X + toolbar.Bounds.Width <= window.ClientSize.Width);
                var position = button.TranslatePoint(default, toolbar)!.Value;
                Assert.True(position.X >= 0 && position.X + button.Bounds.Width <= toolbar.Bounds.Width);
                foreach (var item in toolbar.GetVisualDescendants().OfType<Button>())
                {
                    var point = item.TranslatePoint(default, toolbar)!.Value;
                    Assert.True(point.X >= 0 && point.X + item.Bounds.Width <= toolbar.Bounds.Width);
                }
            }
        }
        finally
        {
            window.Close();
            Loc.Apply(resources, "en-US");
            TypographyPolicy.Apply(resources, "Atkinson Hyperlegible", 100);
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public void LanguageChangesUpdateBoundControlsWithoutChangingUserText()
    {
        var resources = Application.Current!.Resources;
        var label = Loc.Bind(new Button(), Button.ContentProperty, "Cancel");
        var userText = new TextBox { Text = "Cancel" };
        var row = new StackPanel { Children = { label, userText } };
        var window = new Window { Content = row };
        window.Show();
        try
        {
            Loc.Apply(resources, "fi-FI");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Peruuta", label.Content);
            Assert.Equal("Cancel", userText.Text);
            Assert.Equal("fi-FI", Loc.Culture.Name);
            Loc.Apply(resources, "en-US");
            Assert.Equal("Cancel", label.Content);
        }
        finally { Loc.Apply(resources, "en-US"); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(760, "Atkinson Hyperlegible", 150)]
    [InlineData(1280, "Atkinson Hyperlegible", 100)]
    [InlineData(760, "Noto Sans", 150)]
    [InlineData(1280, "DejaVu Serif", 125)]
    public void FinnishSettingsAndConfirmationRemainInsideTheirContent(int width, string family, int scale)
    {
        var resources = Application.Current!.Resources;
        Loc.Apply(resources, "fi-FI");
        TypographyPolicy.Apply(resources, family, scale);
        var settings = new SettingsWindow { Width = width, Height = 900 };
        settings.Show();
        Window? dialog = null;
        try
        {
            settings.SelectCategory("Appearance");
            Dispatcher.UIThread.RunJobs();
            settings.UpdateLayout();
            var view = settings.FindControl<SettingsView>("SettingsContent")!;
            var content = view.FindControl<StackPanel>("AppearanceContent")!;
            foreach (var control in content.GetVisualDescendants().OfType<Control>()
                .Where(control => control.IsEffectivelyVisible && control is Button or ComboBox or TextBlock))
            {
                var point = control.TranslatePoint(default, content)!.Value;
                Assert.True(point.X >= -1 && point.X + control.Bounds.Width <= content.Bounds.Width + 1,
                    $"{control.GetType().Name} {control.Name} overflows: {point.X}+{control.Bounds.Width}>{content.Bounds.Width}");
            }
            using var settingsFrame = settings.CaptureRenderedFrame();
            Directory.CreateDirectory("/tmp/daisy-language-rendering");
            settingsFrame!.Save($"/tmp/daisy-language-rendering/settings-fi-{width}-{scale}.png", PngBitmapEncoderOptions.Default);
            settings.SelectCategory("Input");
            Dispatcher.UIThread.RunJobs();
            settings.UpdateLayout();
            var languagePicker = view.GetLogicalDescendants().OfType<ComboBox>().Single(control => control.Name == "SystemLanguagePicker");
            Assert.Equal((double)resources["InputHeightStandard"]!, languagePicker.Bounds.Height);
            Assert.Equal((double)resources["TypeSizeBody"]!, languagePicker.FontSize);
            var type = typeof(DaisyOS.Shell.App).Assembly.GetType("DaisyOS.Shell.Views.Dialogs.ConfirmationDialog")!;
            dialog = (Window)Activator.CreateInstance(type, Loc.Get("Delete note"), Loc.Format("Delete “{0}”?", "Untitled"),
                Loc.Get("This permanently deletes the note and its file. This action cannot be undone."), Loc.Get("Delete note"), Loc.Get("Cancel"))!;
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            using var frame = dialog.CaptureRenderedFrame();
            var actions = dialog.GetVisualDescendants().OfType<Button>().Where(button => button.Content is string).ToArray();
            Assert.Equal(2, actions.Length);
            foreach (var action in actions)
            {
                var point = action.TranslatePoint(default, dialog)!.Value;
                Assert.InRange(point.X + action.Bounds.Width, 0, dialog.ClientSize.Width);
            }
            frame!.Save($"/tmp/daisy-language-rendering/confirmation-fi-{scale}.png", PngBitmapEncoderOptions.Default);
        }
        finally { dialog?.Close(); settings.Close(); Loc.Apply(resources, "en-US"); TypographyPolicy.Apply(resources, "Atkinson Hyperlegible", 100); }
    }

    [AvaloniaTheory]
    [InlineData("Atkinson Hyperlegible", 100)]
    [InlineData("Atkinson Hyperlegible", 150)]
    [InlineData("Noto Sans", 150)]
    [InlineData("DejaVu Serif", 125)]
    [InlineData("DejaVu Sans Mono", 90)]
    public void ControlsShareFontMeasuredHeightsAndRenderMultilingualText(string family, int scale)
    {
        var resources = Application.Current!.Resources;
        TypographyPolicy.Apply(resources, family, scale);
        var row = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        row.Children.Add(new Button { Content = "Järjestelmän kieli", Classes = { "Primary" } });
        row.Children.Add(new ComboBox { ItemsSource = new[] { "Suomi — Åäö" }, SelectedIndex = 0 });
        row.Children.Add(new TextBox { Text = "日本語 العربية हिन्दी" });
        row.Children.Add(new SearchBar { PlaceholderText = "Hae järjestelmäasetuksista" });
        var window = new Window { Content = row, Width = 460, SizeToContent = SizeToContent.Height };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var height = (double)resources["ControlHeightStandard"]!;
            if (family == "Atkinson Hyperlegible" && scale == 100) Assert.Equal(32, height);
            Assert.All(row.Children, control => Assert.Equal(control is TextBox or ComboBox or SearchBar ? height + 8 : height, control.Bounds.Height));
            Assert.Equal(TextHintingMode.Strong, TextOptions.GetTextHintingMode(window));
            Assert.Equal(BaselinePixelAlignment.Aligned, TextOptions.GetBaselinePixelAlignment(window));
            var font = (FontFamily)resources["ShellFontFamily"]!;
            var size = (double)resources["TypeSizeBody"]!;
            using var text = new TextLayout("ÅÉgjpq 日本語 العربية हिन्दी", new Typeface(font), size);
            Assert.True(text.Height <= height - 10);
            var glyphs = text.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>()
                .SelectMany(run => run.GlyphRun.GlyphInfos).ToArray();
            Assert.NotEmpty(glyphs);
            Assert.All(glyphs, glyph => Assert.NotEqual((ushort)0, glyph.GlyphIndex));
            Assert.Equal(Math.Round(size), size);
            Directory.CreateDirectory("/tmp/daisy-language-rendering");
            frame.Save($"/tmp/daisy-language-rendering/{family.Replace(' ', '-')}-{scale}.png");
        }
        finally { window.Close(); TypographyPolicy.Apply(resources, "Atkinson Hyperlegible", 100); }
    }
}
