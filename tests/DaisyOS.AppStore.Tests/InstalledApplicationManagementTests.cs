using DaisyOS.Core.Models;
using DaisyOS.Shell.Services;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class InstalledApplicationManagementTests
{
    [Fact]
    public void BuiltInLauncherItemCanBePinnedWithoutAnInstalledDesktopEntry()
    {
        var desktop = Path.Combine(Path.GetTempPath(), "daisy-desktop-pin-" + Guid.NewGuid());
        try
        {
            var service = new InstalledApplicationService(new PackageRunner(string.Empty));
            var app = new AppEntry("org.daisyos.taskmanager.desktop", "Task Manager", "Monitor tasks",
                "org.daisyos.taskmanager", false, AppLaunchKind.Internal);

            var shortcut = service.AddToDesktop(app, desktop);

            Assert.Equal(Path.Combine(desktop, "org.daisyos.taskmanager.desktop"), shortcut);
            var content = File.ReadAllText(shortcut!);
            Assert.Contains("Exec=daisyos-shell --open-app=taskmanager", content);
            Assert.Contains("Icon=org.daisyos.taskmanager", content);
        }
        finally
        {
            if (Directory.Exists(desktop)) Directory.Delete(desktop, recursive: true);
        }
    }

    [Theory]
    [InlineData("user", "--user")]
    [InlineData("system", "--system")]
    [InlineData("work", "--installation=work")]
    public async Task RemovalAndRepairKeepTheResolvedInstallationAndRemote(string scope, string scopeArgument)
    {
        var runner = new PackageRunner($"org.example.ScopeTest\t{scope}\tcompany-repo\n");
        var service = new InstalledApplicationService(runner);
        var app = new AppEntry("org.example.ScopeTest.desktop", "Scope Test", "", "", false, AppLaunchKind.DesktopEntry);
        var target = await service.ResolveAsync(app, TestContext.Current.CancellationToken);
        Assert.True(target.CanManage);
        Assert.Equal(scope, target.InstallationScope);
        Assert.True(await service.UninstallAsync(target, TestContext.Current.CancellationToken));
        Assert.Contains(runner.Calls, args => args.SequenceEqual(new[] { "uninstall", scopeArgument, "-y", "--noninteractive", "--", "app/org.example.ScopeTest/x86_64/stable" }));
        Assert.True(await service.RepairAsync(target, TestContext.Current.CancellationToken));
        Assert.Contains(runner.Calls, args => args.SequenceEqual(new[] { "install", scopeArgument, "-y", "--noninteractive", "--reinstall", "--", "company-repo", "org.example.ScopeTest" }));
    }

    private sealed class PackageRunner(string installations) : ICommandRunner
    {
        private bool _removed;
        public List<string[]> Calls { get; } = [];
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments.ToArray());
            if (fileName != "flatpak") return Task.FromResult(CommandResult.Failed("not installed"));
            if (arguments[0] == "uninstall") { _removed = true; return Task.FromResult(CommandResult.Success()); }
            return Task.FromResult(CommandResult.Success(arguments[0] == "list" && !_removed ? (arguments.Contains("--columns=application,installation,ref") ? installations.Replace("company-repo", "app/org.example.ScopeTest/x86_64/stable") : installations) : ""));
        }
    }
}
