using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;

namespace DaisyOS.System.Policies;

public sealed class JsonAppPolicyService : IAppPolicyService
{
    public const string DefaultPolicyPath = "/etc/DaisyOS/allowed-apps.json";

    private readonly ILogService _log;

    public JsonAppPolicyService(ILogService? log = null)
        : this(DefaultPolicyPath, log)
    {
    }

    public JsonAppPolicyService(string policyPath, ILogService? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyPath);
        PolicyPath = Path.GetFullPath(policyPath);
        _log = log ?? NullLogService.Instance;
    }

    public string PolicyPath { get; }

    public async Task<AppPolicyStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var stream = new FileStream(
                PolicyPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var document = await JsonSerializer.DeserializeAsync(
                stream,
                DaisyOS.System.Serialization.DaisySystemJsonContext.Default.AppPolicyDocument,
                cancellationToken);

            return CreateLoadedStatus(document);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new AppPolicyStatus(
                AppPolicyConfigurationState.Unconfigured,
                PolicyPath,
                [],
                "No app policy is configured. Applications are unrestricted.");
        }
        catch (JsonException ex)
        {
            return CreateFailureStatus(
                AppPolicyConfigurationState.Malformed,
                "The app policy is malformed. Applications remain unrestricted.",
                ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return CreateFailureStatus(
                AppPolicyConfigurationState.Unavailable,
                "The app policy could not be read. Applications remain unrestricted.",
                ex);
        }
    }

    public AppPolicyDecision Evaluate(AppEntry app, AppPolicyStatus status)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(status);

        if (app.LaunchKind == AppLaunchKind.Internal)
        {
            return new AppPolicyDecision(
                AppPolicyDecisionKind.TrustedInternal,
                app.Id,
                null,
                "DaisyOS internal applications are trusted. The app policy is report-only.");
        }

        if (status.ConfigurationState != AppPolicyConfigurationState.Loaded)
        {
            return CreateNonLoadedDecision(app.Id, status.ConfigurationState);
        }

        var policyEntry = status.Apps.FirstOrDefault(entry =>
            string.Equals(entry.Id, app.Id, StringComparison.OrdinalIgnoreCase));

        if (policyEntry is null)
        {
            return new AppPolicyDecision(
                AppPolicyDecisionKind.Unlisted,
                app.Id,
                null,
                "This application is not listed in the configured policy. Launching is not blocked.");
        }

        return policyEntry.Allowed
            ? new AppPolicyDecision(
                AppPolicyDecisionKind.Allowed,
                app.Id,
                policyEntry,
                "This application is listed as allowed. The app policy is report-only.")
            : new AppPolicyDecision(
                AppPolicyDecisionKind.ExplicitlyDenied,
                app.Id,
                policyEntry,
                "This application is explicitly listed as denied. Launching is not blocked.");
    }

    private AppPolicyStatus CreateLoadedStatus(AppPolicyDocument? document)
    {
        if (document?.Apps is null)
        {
            return CreateMalformedStatus("The root object must contain an 'apps' array.");
        }

        var apps = new List<AppPolicyEntry>(document.Apps.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateCount = 0;

        for (var index = 0; index < document.Apps.Count; index++)
        {
            var app = document.Apps[index];
            if (app is null
                || string.IsNullOrWhiteSpace(app.Id)
                || string.IsNullOrWhiteSpace(app.Name)
                || string.IsNullOrWhiteSpace(app.Command)
                || string.IsNullOrWhiteSpace(app.Icon)
                || app.Allowed is null)
            {
                return CreateMalformedStatus(
                    $"App policy entry {index} must contain non-empty string fields " +
                    "'id', 'name', 'command', and 'icon', plus a boolean 'allowed' field.");
            }

            var id = app.Id.Trim();
            if (!seenIds.Add(id))
            {
                duplicateCount++;
                continue;
            }

            apps.Add(new AppPolicyEntry(
                id,
                app.Name.Trim(),
                app.Command.Trim(),
                app.Icon.Trim(),
                app.Allowed.Value));
        }

        var message = duplicateCount == 0
            ? $"Loaded {apps.Count} app policy entries. The policy is report-only."
            : $"Loaded {apps.Count} unique app policy entries and ignored {duplicateCount} " +
              "case-insensitive duplicate IDs. The policy is report-only.";

        return new AppPolicyStatus(
            AppPolicyConfigurationState.Loaded,
            PolicyPath,
            apps.ToArray(),
            message);
    }

    private AppPolicyStatus CreateMalformedStatus(string reason)
    {
        var exception = new JsonException(reason);
        return CreateFailureStatus(
            AppPolicyConfigurationState.Malformed,
            $"The app policy is malformed: {reason} Applications remain unrestricted.",
            exception);
    }

    private AppPolicyStatus CreateFailureStatus(
        AppPolicyConfigurationState state,
        string message,
        Exception exception)
    {
        _log.Log(LogLevel.Warning, $"Could not load app policy from {PolicyPath}.", exception);
        return new AppPolicyStatus(state, PolicyPath, [], message);
    }

    private static AppPolicyDecision CreateNonLoadedDecision(
        string appId,
        AppPolicyConfigurationState state)
    {
        return state switch
        {
            AppPolicyConfigurationState.Unconfigured => new AppPolicyDecision(
                AppPolicyDecisionKind.Unconfigured,
                appId,
                null,
                "No app policy is configured. Applications are unrestricted."),
            AppPolicyConfigurationState.Malformed => new AppPolicyDecision(
                AppPolicyDecisionKind.Malformed,
                appId,
                null,
                "The app policy is malformed. Applications remain unrestricted."),
            AppPolicyConfigurationState.Unavailable => new AppPolicyDecision(
                AppPolicyDecisionKind.Unavailable,
                appId,
                null,
                "The app policy is unavailable. Applications remain unrestricted."),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown app policy state.")
        };
    }

    public sealed class AppPolicyDocument
    {
        [JsonPropertyName("apps")]
        public List<AppPolicyEntryDocument?>? Apps { get; init; }
    }

    public sealed class AppPolicyEntryDocument
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("command")]
        public string? Command { get; init; }

        [JsonPropertyName("icon")]
        public string? Icon { get; init; }

        [JsonPropertyName("allowed")]
        public bool? Allowed { get; init; }
    }
}
