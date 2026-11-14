using System.Diagnostics;
using DaisyOS.Core.Models;
using DaisyOS.Core.Search;

namespace DaisyOS.Core.Tests.Search;

public sealed class SearchTests
{
    [Theory]
    [InlineData("  café—notes  ", "CAFÉ NOTES")]
    [InlineData("Ｃａｆｅ", "CAFE")]
    [InlineData("a\t b\n c", "A B C")]
    public void QueriesNormalizeUnicodePunctuationAndWhitespace(string raw, string normalized)
    {
        var query = SearchQuery.Parse(raw);
        Assert.Equal(raw, query.RawText);
        Assert.Equal(normalized, query.NormalizedText);
    }

    [Fact]
    public void RankingPreservesExactPrefixAcronymAliasAndDeterministicOrder()
    {
        var provider = new ApplicationSearchProvider { UsageBoost = _ => 100000 };
        provider.ReplaceCatalog([
            new("exact", "File", "", "", false),
            new("prefix", "Files", "", "", false),
            new("description", "Other", "File utility", "", false),
            new("sysmon", "System Monitor", "", "", false),
            new("unicode", "Café Notes", "", "", false),
            new("alias", "Web", "", "", false, SearchTerms: ["browser"])]);
        Assert.Equal(["exact", "prefix", "description"], provider.Search(SearchQuery.Parse("file")).Select(r => r.Id));
        Assert.Equal("sysmon", Assert.Single(provider.Search(SearchQuery.Parse("sm"))).Id);
        Assert.Equal("unicode", Assert.Single(provider.Search(SearchQuery.Parse("Cafe\u0301"))).Id);
        Assert.Equal("alias", Assert.Single(provider.Search(SearchQuery.Parse("browser"))).Id);
        Assert.Empty(provider.Search(SearchQuery.Parse(" ")));
    }

    [Fact]
    public async Task CoordinatorPublishesFastResultsBeforeSlowProviderAndIsolatesFailure()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fast = new Provider("fast");
        var slow = new Provider("slow", async token => { await release.Task.WaitAsync(token); return [Result("slow", "s")]; });
        var broken = new Provider("broken", _ => throw new IOException("private/path"));
        using var session = new SearchCoordinator([fast, slow, broken]).CreateSession();
        var snapshots = new List<SearchSnapshot>();
        await foreach (var snapshot in session.SearchAsync("test"))
        {
            snapshots.Add(snapshot);
            if (snapshot.Results.Any(r => r.ProviderId == "fast")) release.TrySetResult();
        }
        Assert.Contains(snapshots, s => !s.IsComplete && s.Results.Any(r => r.ProviderId == "fast"));
        Assert.Equal(2, snapshots[^1].Results.Count);
        Assert.Contains(snapshots[^1].Providers, p => p.Status == SearchProviderStatus.Failed);
    }

    [Fact]
    public async Task ProviderTimeoutDoesNotBlockOtherResultsEvenWhenItIgnoresCancellation()
    {
        var release = new TaskCompletionSource<IReadOnlyList<SearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hung = new Provider("hung", _ => release.Task, TimeSpan.FromMilliseconds(30));
        try
        {
            using var session = new SearchCoordinator([hung, new Provider("fast")]).CreateSession();
            var last = await LastAsync(session, "test");
            Assert.Single(last.Results);
            Assert.Contains(last.Providers, p => p.ProviderId == "hung" && p.Status == SearchProviderStatus.TimedOut);
        }
        finally { release.TrySetResult([]); }
    }

    [Fact]
    public async Task NewQueryCancelsPreviousSearchAndSessionsRemainIndependent()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider("slow", async token =>
        {
            started.TrySetResult();
            await Task.Delay(100, token);
            return [Result("slow", "s")];
        }, TimeSpan.FromSeconds(2));
        var coordinator = new SearchCoordinator([provider]);
        using var firstSession = coordinator.CreateSession();
        using var otherSession = coordinator.CreateSession();
        var old = LastAsync(firstSession, "old");
        await started.Task;
        var other = LastAsync(otherSession, "other");
        var current = LastAsync(firstSession, "new");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        Assert.True((await current).IsComplete);
        Assert.True((await other).IsComplete);
    }

    [Fact]
    public async Task CoordinatorDeduplicatesBoundsResultsAndRejectsForeignOwnership()
    {
        var provider = new Provider("p", _ => Task.FromResult<IReadOnlyList<SearchResult>>(
            Enumerable.Range(0, 100).Select(i => Result("p", i.ToString())).Concat([
                Result("p", "0"), Result("other", "foreign"), Result("p", "nan") with { RelevanceScore = double.NaN }]).ToArray()));
        using var session = new SearchCoordinator([provider]).CreateSession();
        var last = await LastAsync(session, "test");
        Assert.Equal(50, last.Results.Count);
        Assert.Equal(50, last.Results.Select(r => r.Id).Distinct().Count());
        Assert.DoesNotContain(last.Results, r => r.ProviderId == "other" || r.Id == "nan");
        Assert.Throws<ArgumentException>(() => new SearchCoordinator([provider, provider]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SearchCoordinator([provider])
            .ExecuteAsync(new("arbitrary", "p", "unknown", "bad")));
    }

    [Fact]
    public async Task SettingsReadActualServiceStateAndRejectDuplicateIdsAndOverlappingUpdates()
    {
        var value = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new SearchSettingsRegistry();
        var setting = new SearchableBooleanSetting("motion", "Reduced motion", "Accessibility", ["animation"],
            _ => Task.FromResult(value), async (requested, _) => { entered.SetResult(); await release.Task; value = requested; });
        registry.Register(setting);
        Assert.Throws<ArgumentException>(() => registry.Register(setting));
        var update = registry.SetAsync("motion", true);
        await entered.Task;
        Assert.NotNull((await registry.SetAsync("motion", true)).Error);
        release.SetResult();
        Assert.Equal(new SettingUpdateResult(true, null), await update);
        value = false;
        Assert.False(await registry.ReadAsync("motion"));
        Assert.True(registry.Unregister("motion"));
        await Assert.ThrowsAsync<KeyNotFoundException>(async () => await registry.ReadAsync("motion"));
    }

    [Fact]
    public async Task FailedSettingWriteReturnsActualValueAndExplicitError()
    {
        var registry = new SearchSettingsRegistry();
        registry.Register(new("contrast", "High contrast", "Accessibility", [], _ => Task.FromResult(false),
            (_, _) => throw new IOException("sensitive detail")));
        var result = await registry.SetAsync("contrast", true);
        Assert.False(result.Value);
        Assert.Equal("The setting could not be updated.", result.Error);
    }

    [Fact]
    public async Task UnknownApplicationActionNeverReachesLauncher()
    {
        var launches = 0;
        var provider = new ApplicationSearchProvider((_, _) => { launches++; return Task.CompletedTask; });
        provider.ReplaceCatalog([new("known", "Known", "", "", false)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ExecuteAsync(new("open", provider.Id, "$(touch /tmp/no)", "Open"), default));
        await provider.ExecuteAsync(new("open", provider.Id, "known", "Open"), default);
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task ProviderInitializationIsSharedAcrossConcurrentConsumers()
    {
        var provider = new Provider("shared");
        var coordinator = new SearchCoordinator([provider]);
        using var first = coordinator.CreateSession();
        using var second = coordinator.CreateSession();
        await Task.WhenAll(LastAsync(first, "one"), LastAsync(second, "two"));
        await LastAsync(first, "three");
        Assert.Equal(1, provider.Initializations);
    }

    [Fact]
    public async Task ProviderConcurrencyIsBoundedAcrossAllSessions()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var full = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var peak = 0;
        var provider = new Provider("bounded", async token =>
        {
            var active = Interlocked.Increment(ref running);
            if (active >= 2) Interlocked.Exchange(ref peak, active);
            else Interlocked.CompareExchange(ref peak, active, 0);
            if (active == 2) full.TrySetResult();
            try { await release.Task.WaitAsync(token); return [Result("bounded", "result")]; }
            finally { Interlocked.Decrement(ref running); }
        });
        var coordinator = new SearchCoordinator([provider, new Provider("bounded-second", async token =>
        {
            var active = Interlocked.Increment(ref running);
            if (active >= 2) Interlocked.Exchange(ref peak, active);
            else Interlocked.CompareExchange(ref peak, active, 0);
            if (active == 2) full.TrySetResult();
            try { await release.Task.WaitAsync(token); return [Result("bounded-second", "result")]; }
            finally { Interlocked.Decrement(ref running); }
        })], concurrency: 2);
        var sessions = Enumerable.Range(0, 10).Select(_ => coordinator.CreateSession()).ToArray();
        try
        {
            var work = sessions.Select(s => LastAsync(s, "test")).ToArray();
            await full.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, Volatile.Read(ref running));
            release.TrySetResult();
            await Task.WhenAll(work);
            Assert.Equal(2, peak);
        }
        finally { release.TrySetResult(); foreach (var session in sessions) session.Dispose(); }
    }

    [Fact]
    public async Task HungExtendedProvidersCannotStarveImmediateResultsAcrossQueries()
    {
        var release = new TaskCompletionSource<IReadOnlyList<SearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var providers = Enumerable.Range(0, 4).Select(i => (ISearchProvider)new Provider("hung-" + i,
            _ => release.Task, TimeSpan.FromMilliseconds(40), SearchStage.Extended)).Append(new Provider("fast")).ToArray();
        using var session = new SearchCoordinator(providers).CreateSession();
        try
        {
            Assert.Contains((await LastAsync(session, "one")).Results, result => result.ProviderId == "fast");
            Assert.Contains((await LastAsync(session, "two")).Results, result => result.ProviderId == "fast");
        }
        finally { release.TrySetResult([]); }
    }

    private static SearchResult Result(string provider, string id) =>
        new(id, provider, "Test", SearchResultType.Application, 100, new ApplicationSearchPayload(id));
    private static async Task<SearchSnapshot> LastAsync(SearchSession session, string text)
    {
        SearchSnapshot? last = null;
        await foreach (var snapshot in session.SearchAsync(text)) last = snapshot;
        return last!;
    }
    private sealed class Provider(string id, Func<CancellationToken, Task<IReadOnlyList<SearchResult>>>? search = null,
        TimeSpan? timeout = null, SearchStage stage = SearchStage.Immediate) : ISearchProvider
    {
        public string Id => id;
        public SearchProviderCapabilities Capabilities => new(id, [SearchResultType.Application], Stage: stage, Timeout: timeout ?? TimeSpan.FromSeconds(5));
        public int Initializations;
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Initializations);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken) =>
            search?.Invoke(cancellationToken) ?? Task.FromResult<IReadOnlyList<SearchResult>>([Result(id, id)]);
        public Task ExecuteAsync(SearchAction action, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
