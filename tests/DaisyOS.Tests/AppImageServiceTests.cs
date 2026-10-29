using DaisyOS.System.AppImage;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class AppImageServiceTests
{
    [Theory]
    [InlineData("My App", "my-app")]
    [InlineData("MyApp_v1.0", "myapp_v1.0")]
    [InlineData("org.example.cool-app", "org.example.cool-app")]
    [InlineData("test/path:app", "test-path-app")]
    [InlineData("   ---special---   ", "special")]
    [InlineData("", "appimage")]
    [InlineData("   ", "appimage")]
    [InlineData(null, "appimage")]
    public void SanitizeDesktopIdProducesValidCleanIdentifiers(string? input, string expected)
    {
        var result = AppImageService.SanitizeDesktopId(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeDesktopIdLimitsLength()
    {
        var longName = new string('a', 120);
        var result = AppImageService.SanitizeDesktopId(longName);
        Assert.True(result.Length <= 80);
    }

    [Theory]
    [InlineData("/path/to/My_Application-x86_64.AppImage", "My Application x86 64")]
    [InlineData("CoolApp.AppImage", "CoolApp")]
    [InlineData("simple_tool.appimage", "simple tool")]
    [InlineData("---.AppImage", "AppImage")]
    public void DisplayNameFromFileNameExtractsReadableName(string path, string expected)
    {
        var result = AppImageService.DisplayNameFromFileName(path);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DesktopEntryBuildAndParseRoundtrip()
    {
        var installation = new AppImageInstallation
        {
            DesktopId = "sample-app",
            Name = "Sample App",
            Comment = "A great application",
            Version = "2.1.0",
            Categories = "Utility;Development;",
            AppImagePath = "/home/user/.local/share/daisyos/appimages/sample-app/sample-app.AppImage",
            DesktopFilePath = "/home/user/.local/share/applications/sample-app.desktop",
            IconPath = "/home/user/.local/share/icons/hicolor/1024x1024/apps/sample-app.png",
            InstalledAt = DateTimeOffset.UtcNow
        };

        var desktopText = AppImageDesktopEntry.Build(installation);
        Assert.Contains("[Desktop Entry]", desktopText);
        Assert.Contains("Name=Sample App", desktopText);
        Assert.Contains("Exec=\"/home/user/.local/share/daisyos/appimages/sample-app/sample-app.AppImage\"", desktopText);
        Assert.Contains("Icon=/home/user/.local/share/icons/hicolor/1024x1024/apps/sample-app.png", desktopText);
        Assert.Contains("X-DaisyOS-AppImage=sample-app", desktopText);
        Assert.Contains("X-AppImage-Version=2.1.0", desktopText);

        Assert.True(AppImageDesktopEntry.IsOwnedBy(desktopText, "sample-app"));
        Assert.False(AppImageDesktopEntry.IsOwnedBy(desktopText, "other-app"));

        var parsed = AppImageDesktopEntry.Parse(desktopText);
        Assert.Equal("Sample App", parsed["Name"]);
        Assert.Equal("A great application", parsed["Comment"]);
        Assert.Equal("sample-app", parsed["X-DaisyOS-AppImage"]);
        Assert.Equal("2.1.0", parsed["X-AppImage-Version"]);
    }

    [Fact]
    public void QuoteExecEscapesReservedCharacters()
    {
        var path = "/opt/apps/test \"app\" $1 `cmd` %u \\path.AppImage";
        var quoted = AppImageDesktopEntry.QuoteExec(path);
        Assert.StartsWith("\"", quoted);
        Assert.EndsWith("\"", quoted);
        Assert.Contains("\\\"", quoted);
        Assert.Contains("\\$", quoted);
        Assert.Contains("\\`", quoted);
        Assert.Contains("%%", quoted);
        Assert.Contains("\\\\", quoted);
    }

    [Fact]
    public async Task InspectAsyncThrowsWhenFileNotFound()
    {
        var runner = new ScriptedCommandRunner();
        var service = new AppImageService(runner);

        await Assert.ThrowsAsync<AppImageException>(() =>
            service.InspectAsync("/path/to/nonexistent/app.AppImage"));
    }

    [Fact]
    public async Task InspectAsyncThrowsWhenNotAppImageExtension()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var runner = new ScriptedCommandRunner();
            var service = new AppImageService(runner);

            await Assert.ThrowsAsync<AppImageException>(() =>
                service.InspectAsync(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task InspectAsyncThrowsWhenFileTooSmallOrNotElf()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "test-" + Guid.NewGuid().ToString("N") + ".AppImage");
        try
        {
            await File.WriteAllBytesAsync(tempFile, "not an elf binary"u8.ToArray());
            var runner = new ScriptedCommandRunner();
            var service = new AppImageService(runner);

            await Assert.ThrowsAsync<AppImageException>(() =>
                service.InspectAsync(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetInstalledAsyncReturnsEmptyWhenNoRegistry()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "daisyos-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var runner = new ScriptedCommandRunner();
            var service = new AppImageService(runner, dataHome: tempDir);

            var installed = await service.GetInstalledAsync();
            Assert.Empty(installed);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task UninstallAsyncReturnsFalseForUnknownId()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "daisyos-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var runner = new ScriptedCommandRunner();
            var service = new AppImageService(runner, dataHome: tempDir);

            var result = await service.UninstallAsync("nonexistent-app");
            Assert.False(result);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AppImageIconCacheReturnsNullForMissingOrNonAppImage()
    {
        Assert.Null(DaisyOS.Shell.Services.AppImageIconCache.GetOrExtractIconPath(""));
        Assert.Null(DaisyOS.Shell.Services.AppImageIconCache.GetOrExtractIconPath("/nonexistent/file.txt"));
        Assert.Null(DaisyOS.Shell.Services.AppImageIconCache.GetOrExtractIconPath("/nonexistent/file.AppImage"));
    }

    [Fact]
    public async Task InspectAsyncExtractsRealIconWhenAvailable()
    {
        var samplePath = "/home/helmeri/Downloads/EpicGamesLauncher-x86_64.AppImage";
        if (!File.Exists(samplePath)) return;

        var service = new AppImageService();
        var inspection = await service.InspectAsync(samplePath);

        Assert.Equal("Epic Games Launcher", inspection.Name);
        Assert.NotNull(inspection.IconData);
        Assert.NotEmpty(inspection.IconData);
        Assert.Equal(".png", inspection.IconExtension);

        var cachedIconPath = DaisyOS.Shell.Services.AppImageIconCache.GetOrExtractIconPath(samplePath);
        Assert.NotNull(cachedIconPath);
        Assert.True(File.Exists(cachedIconPath));
    }

    [Fact]
    public async Task InspectAsyncExtractsLmStudioIconWhenAvailable()
    {
        var samplePath = "/home/helmeri/Downloads/LM-Studio-0.4.24-1-x64.AppImage";
        if (!File.Exists(samplePath)) return;

        var service = new AppImageService();
        var inspection = await service.InspectAsync(samplePath);

        Assert.Equal("LM-Studio", inspection.Name);
        Assert.NotNull(inspection.IconData);
        Assert.NotEmpty(inspection.IconData);
        Assert.Equal(".png", inspection.IconExtension);

        var cachedIconPath = DaisyOS.Shell.Services.AppImageIconCache.GetOrExtractIconPath(samplePath);
        Assert.NotNull(cachedIconPath);
        Assert.True(File.Exists(cachedIconPath));
    }
}

