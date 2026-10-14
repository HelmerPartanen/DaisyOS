using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace DaisyOS.Tests;

public sealed class SettingsHonestyContractTests
{
    [Fact]
    public void NavigationDoesNotAdvertiseUnimplementedCategories()
    {
        var xaml = File.ReadAllText(SettingsViewPath("SettingsView.axaml"));

        Assert.DoesNotContain("Tag=\"Privacy\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Tag=\"Accounts\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Tag=\"Gaming\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsDoesNotExposePreferenceOnlyPlaceholders()
    {
        var code = File.ReadAllText(SettingsViewPath("SettingsView.axaml.cs"));
        string[] removedControls =
        [
            "Ask before apps use location",
            "Allow guest sessions",
            "Spatial audio when available",
            "Empty Trash after",
            "Include preview updates",
            "Turn on Do Not Disturb while gaming",
            "When a display is connected"
        ];

        foreach (var control in removedControls)
            Assert.DoesNotContain(control, code, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendStateWinsOverOldPrivatePreferences()
    {
        var code = File.ReadAllText(SettingsViewPath("SettingsView.axaml.cs"));

        Assert.Contains("var saved = fallback;", code, StringComparison.Ordinal);
        Assert.Contains("_ => fallback", code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RiskyDisplayChangesHaveAutomaticRevertProtection()
    {
        using var closed = new CancellationTokenSource();
        var restored = false;
        var guard = new DaisyOS.Shell.Apps.System.Settings.DisplayChangeGuard();
        var result = await guard.RunAsync(() => Task.FromResult(true),
            () => { restored = true; return Task.FromResult(true); },
            confirmation =>
            {
                Assert.Equal(15, confirmation.SecondsRemaining);
                closed.Cancel();
            }, closed.Token);
        Assert.True(restored);
        Assert.Equal(DaisyOS.Shell.Apps.System.Settings.DisplayChangeResult.Reverted, result);
    }

    [Fact]
    public void WifiJourneyUsesTheRealNetworkManagerService()
    {
        var code = File.ReadAllText(SettingsViewPath("SettingsView.axaml.cs"));

        Assert.Contains("GetNetworksAsync", code, StringComparison.Ordinal);
        Assert.Contains("ConnectToNetworkAsync", code, StringComparison.Ordinal);
        Assert.Contains("DisconnectFromNetworkAsync", code, StringComparison.Ordinal);
        Assert.Contains("Choose a network to connect to.", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Wi-Fi changes are applied through NetworkManager.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwarePagesUseLiveStateWithoutRebuildingOnATimer()
    {
        var code = File.ReadAllText(SettingsViewPath("SettingsView.axaml.cs"));

        Assert.DoesNotContain("_liveRefreshTimer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("app?.SetPreference(key", code, StringComparison.Ordinal);
    }

    [Fact]
    public void InstalledApplicationsStayOnTheAppsPageAndExposeRealRowActions()
    {
        var code = File.ReadAllText(SettingsViewPath("SettingsView.axaml.cs"));
        var systemBuilderStart = code.IndexOf("private async Task BuildSystemAsync", StringComparison.Ordinal);
        var notificationsBuilderStart = code.IndexOf("private void BuildNotifications", systemBuilderStart, StringComparison.Ordinal);
        var systemBuilder = code[systemBuilderStart..notificationsBuilderStart];

        Assert.DoesNotContain("BuildInstalledApplicationsSection", systemBuilder, StringComparison.Ordinal);
        Assert.Contains("case \"InstalledApps\": BuildInstalledApplicationsSection", code, StringComparison.Ordinal);
        Assert.Contains("ResolveApplicationIconPath([app.Icon, app.Id, app.Name])", code, StringComparison.Ordinal);
        Assert.Contains("Icon = UIIcons.MoreVertical", code, StringComparison.Ordinal);
        Assert.Contains("Header = \"Modify\"", code, StringComparison.Ordinal);
        Assert.Contains("Header = \"Repair\"", code, StringComparison.Ordinal);
        Assert.Contains("Header = \"Uninstall\"", code, StringComparison.Ordinal);
        Assert.Contains("appsCrumb.Click += (_, _) => SelectCategory(\"Apps\")", code, StringComparison.Ordinal);
    }

    private static string SettingsViewPath(string fileName) => Path.Combine(
        RepoRoot(), "src", "DaisyOS.Shell", "Apps", "System", "Settings", fileName);

    private static string RepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
}
