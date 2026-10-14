using DaisyOS.System.Input;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxKeyboardLayoutServiceTests
{
    [Fact]
    public async Task StatusParsesCurrentLayoutAndUsesFriendlyNames()
    {
        var rulesPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(rulesPath, "! layout\n  fi Finnish\n  us English (US)\n! variant\n");
            var runner = new ScriptedCommandRunner(
                Succeeded("System Locale: LANG=fi_FI.UTF-8\n   X11 Layout: fi\n"),
                Succeeded("fi\nus\n"),
                Succeeded("[Argument: a(sss) {[Argument: (sss) \"fi\", \"\", \"Finnish\"]}]"));
            var service = new LinuxKeyboardLayoutService(runner, rulesPath);

            var status = await service.GetStatusAsync();

            Assert.True(status.IsAvailable);
            Assert.Equal("fi", status.CurrentLayoutId);
            Assert.Equal("fi", status.SystemLayoutId);
            Assert.Contains(status.AvailableLayouts, option => option is { Id: "fi", DisplayName: "Finnish" });
            Assert.Contains(status.AvailableLayouts, option => option is { Id: "us", DisplayName: "English (US)" });
        }
        finally
        {
            File.Delete(rulesPath);
        }
    }

    [Fact]
    public async Task SettingLayoutAppliesAndVerifiesTheActiveKwinLayout()
    {
        var runner = new ScriptedCommandRunner(
            Succeeded(),
            Succeeded(),
            Succeeded(),
            Succeeded(),
            Succeeded("[Argument: a(sss) {[Argument: (sss) \"fi\", \"\", \"Finnish\"]}]"));
        var service = new LinuxKeyboardLayoutService(runner);

        var result = await service.SetLayoutAsync("fi");

        Assert.True(result.Succeeded);
        Assert.True(result.AppliedToCurrentSession);
        Assert.Collection(
            runner.Calls,
            call => Assert.Equal(["set-x11-keymap", "fi"], call.Arguments),
            call => Assert.Equal(["--file", "kxkbrc", "--group", "Layout", "--key", "LayoutList", "fi", "--notify"], call.Arguments),
            call => Assert.Equal(["--file", "kxkbrc", "--group", "Layout", "--key", "Use", "true", "--type", "bool", "--notify"], call.Arguments),
            call => Assert.Equal(["org.kde.KWin", "/KWin", "org.kde.KWin.reconfigure"], call.Arguments),
            call => Assert.Equal(["--literal", "org.kde.keyboard", "/Layouts", "org.kde.KeyboardLayouts.getLayoutsList"], call.Arguments));
    }

    [Fact]
    public async Task SettingLayoutReturnsFailureWhenSystemDefaultCannotBeChanged()
    {
        var runner = new ScriptedCommandRunner(new CommandResult(1, "", "not authorized", TimedOut: false));
        var service = new LinuxKeyboardLayoutService(runner);

        var result = await service.SetLayoutAsync("fi");

        Assert.False(result.Succeeded);
        Assert.Single(runner.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fi; rm -rf /")]
    [InlineData("../../fi")]
    public async Task UnsafeLayoutIdsAreRejectedBeforeAnyCommandRuns(string layoutId)
    {
        var runner = new ScriptedCommandRunner();
        var service = new LinuxKeyboardLayoutService(runner);

        var result = await service.SetLayoutAsync(layoutId);

        Assert.False(result.Succeeded);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void KwinLayoutParserReadsTheActiveLayout()
    {
        Assert.Equal("fi", LinuxKeyboardLayoutService.ParseKwinLayout(
            "[Argument: a(sss) {[Argument: (sss) \"fi\", \"\", \"Finnish\"]}]"));
    }

    private static CommandResult Succeeded(string output = "") => new(0, output, string.Empty, TimedOut: false);
}
