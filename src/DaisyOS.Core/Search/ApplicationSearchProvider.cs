using DaisyOS.Core.Models;

namespace DaisyOS.Core.Search;

/// <summary>Consumes the existing XDG catalog. Discovery and launching remain outside search.</summary>
public sealed class ApplicationSearchProvider : ISearchProvider
{
    private sealed record Entry(AppEntry App, SearchDocument Document);
    private Entry[] _catalog = [];
    private readonly Func<string, CancellationToken, Task>? _launch;
    private readonly Func<string, string, CancellationToken, Task>? _launchAction;
    public Func<string, double>? UsageBoost { get; set; }
    public string Id => "applications";
    public SearchProviderCapabilities Capabilities => new("Applications", [SearchResultType.Application],
        Priority: 100, SupportsActions: _launch is not null);
    public ApplicationSearchProvider(Func<string, CancellationToken, Task>? launch = null,
        Func<string, string, CancellationToken, Task>? launchAction = null) { _launch = launch; _launchAction = launchAction; }
    public void ReplaceCatalog(IEnumerable<AppEntry> apps) => Volatile.Write(ref _catalog,
        apps.DistinctBy(a => a.Id, StringComparer.Ordinal).Select(a => new Entry(a,
            new(a.Name, a.Description, (a.SearchTerms ?? []).Concat(a.Categories ?? []).Append(a.Id)))).ToArray());
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public IReadOnlyList<SearchResult> Search(SearchQuery query, CancellationToken cancellationToken = default) =>
        SearchCore(query, int.MaxValue, cancellationToken);

    private IReadOnlyList<SearchResult> SearchCore(SearchQuery query, int limit, CancellationToken cancellationToken)
    {
        var candidates = new PriorityQueue<(Entry Entry, double Score, int Index), (double Score, int ReverseIndex)>();
        var catalog = Volatile.Read(ref _catalog);
        for (var index = 0; index < catalog.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = catalog[index];
            var relevance = SearchRanker.Score(entry.Document, query);
            if (relevance <= 0) continue;
            var boost = UsageBoost?.Invoke(entry.App.Id) ?? 0;
            var score = relevance + (double.IsFinite(boost) ? Math.Clamp(boost, 0, 65) : 0);
            if (candidates.Count < limit) candidates.Enqueue((entry, score, index), (score, -index));
            else candidates.EnqueueDequeue((entry, score, index), (score, -index));
        }
        return candidates.UnorderedItems.Select(item => item.Element)
            .OrderByDescending(item => item.Score).ThenBy(item => item.Index)
            .Select(item => new SearchResult(item.Entry.App.Id, Id, item.Entry.App.Name,
                SearchResultType.Application, item.Score, new ApplicationSearchPayload(item.Entry.App.Id))
            {
                Subtitle = item.Entry.App.Description, Icon = item.Entry.App.Icon, MatchedTerms = query.Tokens,
                Actions = _launch is null ? [] : new[] { new SearchAction("open", Id, item.Entry.App.Id, "Open") }
                    .Concat(_launchAction is null ? [] : item.Entry.App.DesktopActions.Select(action =>
                        new SearchAction("desktop-action:" + action.Id, Id, item.Entry.App.Id, action.Name))).ToArray()
            }).ToArray();
    }
    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(SearchCore(query, 50, cancellationToken));
    public Task ExecuteAsync(SearchAction action, CancellationToken cancellationToken)
    {
        var entry = Volatile.Read(ref _catalog).FirstOrDefault(e => e.App.Id == action.EntityId);
        if (action.ProviderId != Id || entry is null) throw new InvalidOperationException("This application action is unavailable.");
        if (action.Id == "open" && _launch is not null) return _launch(action.EntityId, cancellationToken);
        const string prefix = "desktop-action:";
        if (action.Id.StartsWith(prefix, StringComparison.Ordinal) && _launchAction is not null &&
            entry.App.DesktopActions.Any(a => a.Id == action.Id[prefix.Length..]))
            return _launchAction(action.EntityId, action.Id[prefix.Length..], cancellationToken);
        throw new InvalidOperationException("This application action is unavailable.");
    }
}
