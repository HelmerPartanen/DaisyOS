using DaisyOS.Core.Models;
using DaisyOS.System.Appearance;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxSystemThemeServiceTests : IDisposable
{
    private readonly string _configHome = Path.Combine(Path.GetTempPath(), "DaisyOS.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DarkThemeUpdatesGtkKdeAndFallbackSettings()
    {
        var runner = new ScriptedCommandRunner(Succeeded(), Succeeded(), Succeeded(), Succeeded());
        var service = new LinuxSystemThemeService(runner, _configHome);

        var result = await service.ApplyAsync(ThemeMode.Dark);

        Assert.True(result.Succeeded);
        Assert.Collection(
            runner.Calls,
            call =>
            {
                Assert.Equal("gsettings", call.FileName);
                Assert.Equal(["set", "org.gnome.desktop.interface", "color-scheme", "prefer-dark"], call.Arguments);
            },
            call => Assert.Equal(["set", "org.gnome.desktop.interface", "gtk-theme", "Breeze-Dark"], call.Arguments),
            call => Assert.Equal(["set", "org.gnome.desktop.wm.preferences", "button-layout", ":minimize,maximize,close"], call.Arguments),
            call =>
            {
                Assert.Equal("kwriteconfig6", call.FileName);
                Assert.Equal(["--file", "kdeglobals", "--group", "General", "--key", "ColorScheme", "BreezeDark", "--notify"], call.Arguments);
            });
        AssertGtkSettings("gtk-3.0", "Breeze-Dark", "1");
        AssertGtkSettings("gtk-4.0", "Breeze-Dark", "1");
    }

    [Fact]
    public async Task LightThemeUpdatesGtkKdeAndFallbackSettings()
    {
        var runner = new ScriptedCommandRunner(Succeeded(), Succeeded(), Succeeded(), Succeeded());
        var service = new LinuxSystemThemeService(runner, _configHome);

        var result = await service.ApplyAsync(ThemeMode.Light);

        Assert.True(result.Succeeded);
        Assert.Collection(
            runner.Calls,
            call =>
            {
                Assert.Equal("gsettings", call.FileName);
                Assert.Equal(["set", "org.gnome.desktop.interface", "color-scheme", "prefer-light"], call.Arguments);
            },
            call => Assert.Equal(["set", "org.gnome.desktop.interface", "gtk-theme", "Breeze"], call.Arguments),
            call => Assert.Equal(["set", "org.gnome.desktop.wm.preferences", "button-layout", ":minimize,maximize,close"], call.Arguments),
            call =>
            {
                Assert.Equal("kwriteconfig6", call.FileName);
                Assert.Equal(["--file", "kdeglobals", "--group", "General", "--key", "ColorScheme", "BreezeLight", "--notify"], call.Arguments);
            });
        AssertGtkSettings("gtk-3.0", "Breeze", "0");
        AssertGtkSettings("gtk-4.0", "Breeze", "0");
    }

    [Fact]
    public async Task LightThemePreservesOtherGtkSettingsWhileReplacingThemeKeys()
    {
        var directory = Path.Combine(_configHome, "gtk-3.0");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "settings.ini"),
            "[Settings]\ngtk-font-name=Inter 11\ngtk-theme-name=Old\ngtk-decoration-layout=:close\n\n[Unrelated]\nvalue=keep\n");
        var service = new LinuxSystemThemeService(
            new ScriptedCommandRunner(Succeeded(), Succeeded(), Succeeded(), Succeeded()),
            _configHome);

        await service.ApplyAsync(ThemeMode.Light);

        var content = await File.ReadAllTextAsync(Path.Combine(directory, "settings.ini"));
        Assert.Contains("gtk-font-name=Inter 11", content);
        Assert.Contains("gtk-theme-name=Breeze", content);
        Assert.Contains("gtk-application-prefer-dark-theme=0", content);
        Assert.Contains("gtk-decoration-layout=:minimize,maximize,close", content);
        Assert.DoesNotContain("gtk-decoration-layout=:close", content);
        Assert.Contains("[Unrelated]\nvalue=keep", content);
    }

    [Fact]
    public async Task AutomaticThemeIsRejectedUntilTheShellResolvesIt()
    {
        var runner = new ScriptedCommandRunner();
        var service = new LinuxSystemThemeService(runner, _configHome);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyAsync(ThemeMode.Automatic));

        Assert.Empty(runner.Calls);
    }

    public void Dispose()
    {
        if (Directory.Exists(_configHome))
            Directory.Delete(_configHome, recursive: true);
    }

    private void AssertGtkSettings(string toolkitDirectory, string expectedTheme, string expectedDarkPreference)
    {
        var content = File.ReadAllText(Path.Combine(_configHome, toolkitDirectory, "settings.ini"));
        Assert.Contains($"gtk-theme-name={expectedTheme}", content);
        Assert.Contains($"gtk-application-prefer-dark-theme={expectedDarkPreference}", content);
        Assert.Contains("gtk-decoration-layout=:minimize,maximize,close", content);
    }

    private static CommandResult Succeeded() => new(0, string.Empty, string.Empty, TimedOut: false);
}
