namespace DaisyOS.Core.Search;

public sealed record RegisteredSearchAction(string Id, string Name, string Category,
    IReadOnlyList<string> Keywords, Func<CancellationToken, Task> ExecuteAsync);

/// <summary>Only explicitly registered actions can be dispatched; raw query text is never executed.</summary>
public sealed class RegisteredActionSearchProvider : ISearchProvider
{
    private readonly IReadOnlyDictionary<string, (RegisteredSearchAction Action, SearchDocument Document)> _actions;
    public string Id => "actions";
    public SearchProviderCapabilities Capabilities => new("Actions", [SearchResultType.Action], Priority: 80, SupportsActions: true);
    public RegisteredActionSearchProvider(IEnumerable<RegisteredSearchAction> actions) => _actions = actions.ToDictionary(
        a => a.Id, a => (a, new SearchDocument(a.Name, a.Category, a.Keywords)), StringComparer.Ordinal);
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        foreach (var (action, document) in _actions.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var score = SearchRanker.Score(document, query);
            if (score <= 0) continue;
            results.Add(new(action.Id, Id, action.Name, SearchResultType.Action, score, new RegisteredActionSearchPayload(action.Id))
            { Subtitle = action.Category, Actions = [new("execute", Id, action.Id, action.Name)] });
        }
        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
    public Task ExecuteAsync(SearchAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (action.ProviderId != Id || action.Id != "execute" || !_actions.TryGetValue(action.EntityId, out var registered))
            throw new InvalidOperationException("This action is unavailable.");
        return registered.Action.ExecuteAsync(cancellationToken);
    }
}
