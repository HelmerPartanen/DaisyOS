using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Policies;
using Xunit;

namespace DaisyOS.Tests;

public sealed class JsonAppPolicyServiceTests
{
    [Fact]
    public void DefaultConstructorUsesSystemPolicyPath()
    {
        var service = new JsonAppPolicyService();

        Assert.Equal(Path.GetFullPath(JsonAppPolicyService.DefaultPolicyPath), service.PolicyPath);
    }

    [Fact]
    public async Task MissingPolicyIsUnconfiguredAndUnrestricted()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "missing", "allowed-apps.json");
        var service = new JsonAppPolicyService(path);

        var status = await service.GetStatusAsync();

        Assert.Equal(AppPolicyConfigurationState.Unconfigured, status.ConfigurationState);
        Assert.Equal(Path.GetFullPath(path), status.PolicyPath);
        Assert.Empty(status.Apps);
        Assert.False(status.IsConfigured);
        Assert.False(status.HasWarning);
        Assert.False(status.IsEnforced);
        Assert.Contains("unrestricted", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidPolicyLoadsEveryRoadmapFieldWithoutChangingTheFile()
    {
        using var directory = new TemporaryDirectory();
        const string json = """
            {
              "apps": [
                {
                  "id": " org.example.Allowed ",
                  "name": " Example App ",
                  "command": " /usr/bin/example ",
                  "icon": " example ",
                  "allowed": true,
                  "futureField": "ignored"
                },
                {
                  "id": "org.example.Denied",
                  "name": "Denied App",
                  "command": "/usr/bin/denied",
                  "icon": "denied",
                  "allowed": false
                }
              ]
            }
            """;
        var path = directory.Write("allowed-apps.json", json);
        var service = new JsonAppPolicyService(path);

        var status = await service.GetStatusAsync();

        Assert.Equal(AppPolicyConfigurationState.Loaded, status.ConfigurationState);
        Assert.True(status.IsConfigured);
        Assert.False(status.HasWarning);
        Assert.False(status.IsEnforced);
        Assert.Collection(
            status.Apps,
            app => Assert.Equal(
                new AppPolicyEntry(
                    "org.example.Allowed",
                    "Example App",
                    "/usr/bin/example",
                    "example",
                    true),
                app),
            app => Assert.Equal(
                new AppPolicyEntry(
                    "org.example.Denied",
                    "Denied App",
                    "/usr/bin/denied",
                    "denied",
                    false),
                app));
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public async Task EmptyAppsArrayIsAValidConfiguredPolicy()
    {
        using var directory = new TemporaryDirectory();
        var service = new JsonAppPolicyService(directory.Write("allowed-apps.json", "{\"apps\":[]}"));

        var status = await service.GetStatusAsync();

        Assert.Equal(AppPolicyConfigurationState.Loaded, status.ConfigurationState);
        Assert.True(status.IsConfigured);
        Assert.Empty(status.Apps);
    }

    [Fact]
    public async Task DuplicateIdsAreDeduplicatedCaseInsensitivelyWithFirstEntryWinning()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.Write(
            "allowed-apps.json",
            """
            {
              "apps": [
                {
                  "id": "org.example.App",
                  "name": "First",
                  "command": "/usr/bin/first",
                  "icon": "first",
                  "allowed": true
                },
                {
                  "id": "ORG.EXAMPLE.APP",
                  "name": "Second",
                  "command": "/usr/bin/second",
                  "icon": "second",
                  "allowed": false
                }
              ]
            }
            """);
        var service = new JsonAppPolicyService(path);

        var status = await service.GetStatusAsync();

        var entry = Assert.Single(status.Apps);
        Assert.Equal("First", entry.Name);
        Assert.True(entry.Allowed);
        Assert.Contains("duplicate", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"apps\":null}")]
    [InlineData("{\"apps\":[null]}")]
    [InlineData("{\"apps\":[{\"name\":\"App\",\"command\":\"app\",\"icon\":\"app\",\"allowed\":true}]}")]
    [InlineData("{\"apps\":[{\"id\":\"app\",\"command\":\"app\",\"icon\":\"app\",\"allowed\":true}]}")]
    [InlineData("{\"apps\":[{\"id\":\"app\",\"name\":\"App\",\"icon\":\"app\",\"allowed\":true}]}")]
    [InlineData("{\"apps\":[{\"id\":\"app\",\"name\":\"App\",\"command\":\"app\",\"allowed\":true}]}")]
    [InlineData("{\"apps\":[{\"id\":\"app\",\"name\":\"App\",\"command\":\"app\",\"icon\":\"app\"}]}")]
    [InlineData("{\"apps\":[{\"id\":\"app\",\"name\":\"App\",\"command\":\"app\",\"icon\":\"app\",\"allowed\":\"yes\"}]}")]
    public async Task InvalidJsonOrSchemaIsReportedAsMalformed(string json)
    {
        using var directory = new TemporaryDirectory();
        var log = new RecordingLogService();
        var service = new JsonAppPolicyService(directory.Write("allowed-apps.json", json), log);

        var status = await service.GetStatusAsync();

        Assert.Equal(AppPolicyConfigurationState.Malformed, status.ConfigurationState);
        Assert.Empty(status.Apps);
        Assert.False(status.IsConfigured);
        Assert.True(status.HasWarning);
        Assert.False(status.IsEnforced);
        Assert.Contains("malformed", status.Message, StringComparison.OrdinalIgnoreCase);
        var warning = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.IsType<global::System.Text.Json.JsonException>(warning.Exception);
    }

    [Fact]
    public async Task UnreadablePolicyIsReportedAsUnavailable()
    {
        using var directory = new TemporaryDirectory();
        var log = new RecordingLogService();
        var service = new JsonAppPolicyService(directory.Path, log);

        var status = await service.GetStatusAsync();

        Assert.Equal(AppPolicyConfigurationState.Unavailable, status.ConfigurationState);
        Assert.Empty(status.Apps);
        Assert.True(status.HasWarning);
        Assert.Contains("could not be read", status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LogLevel.Warning, Assert.Single(log.Entries).Level);
    }

    [Fact]
    public async Task CancellationIsNotConvertedIntoAPolicyFailure()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new JsonAppPolicyService(Path.Combine(directory.Path, "missing.json"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetStatusAsync(cancellation.Token));
    }

    [Fact]
    public void InternalAppsAreTrustedEvenWhenConfigurationIsMalformed()
    {
        var service = new JsonAppPolicyService();
        var app = CreateApp("settings", AppLaunchKind.Internal);
        var status = CreateStatus(AppPolicyConfigurationState.Malformed);

        var decision = service.Evaluate(app, status);

        Assert.Equal(AppPolicyDecisionKind.TrustedInternal, decision.Kind);
        Assert.Null(decision.PolicyEntry);
        Assert.False(decision.IsLaunchBlocked);
        Assert.False(decision.IsEnforced);
    }

    [Fact]
    public void ListedExternalAppsReportAllowedAndDeniedDecisionsCaseInsensitively()
    {
        var service = new JsonAppPolicyService();
        var allowed = new AppPolicyEntry("org.example.Allowed", "Allowed", "allowed", "allowed", true);
        var denied = new AppPolicyEntry("org.example.Denied", "Denied", "denied", "denied", false);
        var status = CreateStatus(AppPolicyConfigurationState.Loaded, allowed, denied);

        var allowedDecision = service.Evaluate(
            CreateApp("ORG.EXAMPLE.ALLOWED", AppLaunchKind.DesktopEntry),
            status);
        var deniedDecision = service.Evaluate(
            CreateApp("org.example.denied", AppLaunchKind.DirectCommand),
            status);

        Assert.Equal(AppPolicyDecisionKind.Allowed, allowedDecision.Kind);
        Assert.Same(allowed, allowedDecision.PolicyEntry);
        Assert.Equal(AppPolicyDecisionKind.ExplicitlyDenied, deniedDecision.Kind);
        Assert.Same(denied, deniedDecision.PolicyEntry);
        Assert.False(allowedDecision.IsLaunchBlocked);
        Assert.False(deniedDecision.IsLaunchBlocked);
    }

    [Fact]
    public void ExternalAppMissingFromLoadedPolicyIsUnlisted()
    {
        var service = new JsonAppPolicyService();
        var status = CreateStatus(
            AppPolicyConfigurationState.Loaded,
            new AppPolicyEntry("different-app", "Different", "different", "different", true));

        var decision = service.Evaluate(CreateApp("target-app", AppLaunchKind.Uri), status);

        Assert.Equal(AppPolicyDecisionKind.Unlisted, decision.Kind);
        Assert.Null(decision.PolicyEntry);
        Assert.False(decision.IsLaunchBlocked);
    }

    [Theory]
    [InlineData(AppPolicyConfigurationState.Unconfigured, AppPolicyDecisionKind.Unconfigured)]
    [InlineData(AppPolicyConfigurationState.Malformed, AppPolicyDecisionKind.Malformed)]
    [InlineData(AppPolicyConfigurationState.Unavailable, AppPolicyDecisionKind.Unavailable)]
    public void NonLoadedPolicyStatesRemainDistinctInExternalAppDecisions(
        AppPolicyConfigurationState state,
        AppPolicyDecisionKind expectedKind)
    {
        var service = new JsonAppPolicyService();

        var decision = service.Evaluate(
            CreateApp("external", AppLaunchKind.DesktopEntry),
            CreateStatus(state));

        Assert.Equal(expectedKind, decision.Kind);
        Assert.Null(decision.PolicyEntry);
        Assert.False(decision.IsLaunchBlocked);
        Assert.False(decision.IsEnforced);
        Assert.Contains("unrestricted", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AppPolicyStatus CreateStatus(
        AppPolicyConfigurationState state,
        params AppPolicyEntry[] apps) =>
        new(state, "/test/allowed-apps.json", apps, state.ToString());

    private static AppEntry CreateApp(string id, AppLaunchKind launchKind) =>
        new(id, id, "Test application", "test", false, launchKind, "/usr/bin/test");

    private sealed class RecordingLogService : ILogService
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            Entries.Add((level, message, exception));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = global::System.IO.Path.Combine(
                global::System.IO.Path.GetTempPath(),
                "daisyos-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Write(string name, string contents)
        {
            var path = global::System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
