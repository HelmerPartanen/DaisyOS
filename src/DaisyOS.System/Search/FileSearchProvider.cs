using DaisyOS.Core.Search;

namespace DaisyOS.System.Search;

public sealed class FileSearchProvider(FileSearchDatabase database, FileSearchIndexer indexer,
    Func<string, bool, CancellationToken, Task> open) : ISearchProvider
{
    public string Id => "files";
    public SearchProviderCapabilities Capabilities => new("Files and folders", [SearchResultType.File, SearchResultType.Folder],
        SearchStage.Extended, SupportsActions: true, RequiresDatabase: true, Timeout: TimeSpan.FromMilliseconds(150));
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var candidates = database.Search(query, cancellationToken);
        var fuzzy = candidates.Count == 0 && query.Tokens.Count == 1 && query.NormalizedText.Length >= 4;
        if (fuzzy) candidates = database.Search(SearchQuery.Parse(query.NormalizedText[..2]) with { FileFilters = query.FileFilters }, cancellationToken);
        var results = new List<SearchResult>();
        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Per-candidate permission/identity checks, never directory enumeration during queries.
            if (!indexer.IsAllowed(file.Path) || SearchFileAccess.Read(file.Path) is not { } current || current.IsDirectory != file.IsDirectory) continue;
            var score = SearchRanker.Score(new(file.Name, "", [file.Extension]), query);
            if (fuzzy && score <= 0) continue;
            results.Add(new(file.Path, Id, file.Name, file.IsDirectory ? SearchResultType.Folder : SearchResultType.File,
                score > 0 ? score : 200, new FileSearchPayload(file.Path, file.IsDirectory))
            {
                Subtitle = file.Parent, Icon = file.IsDirectory ? "folder" : "file",
                Actions = [new("open", Id, file.Path, "Open"), new("reveal", Id, file.Path, "Open containing folder"),
                    new("copy-path", Id, file.Path, "Copy path")]
            });
        }
        return Task.FromResult<IReadOnlyList<SearchResult>>(results.OrderByDescending(r => r.RelevanceScore)
            .ThenBy(r => r.Id, StringComparer.Ordinal).Take(50).ToArray());
    }
    public async Task ExecuteAsync(SearchAction action, CancellationToken cancellationToken)
    {
        if (action.ProviderId != Id || action.Id is not ("open" or "reveal") || !indexer.IsAllowed(action.EntityId) ||
            SearchFileAccess.Read(action.EntityId) is null) throw new InvalidOperationException("This file is no longer accessible.");
        await open(action.EntityId, action.Id == "reveal", cancellationToken).ConfigureAwait(false);
        if (action.Id == "open") database.RecordUse(action.EntityId);
    }
}
