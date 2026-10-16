using System.Text;
using System.Xml;
using System.Text.Json;
using DaisyOS.Core.Models;
using DaisyOS.System.AppStore;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxAppStoreServiceTests : IDisposable
{
    private readonly string _catalog = Path.Combine(Path.GetTempPath(), "daisy-store-" + Guid.NewGuid() + ".xml");
    private const string Xml = """
    <components>
      <component type="desktop-application">
        <id>org.mozilla.firefox</id><bundle type="flatpak">app/org.mozilla.firefox/x86_64/stable</bundle>
        <name>Firefox</name><name xml:lang="fi">Firefox suomeksi</name><summary>Private web browser</summary>
        <description><p>Browse the web.</p><ul><li>Private</li><li>Fast</li></ul></description>
        <developer><name>Mozilla</name></developer><project_license>MPL-2.0</project_license>
        <categories><category>Network</category><category>WebBrowser</category></categories>
        <keywords><keyword>internet</keyword></keywords><releases><release version="157.0"><description><p>New release.</p></description></release></releases>
        <icon type="remote" width="128">https://dl.flathub.org/media/firefox/icon.png</icon>
        <custom><value key="flathub::verification::verified">true</value></custom>
        <screenshots><screenshot><image width="752">https://dl.flathub.org/media/firefox/screen.png</image><image width="1000">https://untrusted.test/screen.png</image></screenshot></screenshots>
      </component>
      <component type="desktop-application"><id>com.valvesoftware.Steam</id><name>Steam</name><summary>Games</summary><categories><category>Game</category></categories></component>
      <component type="runtime"><id>org.freedesktop.Platform</id><name>Runtime</name></component>
      <component type="desktop-application"><id>--bad-id</id><name>Invalid</name></component>
    </components>
    """;
    public LinuxAppStoreServiceTests() => File.WriteAllText(_catalog, Xml);
    public void Dispose() => File.Delete(_catalog);
    private LinuxAppStoreService Service(ScriptedCommandRunner runner) => new(runner, _catalog);

    [Fact]
    public void AppStreamContainsRealLocalizedMetadataAndExcludesRuntimes()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Xml));
        var apps = FlathubCatalog.Read(stream, "fi-FI");
        Assert.Equal(2, apps.Count);
        var app = apps.Single(a => a.Id == "org.mozilla.firefox");
        Assert.Equal("Firefox suomeksi", app.Name);
        Assert.Equal("157.0", app.Version);
        Assert.True(app.IsVerified);
        Assert.Equal("Mozilla", app.Publisher);
        Assert.Equal("MPL-2.0", app.License);
        Assert.Contains("• Private", app.Description);
        Assert.Equal("New release.", app.ReleaseNotes);
        Assert.Equal("https://dl.flathub.org/media/firefox/screen.png", Assert.Single(app.Screenshots));
        Assert.Equal(0, app.Rating);
        Assert.Equal(AppCategory.Internet, app.Category);
    }
    [Fact]
    public void PackageInformationUsesRealSizesAndNamesRequestedPermissions()
    {
        using var json = JsonDocument.Parse("""
        {"download_size":127000000,"installed_size":339000000,"metadata":{"runtimeInstalledSize":668000000,"runtimeIsEol":true,
        "permissions":{"shared":["network"],"sockets":["pulseaudio"],"devices":["dri"],"filesystems":["xdg-download:ro"]}}}
        """);
        var info = LinuxAppStoreService.ParsePackageInfo(json.RootElement);
        Assert.Equal(127000000, info.DownloadBytes);
        Assert.Equal(339000000, info.InstalledBytes);
        Assert.Equal(668000000, info.RuntimeInstalledBytes);
        Assert.True(info.RuntimeEndOfLife);
        Assert.Contains("Internet access", info.Permissions);
        Assert.Contains("Graphics acceleration", info.Permissions);
        Assert.Contains("Access to your Downloads folder", info.Permissions);
    }
    [Theory]
    [InlineData("https://dl.flathub.org/media/org.app/icon.png", true)]
    [InlineData("https://dl.flathub.org/repo/screenshots/org.app/screen.png", true)]
    [InlineData("https://example.com/media/icon.png", false)]
    [InlineData("http://dl.flathub.org/media/icon.png", false)]
    public void FlathubMediaSupportsCurrentAndLegacyCatalogs(string url, bool expected)
        => Assert.Equal(expected, FlathubCatalog.IsMediaUrl(url));
    [Fact]
    public void ExternalEntitiesAreRejected()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE components [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><components>&x;</components>"));
        Assert.Throws<XmlException>(() => FlathubCatalog.Read(stream, "en"));
    }
    [Fact]
    public async Task SearchUsesFullCatalogAndKeywordsWithoutPerAppCommands()
    {
        var runner = new ScriptedCommandRunner(CommandResult.Success());
        var service = Service(runner);
        var matches = await service.SearchAppsAsync("internet");
        Assert.Equal("Firefox", Assert.Single(matches).Name);
        Assert.Single(runner.Calls);
        Assert.Equal("flatpak", runner.Calls[0].FileName);
        Assert.Contains("--columns=application,name,version,origin,installation", runner.Calls[0].Arguments);
        Assert.Empty(await service.SearchAppsAsync("nonexistentword"));
    }
    [Fact]
    public async Task CategoriesUseAppStreamAndFeaturedPicksAreActualCatalogApps()
    {
        var service = Service(new ScriptedCommandRunner(CommandResult.Success()));
        Assert.Equal("Steam", Assert.Single(await service.GetAppsByCategoryAsync(AppCategory.Gaming)).Name);
        Assert.Equal(2, (await service.GetFeaturedAppsAsync()).Count);
    }
    [Fact]
    public async Task MissingFlatpakStillAllowsBrowsingButLibraryReportsAnActionableError()
    {
        var service = Service(new ScriptedCommandRunner(CommandResult.Failed("missing", 127)));
        Assert.Equal(2, (await service.GetAppsByCategoryAsync(AppCategory.Discover)).Count);
        var error = await Assert.ThrowsAsync<AppStoreException>(() => service.GetInstalledAppsAsync());
        Assert.Contains("Flatpak", error.Message);
    }
    [Fact]
    public async Task InstallConfiguresUserFlathubAndNeverUsesHostPackageManagers()
    {
        var runner = new ScriptedCommandRunner(CommandResult.Success());
        var progress = new List<double>();
        Assert.True(await Service(runner).InstallAppAsync("org.mozilla.firefox", progress.Add));
        Assert.All(runner.Calls, c => Assert.Equal("flatpak", c.FileName));
        Assert.Contains(runner.Calls, c => c.Arguments.SequenceEqual(new[] { "remote-add", "--user", "--if-not-exists", "flathub", "https://dl.flathub.org/repo/flathub.flatpakrepo" }));
        Assert.Equal(new[] { "install", "--user", "--noninteractive", "-y", "flathub", "org.mozilla.firefox" }, runner.Calls.Last().Arguments);
        Assert.Equal(new double[] { 0, 1 }, progress);
        Assert.Equal(TimeSpan.FromMinutes(30), runner.Calls.Last().Timeout);
    }
    [Fact]
    public async Task RemoveUsesActualSystemScopeAndKeepsApplicationData()
    {
        var runner = new ScriptedCommandRunner(CommandResult.Success("org.mozilla.firefox\tFirefox\t157\tflathub\tsystem\n"), CommandResult.Success());
        Assert.True(await Service(runner).UninstallAppAsync("org.mozilla.firefox"));
        Assert.Equal(new[] { "uninstall", "--system", "--noninteractive", "-y", "org.mozilla.firefox" }, runner.Calls.Last().Arguments);
        Assert.DoesNotContain("--delete-data", runner.Calls.Last().Arguments);
    }
    [Fact]
    public async Task InstalledAndUpdatesUseActualIdsVersionsAndScope()
    {
        var list = CommandResult.Success("org.mozilla.firefox\tFirefox\t156\tflathub\tuser\n");
        var runner = new ScriptedCommandRunner(list, CommandResult.Success("org.mozilla.firefox\n"));
        var app = Assert.Single(await Service(runner).GetUpdatesAsync());
        Assert.Equal(AppInstallState.UpdateAvailable, app.InstallState);
        Assert.Equal("156", app.Version);
        Assert.Equal("user", app.InstallationScope);
        Assert.Equal(new[] { "remote-ls", "--user", "--updates", "--app", "--columns=application" }, runner.Calls.Last().Arguments);
    }
    [Theory]
    [InlineData("--delete-data")][InlineData("org.foo.Bar;echo bad")][InlineData("org.foo.Bar\n")][InlineData("simple-package")]
    public async Task InvalidIdsCannotReachTransactionCommands(string id)
    {
        var runner = new ScriptedCommandRunner(CommandResult.Success());
        await Assert.ThrowsAsync<ArgumentException>(() => Service(runner).InstallAppAsync(id));
        Assert.Empty(runner.Calls);
    }
    [Fact]
    public async Task NativeFirefoxIsInstalledInDiscoveryAndLibraryWithoutFlatpakUpdateCommands()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        try
        {
            var desktop = Path.Combine(directory, "firefox.desktop");
            File.WriteAllText(desktop, "[Desktop Entry]\nType=Application\nName=Firefox\nExec=firefox %u\n");
            var runner = new ScriptedCommandRunner(CommandResult.Success(), CommandResult.Success(), CommandResult.Success());
            var service = new LinuxAppStoreService(runner, _catalog, [directory]);
            var app = (await service.GetAppsByCategoryAsync(AppCategory.Discover)).Single(a => a.Id == "org.mozilla.firefox");
            Assert.Equal(AppInstallState.Installed, app.InstallState); Assert.Equal(desktop, app.NativeDesktopFile);
            Assert.Equal("native", app.InstallationScope);
            Assert.Single(await service.GetInstalledAppsAsync());
            Assert.Empty(await service.GetUpdatesAsync());
            Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("--installation=native"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task BrokenCatalogDoesNotPreventManagingInstalledApplications()
    {
        File.WriteAllText(_catalog, "<broken");
        var runner = new ScriptedCommandRunner(CommandResult.Success("org.example.Editor\tEditor\t2.0\tprivate-repo\tuser\n"));
        var app = Assert.Single(await Service(runner).GetInstalledAppsAsync());
        Assert.Equal("Editor", app.Name);
        Assert.Equal(AppInstallState.Installed, app.InstallState);
        Assert.Equal("private-repo", app.Origin);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task NativeApplicationRemovalUsesActualPackageOwnerAndKeepsDependencyChecks()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var desktop = Path.Combine(directory, "firefox.desktop");
            File.WriteAllText(desktop, "[Desktop Entry]\nType=Application\nName=Firefox\nExec=firefox %u\n");
            var runner = new ScriptedCommandRunner(CommandResult.Success(), CommandResult.Success("firefox\n"), CommandResult.Success());
            var service = new LinuxAppStoreService(runner, _catalog, [directory]);
            Assert.True(await service.UninstallAppAsync("org.mozilla.firefox"));
            Assert.Collection(runner.Calls,
                c => Assert.Equal("list", c.Arguments[0]),
                c => { Assert.Equal("pacman", c.FileName); Assert.Equal(new[] { "-Qqo", "--", desktop }, c.Arguments); },
                c => { Assert.Equal("pkexec", c.FileName); Assert.Equal(new[] { "pacman", "-R", "--noconfirm", "--", "firefox" }, c.Arguments); });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("--recursive")]
    [InlineData("firefox\nkwin")]
    [InlineData("firefox;reboot")]
    public async Task MalformedPackageOwnersNeverReachRemoval(string owner)
    {
        var runner = new ScriptedCommandRunner(CommandResult.Success(owner));
        Assert.Null(await NativeApplicationPackages.FindOwnerAsync(runner, "/usr/share/applications/firefox.desktop"));
        Assert.Single(runner.Calls);
        await Assert.ThrowsAsync<ArgumentException>(() => NativeApplicationPackages.RemoveAsync(runner, owner));
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task FailedTransactionDoesNotExposeCommandOutputOrReportSuccess()
    {
        var runner = new ScriptedCommandRunner(CommandResult.Success(), CommandResult.Success(), CommandResult.Failed("private path/password"));
        var error = await Assert.ThrowsAsync<AppStoreException>(() => Service(runner).InstallAppAsync("org.mozilla.firefox"));
        Assert.DoesNotContain("private", error.Message);
    }
}
