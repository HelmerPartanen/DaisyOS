using System.Threading.Channels;

namespace DaisyOS.Core.Continuity;

[Flags]
public enum ContinuityFeatures { None = 0, Clipboard = 1, Files = 2 }
public enum ContinuityPeerState { TrustedOffline, Connecting, Connected, Reconnecting, Stopped, Revoked, Error }

public sealed record ContinuityHello(int Major, int Minor, string DeviceId, ContinuityFeatures Features);
public enum ContinuityPeerError { None, IdentityMismatch, InvalidHello }
public sealed record ContinuityPeerSnapshot(long Revision, ContinuityPeerState State, ContinuityFeatures Features,
    ContinuityPeerError Error = ContinuityPeerError.None);

/// <summary>Transport adapters must expose the cryptographically verified identity,
/// not an advertisement or Hello claim. Implementations must honor cancellation.</summary>
public interface IContinuityPeerConnection : IAsyncDisposable
{
    string AuthenticatedPeerId { get; }
    ValueTask<ContinuityHello> ExchangeHelloAsync(ContinuityHello local, CancellationToken cancellationToken);
    ValueTask WaitForDisconnectAsync(CancellationToken cancellationToken);
}

public interface IContinuityPeerConnector
{
    ValueTask<IContinuityPeerConnection> ConnectAsync(string trustedPeerId, CancellationToken cancellationToken);
}

public interface IContinuityRetryDelay
{
    ValueTask WaitAsync(int failureCount, CancellationToken cancellationToken);
}

/// <summary>Bounded exponential backoff with jitter; no polling while connected.</summary>
public sealed class ContinuityRetryDelay : IContinuityRetryDelay
{
    public ValueTask WaitAsync(int failureCount, CancellationToken cancellationToken)
    {
        var ceilingMs = Math.Min(30_000, 500 * (1 << Math.Clamp(failureCount - 1, 0, 6)));
        var milliseconds = Random.Shared.Next(ceilingMs / 2, ceilingMs + 1);
        return new ValueTask(Task.Delay(milliseconds, cancellationToken));
    }
}

/// <summary>
/// Lifecycle of one already-trusted peer. This is not pairing or a network
/// implementation. It owns each connection, negotiates only MVP features, and
/// cannot promote an untrusted discovery candidate. No feature data is dispatched.
/// </summary>
public sealed class ContinuityPeerSession : IAsyncDisposable
{
    private const ContinuityFeatures Supported = ContinuityFeatures.Clipboard | ContinuityFeatures.Files;
    private readonly object _gate = new();
    private readonly Channel<ContinuityPeerSnapshot> _changes = Channel.CreateBounded<ContinuityPeerSnapshot>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, AllowSynchronousContinuations = false });
    private readonly string _localId;
    private readonly string _peerId;
    private readonly IContinuityPeerConnector _connector;
    private readonly IContinuityRetryDelay _retry;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ContinuityPeerSnapshot _snapshot = new(0, ContinuityPeerState.TrustedOffline, ContinuityFeatures.None);
    private bool _started;
    private bool _disposed;
    private bool _revoked;
    private bool _stopped;

    public ContinuityPeerSession(string localId, string trustedPeerId,
        IContinuityPeerConnector connector, IContinuityRetryDelay? retry = null)
    {
        ValidateId(localId);
        ValidateId(trustedPeerId);
        if (localId == trustedPeerId) throw new ArgumentException("Cannot connect to self.", nameof(trustedPeerId));
        _localId = localId;
        _peerId = trustedPeerId;
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        _retry = retry ?? new ContinuityRetryDelay();
    }

    public ContinuityPeerSnapshot Snapshot { get { lock (_gate) return _snapshot; } }

    /// <summary>Coalesced state changes for one local IPC/UI subscriber. Revision
    /// numbers expose skipped intermediate states; Snapshot is authoritative.</summary>
    public IAsyncEnumerable<ContinuityPeerSnapshot> WatchAsync(CancellationToken cancellationToken = default) =>
        _changes.Reader.ReadAllAsync(cancellationToken);

    /// <summary>Run once, only after the host has enabled sharing in an unlocked
    /// active session. Lock/pause/logout must cancel this run and discard it.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) throw new InvalidOperationException("Peer session can only run once.");
            _started = true;
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = lifetime.Token;
        var failures = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                SetState(ContinuityPeerState.Connecting);
                try
                {
                    using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token);
                    handshake.CancelAfter(TimeSpan.FromSeconds(10));
                    await using var connection = await _connector.ConnectAsync(_peerId, handshake.Token).ConfigureAwait(false);
                    if (!string.Equals(connection.AuthenticatedPeerId, _peerId, StringComparison.Ordinal))
                    {
                        SetState(ContinuityPeerState.Error, error: ContinuityPeerError.IdentityMismatch);
                        return;
                    }
                    var hello = await connection.ExchangeHelloAsync(new(1, 0, _localId, Supported), handshake.Token).ConfigureAwait(false);
                    if (hello.Major != 1 || hello.Minor < 0 || hello.DeviceId != _peerId)
                    {
                        SetState(ContinuityPeerState.Error, error: ContinuityPeerError.InvalidHello);
                        return;
                    }
                    handshake.Token.ThrowIfCancellationRequested();
                    handshake.CancelAfter(Timeout.InfiniteTimeSpan);
                    SetState(ContinuityPeerState.Connected, hello.Features & Supported);
                    await connection.WaitForDisconnectAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (InvalidDataException)
                {
                    SetState(ContinuityPeerState.Error, error: ContinuityPeerError.InvalidHello);
                    break;
                }
                catch (OperationCanceledException) { } // Handshake deadline, retry with backoff.
                catch (IOException) { }
                token.ThrowIfCancellationRequested();
                SetState(ContinuityPeerState.Reconnecting);
                // Do not reset on a successful Hello: repeated immediate drops
                // must still back off rather than form a tight reconnect loop.
                failures = Math.Min(failures + 1, 32);
                await _retry.WaitAsync(failures, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (Snapshot.State != ContinuityPeerState.Error) SetState(ContinuityPeerState.Stopped);
            _changes.Writer.TryComplete();
            _finished.TrySetResult();
        }
    }

    /// <summary>Immediately invalidate negotiated features on lock, pause or
    /// logout. A new instance is required after the host authorizes resume.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed || _revoked || _stopped) return;
            _stopped = true;
            _snapshot = new(_snapshot.Revision + 1, ContinuityPeerState.Stopped, ContinuityFeatures.None);
            _changes.Writer.TryWrite(_snapshot);
        }
        _lifetime.Cancel();
    }

    /// <summary>Irreversible for this instance. Invalidates features before
    /// cancellation; late transport callbacks cannot restore Connected.</summary>
    public void Revoke()
    {
        lock (_gate)
        {
            if (_disposed || _revoked) return;
            _revoked = true;
            _snapshot = new(_snapshot.Revision + 1, ContinuityPeerState.Revoked, ContinuityFeatures.None);
            _changes.Writer.TryWrite(_snapshot);
        }
        _lifetime.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (!_revoked)
                    _snapshot = new(_snapshot.Revision + 1, ContinuityPeerState.Stopped, ContinuityFeatures.None);
                _changes.Writer.TryWrite(_snapshot);
                if (!_started)
                {
                    _changes.Writer.TryComplete();
                    _finished.TrySetResult();
                }
            }
        }
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _finished.Task.ConfigureAwait(false);
        // Keep the cancelled source alive for concurrent Revoke/Dispose callers.
        // It has no timer or wait handle and is reclaimed with this instance.
    }

    private void SetState(ContinuityPeerState state, ContinuityFeatures features = ContinuityFeatures.None,
        ContinuityPeerError error = ContinuityPeerError.None)
    {
        lock (_gate)
        {
            if (_disposed || _revoked || _stopped) return;
            _snapshot = new(_snapshot.Revision + 1, state, features, error);
            _changes.Writer.TryWrite(_snapshot);
        }
    }

    private static void ValidateId(string id)
    {
        if (id is null || id.Length != 64 || id.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Device identity must be 32 bytes in lowercase hexadecimal.", nameof(id));
    }
}
