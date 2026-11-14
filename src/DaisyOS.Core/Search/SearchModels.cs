using System.Text;

namespace DaisyOS.Core.Search;

public enum SearchResultType { Application, File, Folder, Setting, Action, Window, Calculator, RecentItem }
public enum SearchResultKind { Primary, Secondary, Actionable, Informational }
public enum SearchStage { Immediate, Extended }
public enum SearchProviderStatus { Ready, Unavailable, TimedOut, Failed }

public sealed record SearchQuery(string RawText, string NormalizedText, IReadOnlyList<string> Tokens)
{
    public SearchFileFilters FileFilters { get; init; } = new();
    public static SearchQuery Parse(string? text)
    {
        text ??= "";
        // Bound work before normalization; raw input is never interpreted as executable text.
        var bounded = text.Length > 1024 ? text[..1024] : text;
        var (remaining, filters) = SearchFileFilters.Extract(bounded);
        var normalized = Normalize(remaining);
        return new(text, normalized, normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)) { FileFilters = filters };
    }

    public static string Normalize(string text)
    {
        string normalized;
        try { normalized = text.Normalize(NormalizationForm.FormKC); }
        catch (ArgumentException)
        {
            // A truncated UTF-16 pair or malformed metadata must not break search.
            normalized = string.Concat(text.EnumerateRunes().Select(r => r.ToString()))
                .Normalize(NormalizationForm.FormKC);
        }
        var builder = new StringBuilder(normalized.Length);
        Span<char> characters = stackalloc char[2];
        foreach (var rune in normalized.ToUpperInvariant().EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune)) builder.Append(characters[..rune.EncodeToUtf16(characters)]);
            else builder.Append(' ');
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

public abstract record SearchPayload;
public sealed record ApplicationSearchPayload(string ApplicationId) : SearchPayload;
public sealed record FileSearchPayload(string Path, bool IsDirectory) : SearchPayload;
public sealed record CalculatorSearchPayload(string Value) : SearchPayload;
public sealed record RegisteredActionSearchPayload(string ActionId) : SearchPayload;
public sealed record WindowSearchPayload(string WindowId) : SearchPayload;
public sealed record SettingSearchPayload(string SettingId) : SearchPayload;
public sealed record SearchAction(string Id, string ProviderId, string EntityId, string Label);

public sealed record SearchResult(
    string Id, string ProviderId, string Title, SearchResultType Type, double RelevanceScore,
    SearchPayload Payload)
{
    public string? Subtitle { get; init; }
    public string? Icon { get; init; }
    public SearchResultKind Kind { get; init; } = SearchResultKind.Actionable;
    public IReadOnlyList<string> MatchedTerms { get; init; } = [];
    public IReadOnlyList<SearchAction> Actions { get; init; } = [];
    public bool IsEnabled { get; init; } = true;
    public string? UnavailableReason { get; init; }
}

public sealed record SearchProviderCapabilities(
    string DisplayName, IReadOnlyList<SearchResultType> ResultTypes,
    SearchStage Stage = SearchStage.Immediate, int Priority = 0,
    bool SupportsActions = false, bool RequiresDatabase = false,
    bool Enabled = true, TimeSpan? Timeout = null);

public interface ISearchProvider
{
    string Id { get; }
    SearchProviderCapabilities Capabilities { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken);
    Task ExecuteAsync(SearchAction action, CancellationToken cancellationToken);
}

public interface ISearchIndexer
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task ReindexAsync(CancellationToken cancellationToken);
}

public sealed record ProviderDiagnostic(string ProviderId, SearchProviderStatus Status, double DurationMs, int CandidateCount);
public sealed record SearchSnapshot(long Generation, IReadOnlyList<SearchResult> Results,
    IReadOnlyList<ProviderDiagnostic> Providers, bool IsComplete, double ElapsedMs);

public sealed record SearchEngineDiagnostics(long CompletedQueries, long CancelledQueries,
    double AverageMs, double P95Ms, IReadOnlyList<ProviderDiagnostic> Providers);
