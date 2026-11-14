using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace DaisyOS.Core.Search;

/// <summary>A process-owned service. Each consumer creates an independent query session.</summary>
public sealed class SearchCoordinator
{
    private readonly ISearchProvider[] _providers;
    private readonly object _metricsGate = new();
    private readonly Queue<double> _latencies = new();
    private long _completed;
    private long _cancelled;
    private IReadOnlyList<ProviderDiagnostic> _lastProviders = [];
    internal void RecordCancellation() => Interlocked.Increment(ref _cancelled);
    public SearchEngineDiagnostics Diagnostics
    {
        get
        {
            lock (_metricsGate)
            {
                var sorted = _latencies.Order().ToArray();
                return new(_completed, Interlocked.Read(ref _cancelled), sorted.Length == 0 ? 0 : sorted.Average(),
                    sorted.Length == 0 ? 0 : sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], _lastProviders);
            }
        }
    }
    private void RecordCompleted(double milliseconds, IReadOnlyList<ProviderDiagnostic> providers)
    {
        lock (_metricsGate)
        {
            _completed++; _latencies.Enqueue(milliseconds);
            if (_latencies.Count > 256) _latencies.Dequeue();
            _lastProviders = providers.ToArray();
        }
    }
    private readonly SemaphoreSlim _workers;
    private readonly SemaphoreSlim _extendedWorkers;
    private sealed class ProviderState
    {
        public readonly SemaphoreSlim Execution = new(1, 1);
        public readonly SemaphoreSlim Initialization = new(1, 1);
        public bool Initialized;
    }
    private readonly Dictionary<string, ProviderState> _states;
    public SearchCoordinator(IEnumerable<ISearchProvider> providers, int concurrency = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
        _providers = providers.ToArray();
        if (_providers.Any(p => string.IsNullOrWhiteSpace(p.Id)) ||
            _providers.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != _providers.Length)
            throw new ArgumentException("Search providers require unique, nonempty IDs.", nameof(providers));
        _workers = new(concurrency, concurrency);
        _extendedWorkers = new(Math.Max(1, concurrency / 2), Math.Max(1, concurrency / 2));
        _states = _providers.ToDictionary(p => p.Id, _ => new ProviderState(), StringComparer.Ordinal);
    }

    public SearchSession CreateSession() => new(this);

    internal async IAsyncEnumerable<SearchSnapshot> SearchAsync(string text, long generation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var query = SearchQuery.Parse(text);
        if (query.Tokens.Count == 0 && !query.FileFilters.IsActive)
        {
            yield return new(generation, [], [], true, timer.Elapsed.TotalMilliseconds);
            yield break;
        }
        var results = new Dictionary<string, SearchResult>(StringComparer.Ordinal);
        var diagnostics = new List<ProviderDiagnostic>();
        // Immediate providers get a head start, without waiting for slow extended providers.
        var pending = _providers.Where(p => p.Capabilities.Enabled && (!query.FileFilters.IsActive ||
                p.Capabilities.ResultTypes.Any(type => type is SearchResultType.File or SearchResultType.Folder)))
            .OrderBy(p => p.Capabilities.Stage).ThenByDescending(p => p.Capabilities.Priority)
            .Select(p => RunProviderAsync(p, query, cancellationToken)).ToList();
        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
            pending.Remove(finished);
            var outcome = await finished.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(outcome.Diagnostic);
            foreach (var result in outcome.Results)
            {
                // Provider ownership is part of identity; IDs never route actions to another provider.
                var key = result.Payload switch
                {
                    FileSearchPayload file => "file:" + file.Path,
                    ApplicationSearchPayload app => "app:" + app.ApplicationId,
                    WindowSearchPayload window => "window:" + window.WindowId,
                    _ => result.ProviderId + ":" + result.Id
                };
                if (!results.TryGetValue(key, out var previous) || result.RelevanceScore > previous.RelevanceScore)
                    results[key] = result;
            }
            var ordered = results.Values.OrderByDescending(r => r.RelevanceScore)
                .ThenBy(r => r.ProviderId, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
                .Take(50).ToArray();
            if (pending.Count == 0) RecordCompleted(timer.Elapsed.TotalMilliseconds, diagnostics);
            yield return new(generation, ordered, diagnostics.ToArray(), pending.Count == 0, timer.Elapsed.TotalMilliseconds);
        }
        if (diagnostics.Count == 0)
            yield return new(generation, [], [], true, timer.Elapsed.TotalMilliseconds);
    }

    private async Task<(IReadOnlyList<SearchResult> Results, ProviderDiagnostic Diagnostic)> RunProviderAsync(
        ISearchProvider provider, SearchQuery query, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(provider.Capabilities.Timeout ?? TimeSpan.FromMilliseconds(250));
        var entered = false;
        var providerEntered = false;
        var extendedEntered = false;
        var state = _states[provider.Id];
        try
        {
            await state.Execution.WaitAsync(deadline.Token).ConfigureAwait(false);
            providerEntered = true;
            if (provider.Capabilities.Stage == SearchStage.Extended)
            {
                await _extendedWorkers.WaitAsync(deadline.Token).ConfigureAwait(false);
                extendedEntered = true;
            }
            await _workers.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            // Isolation includes providers doing synchronous work before returning a task.
            var work = Task.Run(async () =>
            {
                await state.Initialization.WaitAsync(deadline.Token).ConfigureAwait(false);
                try
                {
                    if (!state.Initialized)
                    {
                        await provider.InitializeAsync(deadline.Token).ConfigureAwait(false);
                        state.Initialized = true;
                    }
                }
                finally { state.Initialization.Release(); }
                return await provider.SearchAsync(query, deadline.Token).ConfigureAwait(false);
            }, deadline.Token);
            // Keep a worker occupied until a non-cooperative provider actually exits.
            var releaseExtended = extendedEntered;
            _ = work.ContinueWith(t =>
            {
                _ = t.Exception;
                _workers.Release();
                state.Execution.Release();
                if (releaseExtended) _extendedWorkers.Release();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            entered = false; providerEntered = false; extendedEntered = false;
            var candidates = await work.WaitAsync(deadline.Token).ConfigureAwait(false);
            var valid = candidates.Where(r => r.ProviderId == provider.Id && double.IsFinite(r.RelevanceScore))
                .OrderByDescending(r => r.RelevanceScore).ThenBy(r => r.Id, StringComparer.Ordinal)
                .DistinctBy(r => r.Id, StringComparer.Ordinal).Take(50).ToArray();
            return (valid, new(provider.Id, SearchProviderStatus.Ready, timer.Elapsed.TotalMilliseconds, candidates.Count));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ([], new(provider.Id, SearchProviderStatus.TimedOut, timer.Elapsed.TotalMilliseconds, 0));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Diagnostics deliberately contain neither queries, paths nor exception messages.
            return ([], new(provider.Id, SearchProviderStatus.Failed, timer.Elapsed.TotalMilliseconds, 0));
        }
        finally
        {
            if (entered) _workers.Release();
            if (providerEntered) state.Execution.Release();
            if (extendedEntered) _extendedWorkers.Release();
        }
    }

    public Task ExecuteAsync(SearchAction action, CancellationToken cancellationToken = default)
    {
        var provider = _providers.SingleOrDefault(p => p.Id == action.ProviderId);
        if (provider is null || !provider.Capabilities.Enabled || !provider.Capabilities.SupportsActions)
            throw new InvalidOperationException("This search action is unavailable.");
        return provider.ExecuteAsync(action, cancellationToken);
    }
}

/// <summary>Latest-query cancellation is scoped to one consumer, not the shared service.</summary>
public sealed class SearchSession(SearchCoordinator coordinator) : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private long _generation;
    private bool _disposed;
    public async IAsyncEnumerable<SearchSnapshot> SearchAsync(string text,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        CancellationTokenSource source;
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is { IsCancellationRequested: false }) { coordinator.RecordCancellation(); _current.Cancel(); }
            _current = source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            generation = ++_generation;
        }
        try
        {
            await foreach (var snapshot in coordinator.SearchAsync(text, generation, source.Token).ConfigureAwait(false))
            {
                lock (_gate) { if (_generation != generation) yield break; }
                yield return snapshot;
            }
        }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_current, source)) _current = null; }
            source.Dispose();
        }
    }
    public void Dispose()
    {
        lock (_gate) { _disposed = true; ++_generation; _current?.Cancel(); }
    }
}
