using DaisyOS.System.Appearance;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxLanguageServiceTests
{
    [Theory]
    [InlineData("fi_FI.utf8\nen_US.utf8\n", "fi_FI.UTF-8")]
    [InlineData("C\nC.utf8\n", "C.UTF-8")]
    public async Task SavesUserLocaleWithSafeFallbackAndExplicitActivationEnvironment(string available, string expected)
    {
        var folder = Path.Combine(Path.GetTempPath(), "daisy-language-" + Guid.NewGuid().ToString("N"));
        var before = new[] { "LANG", "LANGUAGE", "LC_MESSAGES", "LC_ALL" }.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            var runner = new ScriptedCommandRunner(new CommandResult(0, available, "", false),
                new CommandResult(127, "", "missing optional helper", false),
                new CommandResult(127, "", "missing optional helper", false),
                new CommandResult(127, "", "missing optional helper", false));
            var result = await new LinuxLanguageService(runner, folder).ApplyAsync("fi-FI");
            Assert.True(result.Succeeded);
            var file = await File.ReadAllTextAsync(Path.Combine(folder, "environment.d", "90-daisyos-language.conf"));
            Assert.Contains("LANG=" + expected + "\n", file);
            Assert.Contains("LANGUAGE=fi:en\n", file);
            Assert.Equal(expected, Environment.GetEnvironmentVariable("LC_MESSAGES"));
            Assert.Null(Environment.GetEnvironmentVariable("LC_ALL"));
            Assert.Contains(runner.Calls, call => call.FileName == "dbus-update-activation-environment" &&
                call.Arguments.Contains("LANG=" + expected) && call.Arguments.Contains("LC_ALL="));
            Assert.DoesNotContain(runner.Calls, call => call.FileName == "localectl");
        }
        finally
        {
            foreach (var pair in before) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void FirstLaunchRetainsValidInheritedLocaleWithoutAStoredPreference()
    {
        var folder = Path.Combine(Path.GetTempPath(), "daisy-language-" + Guid.NewGuid().ToString("N"));
        var before = new[] { "XDG_CONFIG_HOME", "LANG", "LANGUAGE", "LC_MESSAGES", "LC_ALL" }.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", folder);
            Environment.SetEnvironmentVariable("LANG", "en_US.utf8");
            LinuxLanguageService.ApplyProcessEnvironment(DaisyOS.Core.Models.SystemLanguages.Resolve("en-US"));
            Assert.Equal("en_US.UTF-8", Environment.GetEnvironmentVariable("LANG"));
            Environment.SetEnvironmentVariable("LANG", "invalid");
            LinuxLanguageService.ApplyProcessEnvironment(DaisyOS.Core.Models.SystemLanguages.Resolve("en-US"));
            Assert.Equal("C.UTF-8", Environment.GetEnvironmentVariable("LANG"));
        }
        finally { foreach (var pair in before) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
    }

    [Fact]
    public async Task RejectsUnsupportedLanguageWithoutWritingOrRunningCommands()
    {
        var runner = new ScriptedCommandRunner();
        var folder = Path.Combine(Path.GetTempPath(), "daisy-language-" + Guid.NewGuid().ToString("N"));
        var result = await new LinuxLanguageService(runner, folder).ApplyAsync("../../invalid");
        Assert.False(result.Succeeded);
        Assert.Empty(runner.Calls);
        Assert.False(Directory.Exists(folder));
    }
}
