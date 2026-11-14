using DaisyOS.Shell.Apps.AppInstaller;
using DaisyOS.System.AppImage;
using Xunit;

namespace DaisyOS.Tests;

public sealed class AppInstallerViewModelTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"daisy-installer-test-"+Guid.NewGuid().ToString("N"));
    private string PackagePath=>Path.Combine(_root,"TestApp.AppImage");
    public AppInstallerViewModelTests() { Directory.CreateDirectory(_root); File.WriteAllText(PackagePath,"fixture inspected by fake AppImage service"); }
    public void Dispose() { if(Directory.Exists(_root)) Directory.Delete(_root,true); }

    private sealed class FakeAppImageInstallerService : IAppImageInstallerService
    {
        public List<AppImageInstallation> InstalledApps { get; } = [];
        public AppImageInspection? NextInspection { get; set; }
        public bool FailInspect { get; set; }
        public bool FailInstall { get; set; }

        public Task<AppImageInspection> InspectAsync(string path, CancellationToken cancellationToken = default)
        {
            if (FailInspect) throw new AppImageException("Invalid AppImage");
            return Task.FromResult(NextInspection ?? new AppImageInspection
            {
                SourcePath = path,
                Name = "Test App",
                Comment = "Test Comment",
                Version = "1.0.0",
                DesktopId = "test-app",
                Executable = path,
                Categories = "Utility",
                SizeBytes = 10_000_000,
                HasEmbeddedMetadata = true
            });
        }

        public Task<AppImageInstallation> InstallAsync(string path, string? destinationDirectory = null, CancellationToken cancellationToken = default)
        {
            if (FailInstall) throw new AppImageException("Install failed");
            var targetDir = string.IsNullOrWhiteSpace(destinationDirectory)
                ? "/home/user/.local/share/daisyos/appimages/test-app"
                : destinationDirectory;
            var installation = new AppImageInstallation
            {
                DesktopId = "test-app",
                Name = "Test App",
                Comment = "Test Comment",
                Version = "1.0.0",
                Categories = "Utility",
                AppImagePath = Path.Combine(targetDir, "test-app.AppImage"),
                DesktopFilePath = "/home/user/.local/share/applications/test-app.desktop",
                IconPath = "",
                InstalledAt = DateTimeOffset.UtcNow
            };
            InstalledApps.RemoveAll(a => a.DesktopId == installation.DesktopId);
            InstalledApps.Add(installation);
            return Task.FromResult(installation);
        }

        public Task<IReadOnlyList<AppImageInstallation>> GetInstalledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppImageInstallation>>(InstalledApps.ToList());

        public Task<bool> UninstallAsync(string desktopId, CancellationToken cancellationToken = default)
        {
            var removed = InstalledApps.RemoveAll(a => a.DesktopId == desktopId) > 0;
            return Task.FromResult(removed);
        }
    }

    [Fact]
    public async Task InitializeAsyncPopulatesInstalledList()
    {
        var service = new FakeAppImageInstallerService();
        service.InstalledApps.Add(new AppImageInstallation
        {
            DesktopId = "existing-app",
            Name = "Existing App",
            Comment = "",
            Version = "2.0",
            Categories = "",
            AppImagePath = "/path/to/existing.AppImage",
            DesktopFilePath = "/path/to/existing.desktop",
            IconPath = "",
            InstalledAt = DateTimeOffset.UtcNow
        });

        using var vm = new AppInstallerViewModel(service, dataHome: _root);
        await vm.InitializeAsync();

        Assert.Single(vm.Installed);
        Assert.Equal("existing-app", vm.Installed[0].DesktopId);
        Assert.False(vm.ShowInstalledEmpty);
    }

    [Fact]
    public async Task SelectFileAsyncInspectsFileAndConfiguresFirstTimeButtons()
    {
        var service = new FakeAppImageInstallerService();
        using var vm = new AppInstallerViewModel(service, dataHome: _root);

        await vm.SelectFileAsync(PackagePath);

        Assert.True(vm.HasSelection);
        Assert.Equal("Test App", vm.SelectedName);
        Assert.Equal("Test Comment", vm.SelectedComment);
        Assert.False(vm.IsSelectedInstalled);
        Assert.True(vm.ShowFirstTimeInstallButton);
        Assert.False(vm.ShowReinstallButton);
        Assert.False(vm.ShowOpenButton);
    }

    [Fact]
    public async Task SelectFileAsyncForAlreadyInstalledShowsOpenAndReinstall()
    {
        var service = new FakeAppImageInstallerService();
        service.InstalledApps.Add(new AppImageInstallation
        {
            DesktopId = "test-app",
            Name = "Test App",
            Comment = "",
            Version = "1.0.0",
            Categories = "",
            AppImagePath = "/path/to/test-app.AppImage",
            DesktopFilePath = "/path/to/test-app.desktop",
            IconPath = "",
            InstalledAt = DateTimeOffset.UtcNow
        });

        using var vm = new AppInstallerViewModel(service, dataHome: _root);
        await vm.SelectFileAsync(PackagePath);

        Assert.True(vm.HasSelection);
        Assert.True(vm.IsSelectedInstalled);
        Assert.True(vm.ShowOpenButton);
        Assert.True(vm.ShowReinstallButton);
        Assert.False(vm.ShowFirstTimeInstallButton);
    }

    [Fact]
    public async Task InstallAsyncInstallsAndExposesOpenButton()
    {
        var service = new FakeAppImageInstallerService();
        using var vm = new AppInstallerViewModel(service, dataHome: _root);

        await vm.SelectFileAsync(PackagePath);
        Assert.True(vm.ShowFirstTimeInstallButton);
        Assert.False(vm.ShowOpenButton);

        await vm.InstallAsync();

        Assert.True(vm.JustInstalled);
        Assert.True(vm.IsSelectedInstalled);
        Assert.True(vm.ShowOpenButton);
        Assert.False(vm.ShowFirstTimeInstallButton);
        Assert.False(vm.ShowReinstallButton);
        Assert.Single(vm.Installed);
    }

    [Fact]
    public async Task RemoveAsyncUninstallsAndUpdatesState()
    {
        var service = new FakeAppImageInstallerService();
        using var vm = new AppInstallerViewModel(service, dataHome: _root);

        await vm.SelectFileAsync(PackagePath);
        await vm.InstallAsync();
        Assert.Single(vm.Installed);

        await vm.RemoveAsync(vm.Installed[0]);

        Assert.Empty(vm.Installed);
        Assert.False(vm.IsSelectedInstalled);
        Assert.False(vm.ShowOpenButton);
        Assert.True(vm.ShowFirstTimeInstallButton);
    }

    [Fact]
    public async Task SelectFileAsyncAwaitsInitializationWithoutDroppingFile()
    {
        var service = new FakeAppImageInstallerService();
        using var vm = new AppInstallerViewModel(service, dataHome: _root);

        // Simulate concurrent startup where InitializeAsync starts on window Opened
        // and SelectFileAsync is called immediately afterwards by activation
        var initTask = vm.InitializeAsync();
        var selectTask = vm.SelectFileAsync(PackagePath);

        await Task.WhenAll(initTask, selectTask);

        Assert.True(vm.HasSelection);
        Assert.Equal("Test App", vm.SelectedName);
    }

    [Fact]
    public async Task InstallDestinationPathUpdatesWithCustomDirectory()
    {
        var service = new FakeAppImageInstallerService();
        using var vm = new AppInstallerViewModel(service, dataHome: _root);

        await vm.SelectFileAsync(PackagePath);
        Assert.Contains("test-app", vm.InstallDestinationPath);

        vm.CustomDestinationDirectory = "/custom/install/dir";
        Assert.Equal("/custom/install/dir", vm.InstallDestinationPath);

        await vm.InstallAsync();
        Assert.Equal("/custom/install/dir/test-app.AppImage", service.InstalledApps[0].AppImagePath);
    }
}

