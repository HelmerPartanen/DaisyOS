using System.Globalization;
using System.Resources;
using System.Text;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Shell.Services.Localization;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LanguageCatalogTests
{
    [Fact]
    public void FinnishSatellitePreservesFormatArgumentsAndCultureAwareFormatting()
    {
        var manager = new ResourceManager("DaisyOS.Shell.Resources.Messages", typeof(Loc).Assembly);
        Assert.Equal("Peruuta", manager.GetString(Loc.Key("Cancel"), CultureInfo.GetCultureInfo("fi-FI")));
        var set = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        foreach (global::System.Collections.DictionaryEntry entry in set)
        {
            var source = (string)entry.Value!;
            var translated = manager.GetString((string)entry.Key, CultureInfo.GetCultureInfo("fi-FI"))!;
            Assert.Equal(CompositeFormat.Parse(source).MinimumArgumentCount, CompositeFormat.Parse(translated).MinimumArgumentCount);
        }
        var format = manager.GetString(Loc.Key("{0:F1} MB"), CultureInfo.GetCultureInfo("fi-FI"))!;
        Assert.Equal("1,5 Mt", string.Format(CultureInfo.GetCultureInfo("fi-FI"), format, 1.5));
        Assert.Equal("01:02:03", string.Format(CultureInfo.InvariantCulture, "{0:hh\\:mm\\:ss}", new TimeSpan(1, 2, 3)));
    }

    [Fact]
    public async Task LanguagePersistsAlongsideFontWithoutLosingOldPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daisy-language-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "appearance.json");
            await File.WriteAllTextAsync(path, "{\"systemFontFamily\":\"DejaVu Serif\",\"textScalePercent\":125}");
            var store = new AppearanceSettingsStore(path);
            var settings = store.Load();
            Assert.Equal("en-US", settings.LanguageTag);
            settings.LanguageTag = "fi-FI";
            await store.SaveAsync(settings);
            var loaded = store.Load();
            Assert.Equal("fi-FI", loaded.LanguageTag);
            Assert.Equal("DejaVu Serif", loaded.SystemFontFamily);
            Assert.Equal(125, loaded.TextScalePercent);
        }
        finally { Directory.Delete(directory, true); }
    }
}
