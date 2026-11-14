using DaisyOS.Core.Search;
using DaisyOS.System.Processes;

namespace DaisyOS.Search.Tests;

public sealed class ApplicationCatalogTests
{
    [Fact]
    public void FallbackDiscoveryRespectsHiddenPrecedenceNestedIdsMetadataAndDesktopVisibility()
    {
        var root = Directory.CreateTempSubdirectory("daisy-xdg-search-").FullName;
        try
        {
            var user = Directory.CreateDirectory(Path.Combine(root, "user")).FullName;
            var system = Directory.CreateDirectory(Path.Combine(root, "system")).FullName;
            static void Entry(string directory, string filename, string extra = "") => File.WriteAllText(Path.Combine(directory, filename),
                "[Desktop Entry]\nType=Application\nName=Fixture\nExec=/usr/bin/true\nGenericName=Design Tool\nKeywords=image;paint;\n" + extra);
            Entry(system, "masked.desktop");
            Entry(user, "masked.desktop", "Hidden=true\n");
            Entry(system, "invisible.desktop", "NoDisplay=true\n");
            Entry(system, "wrong-desktop.desktop", "OnlyShowIn=DaisyNonexistentDesktop;\n");
            Entry(system, "terminal.desktop", "Terminal=true\n");
            Directory.CreateDirectory(Path.Combine(user, "nested"));
            Entry(Path.Combine(user, "nested"), "visible.desktop");
            var apps = new LinuxAppLauncherService().DiscoverDesktopEntries([user, system]);
            Assert.DoesNotContain(apps, app => app.Id == "masked.desktop" || app.Id == "invisible.desktop" || app.Id == "wrong-desktop.desktop");
            Assert.Contains(apps, app => app.Id == "terminal.desktop");
            var nested = Assert.Single(apps, app => app.Id == "nested-visible.desktop");
            Assert.Contains("Design Tool", nested.SearchTerms!);
            Assert.Contains("paint", nested.SearchTerms!);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task DesktopActionsAreOnlyExecutedForCatalogRegisteredIds()
    {
        string? executed = null;
        var provider = new ApplicationSearchProvider((_, _) => Task.CompletedTask,
            (_, action, _) => { executed = action; return Task.CompletedTask; });
        provider.ReplaceCatalog([new("browser.desktop", "Browser", "", "", false)
        { DesktopActions = [new("private", "New private window")] }]);
        var result = Assert.Single(await provider.SearchAsync(SearchQuery.Parse("browser"), default));
        Assert.Equal(2, result.Actions.Count);
        await provider.ExecuteAsync(result.Actions[1], default);
        Assert.Equal("private", executed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ExecuteAsync(new("desktop-action:arbitrary", provider.Id, result.Id, "Invalid"), default));
    }
}
