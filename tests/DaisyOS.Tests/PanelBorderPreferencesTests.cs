using DaisyOS.Shell.Services.Appearance;
using Xunit;
namespace DaisyOS.Tests;
public sealed class PanelBorderPreferencesTests
{
    [Fact]
    public async Task PanelBordersDefaultToEnabledAndTheOptOutPersists()
    {
        var folder = Directory.CreateTempSubdirectory("daisy-border-settings-");
        try
        {
            var store = new AppearanceSettingsStore(Path.Combine(folder.FullName, "appearance.json"));
            var settings = store.Load(); Assert.False(settings.DisablePanelBorders);
            settings.DisablePanelBorders = true; await store.SaveAsync(settings);
            Assert.True(store.Load().DisablePanelBorders);
            settings.DisablePanelBorders = false; await store.SaveAsync(settings);
            Assert.False(store.Load().DisablePanelBorders);
        }
        finally { folder.Delete(true); }
    }
}
