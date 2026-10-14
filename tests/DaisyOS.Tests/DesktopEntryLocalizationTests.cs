using System.Globalization;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.ViewModels;
using Xunit;

namespace DaisyOS.Tests;

public sealed class DesktopEntryLocalizationTests
{
    [Fact]
    public async Task DefaultApplicationPickersUseDesktopLocaleWithoutChangingAssociationIds()
    {
        var original = CultureInfo.CurrentUICulture;
        var directory = Directory.CreateTempSubdirectory("daisy-associations-").FullName;
        var path = Path.Combine(directory, "org.daisyos.files.desktop");
        try
        {
            await File.WriteAllTextAsync(path, "[Desktop Entry]\nType=Application\nName=Files\nName[fi]=Tiedostot\nMimeType=inode/directory;\n");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fi-FI");
            var service = new DaisyOS.System.Processes.LinuxDefaultAppsService(
                new ScriptedCommandRunner(new DaisyOS.System.Processes.CommandResult(0, "org.daisyos.files.desktop\n", "", false)), [directory]);
            var status = await service.GetDefaultAppsAsync();
            var files = Assert.Single(status.AvailableFileManagers);
            Assert.Equal("Tiedostot", files.DisplayName);
            Assert.Equal("org.daisyos.files.desktop", files.DesktopId);
            Assert.Equal(files.DesktopId, status.CurrentFileManagerDesktopId);
        }
        finally { CultureInfo.CurrentUICulture = original; File.Delete(path); Directory.Delete(directory); }
    }

    [Fact]
    public void DesktopNamesPreferTerritoryThenLanguageThenNeutral()
    {
        var entry = new Dictionary<string, string>
        {
            ["Name"] = "Files", ["Name[fi]"] = "Tiedostot", ["Name[fi_FI]"] = "Omat tiedostot",
            ["Comment"] = "Browse files", ["Comment[fi]"] = "Selaa tiedostoja"
        };
        Assert.Equal("Omat tiedostot", DesktopEntryLocalization.GetValue(entry, "Name", CultureInfo.GetCultureInfo("fi-FI")));
        entry["Name[fi_FI]"] = "";
        Assert.Equal("Tiedostot", DesktopEntryLocalization.GetValue(entry, "Name", CultureInfo.GetCultureInfo("fi-FI")));
        Assert.Equal("Files", DesktopEntryLocalization.GetValue(entry, "Name", CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("Browse files", DesktopEntryLocalization.GetValue(entry, "Comment", CultureInfo.GetCultureInfo("de-DE")));
    }

    [Fact]
    public void DesktopShortcutAndActionCachesRespectLanguageWithoutChangingCommands()
    {
        var original = CultureInfo.CurrentUICulture;
        var path = Path.Combine(Path.GetTempPath(), $"daisy-localized-{Guid.NewGuid():N}.desktop");
        File.WriteAllText(path, "[Desktop Entry]\nType=Application\nName=Example\nName[fi]=Esimerkki\nExec=example %U\nActions=New;\n[Desktop Action New]\nName=New window\nName[fi]=Uusi ikkuna\nExec=example --new-window\n");
        try
        {
            foreach (var language in new[] { "en-US", "fi-FI", "en-US" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                var finnish = language == "fi-FI";
                Assert.Equal(finnish ? "Esimerkki" : "Example", DesktopItemLoader.ParseDesktopFile(path).name);
                var action = Assert.Single(DesktopActionReader.GetActions(path));
                Assert.Equal(finnish ? "Uusi ikkuna" : "New window", action.Name);
                Assert.Equal("New", action.Id);
                Assert.Equal("example --new-window", action.Exec);
            }
        }
        finally { CultureInfo.CurrentUICulture = original; File.Delete(path); }
    }
}
