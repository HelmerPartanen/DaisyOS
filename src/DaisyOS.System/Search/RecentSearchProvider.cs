using DaisyOS.Core.Search;

namespace DaisyOS.System.Search;

/// <summary>Bounded local history of successfully opened indexed files.</summary>
public sealed class RecentSearchProvider(FileSearchService files) : ISearchProvider
{
    public string Id => "recent";
    public SearchProviderCapabilities Capabilities => new("Recent files", [SearchResultType.RecentItem, SearchResultType.File],
        SearchStage.Extended, SupportsActions: true, RequiresDatabase: true);
    public Task InitializeAsync(CancellationToken cancellationToken) => files.InitializeAsync(cancellationToken);
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        if (query.FileFilters.IsActive) return [];
        var explicitRecent = query.NormalizedText == "RECENT" || query.NormalizedText.StartsWith("RECENT ", StringComparison.Ordinal);
        var terms = explicitRecent ? SearchQuery.Parse(query.NormalizedText.Length > 6 ? query.NormalizedText[7..] : "") : query;
        var candidates = await files.RecentAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<SearchResult>();
        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = candidates[i];
            if (!files.IsAllowed(file.Path) || SearchFileAccess.Read(file.Path) is null) continue;
            var score = terms.Tokens.Count == 0 && explicitRecent ? 400 : SearchRanker.Score(new(file.Name, ""), terms);
            if (score <= 0) continue;
            results.Add(new(file.Path, Id, file.Name, SearchResultType.RecentItem, score + Math.Max(1, 50 - i),
                new FileSearchPayload(file.Path, file.IsDirectory))
            {
                Subtitle = file.Parent,
                Actions = [new("open", Id, file.Path, "Open"), new("reveal", Id, file.Path, "Open containing folder"),
                    new("copy-path", Id, file.Path, "Copy path")]
            });
        }
        return results.OrderByDescending(result => result.RelevanceScore).ThenBy(result => result.Id, StringComparer.Ordinal).Take(50).ToArray();
    }
    public async Task ExecuteAsync(SearchAction action, CancellationToken cancellationToken)
    {
        if (action.ProviderId != Id || !(await files.RecentAsync(cancellationToken).ConfigureAwait(false)).Any(file => file.Path == action.EntityId))
            throw new InvalidOperationException("This recent item is unavailable.");
        await files.ExecuteAsync(action with { ProviderId = files.Id }, cancellationToken).ConfigureAwait(false);
    }
}
