using DaisyOS.System.Appearance;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxPersonalizationServiceTests
{
    [Theory]
    [InlineData(48, true)]
    [InlineData(24, false)]
    public async Task CursorResultChecksTheLiveCompositorSize(int liveSize, bool succeeds)
    {
        using var temp = new TemporaryDirectory();
        var runner = new CursorStateRunner(liveSize);
        var service = new LinuxPersonalizationService(runner, configHome: temp.Path, dataHome: temp.Path, dataDirectories: []);
        var result = await service.ApplyCursorAsync("breeze_cursors", 48);
        Assert.Equal(succeeds, result.Succeeded);
        Assert.Equal(succeeds ? 1 : 4, runner.LiveChecks);
        Assert.Equal(succeeds, runner.ActivationEnvironmentUpdated);
    }

    private sealed class CursorStateRunner(int size) : ICommandRunner
    {
        public int LiveChecks { get; private set; }
        public bool ActivationEnvironmentUpdated { get; private set; }
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (fileName == "qdbus6") LiveChecks++;
            if (fileName == "dbus-update-activation-environment")
                ActivationEnvironmentUpdated = arguments.SequenceEqual(new[] { "--systemd", "XCURSOR_THEME=breeze_cursors", "XCURSOR_SIZE=48" });
            return Task.FromResult(new CommandResult(0, fileName == "qdbus6" ? $"themeSize: {size}\n" : "", "", false));
        }
    }

    [Fact]
    public async Task CursorDiscoveryPrefersUserThemeAndIgnoresIncompleteThemes()
    {
        using var temp = new TemporaryDirectory();
        var user = Path.Combine(temp.Path, "user");
        var system = Path.Combine(temp.Path, "system");
        CreateCursorTheme(user, "breeze", "My Breeze");
        CreateCursorTheme(system, "breeze", "System Breeze");
        Directory.CreateDirectory(Path.Combine(system, "icons", "not-a-cursor"));

        var service = new LinuxPersonalizationService(new ScriptedCommandRunner(),
            dataHome: user, dataDirectories: [system]);

        var themes = await service.GetCursorThemesAsync();

        var theme = Assert.Single(themes);
        Assert.Equal("breeze", theme.Id);
        Assert.Equal("My Breeze", theme.DisplayName);
        Assert.StartsWith(user, theme.DirectoryPath, StringComparison.Ordinal);
    }

    [Fact]
    public void FontFamiliesAreParsedDeduplicatedAndSorted()
    {
        var families = LinuxPersonalizationService.ParseFontFamilies("Inter,Inter Display\nNoto Sans\ninter\n");

        Assert.Equal(["Inter", "Inter Display", "Noto Sans"], families);
    }

    [Fact]
    public async Task ApplyCursorUsesPlasmaAndSynchronizesGtkSettings()
    {
        using var temp = new TemporaryDirectory();
        var runner = new ScriptedCommandRunner(new CommandResult(0, "", "", false));
        var service = new LinuxPersonalizationService(runner, configHome: temp.Path, dataHome: temp.Path, dataDirectories: []);

        var result = await service.ApplyCursorAsync("Breeze_Snow", 48);

        Assert.True(result.Succeeded);
        Assert.Contains(runner.Calls, call => call.FileName == "plasma-apply-cursortheme" &&
            call.Arguments.SequenceEqual(["--size", "48", "Breeze_Snow"]));
        Assert.Contains(runner.Calls, call => call.FileName == "kwriteconfig6" &&
            call.Arguments.SequenceEqual(["--file", "kcminputrc", "--group", "Mouse", "--key", "cursorTheme", "Breeze_Snow", "--notify"]));
        Assert.Contains(runner.Calls, call => call.FileName == "kwriteconfig6" &&
            call.Arguments.SequenceEqual(["--file", "kcminputrc", "--group", "Mouse", "--key", "cursorSize", "48", "--notify"]));
        Assert.Contains(runner.Calls, call => call.FileName == "dbus-send" &&
            call.Arguments.SequenceEqual(["--session", "--type=signal", "/KGlobalSettings", "org.kde.KGlobalSettings.notifyChange", "int32:5", "int32:0"]));
        var gtk3 = await File.ReadAllTextAsync(Path.Combine(temp.Path, "gtk-3.0", "settings.ini"));
        Assert.Contains("gtk-cursor-theme-name=Breeze_Snow", gtk3);
        Assert.Contains("gtk-cursor-theme-size=48", gtk3);
    }

    [Fact]
    public async Task InstallFontValidatesAndAtomicallyInstallsForCurrentUser()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.Path, "My Font.ttf");
        await File.WriteAllBytesAsync(source, [0, 1, 2, 3]);
        var runner = new ScriptedCommandRunner(
            new CommandResult(0, "Example Sans\n", "", false),
            new CommandResult(0, "", "", false));
        var service = new LinuxPersonalizationService(runner, dataHome: Path.Combine(temp.Path, "data"), dataDirectories: []);

        var result = await service.InstallFontAsync(source);

        Assert.True(result.Succeeded);
        Assert.Equal(["Example Sans"], result.Families);
        Assert.NotNull(result.InstalledPath);
        Assert.True(File.Exists(result.InstalledPath));
        Assert.Contains(runner.Calls, call => call.FileName == "fc-cache");
    }

    private static void CreateCursorTheme(string dataRoot, string id, string name)
    {
        var directory = Path.Combine(dataRoot, "icons", id);
        Directory.CreateDirectory(Path.Combine(directory, "cursors"));
        File.WriteAllText(Path.Combine(directory, "index.theme"), $"[Icon Theme]\nName={name}\n");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "daisy-personalization-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
