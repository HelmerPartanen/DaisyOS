using System.Threading.Channels;
using DaisyOS.Core.Continuity;

namespace DaisyOS.Core.Tests;

public sealed class ContinuityPeerSessionTests
{
    private static readonly string A = new('a', 64);
    private static readonly string B = new('b', 64);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TwoMockPeersExchangeHelloWithoutGrantingFeaturePermissions()
    {
        var leftHello = Channel.CreateBounded<ContinuityHello>(1);
        var rightHello = Channel.CreateBounded<ContinuityHello>(1);
        var left = new MockConnection(B, rightHello.Reader, leftHello.Writer);
        var right = new MockConnection(A, leftHello.Reader, rightHello.Writer);
        await using var first = new ContinuityPeerSession(A, B, new Connector(left));
        await using var second = new ContinuityPeerSession(B, A, new Connector(right));
        var firstRun = first.RunAsync();
        var secondRun = second.RunAsync();
        await Task.WhenAll(left.Connected.Task, right.Connected.Task).WaitAsync(Deadline);
        Assert.Equal(ContinuityPeerState.Connected, first.Snapshot.State);
        Assert.Equal(ContinuityFeatures.Clipboard | ContinuityFeatures.Files, first.Snapshot.Features);
        // Negotiated support cannot turn a default permission into a grant.
        Assert.NotEqual(ContinuityDenial.None,
            ContinuityPolicy.Evaluate(new(), ContinuityCapability.Clipboard, DateTimeOffset.UtcNow));
        first.Revoke();
        second.Revoke();
        await Task.WhenAll(firstRun, secondRun).WaitAsync(Deadline);
        Assert.True(left.Disposed);
        Assert.True(right.Disposed);
        Assert.Equal(ContinuityPeerState.Revoked, first.Snapshot.State);
        Assert.Equal(ContinuityFeatures.None, first.Snapshot.Features);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task WrongIdentityOrVersionStopsWithoutRetry(bool wrongIdentity, int major)
    {
        var connection = new MockConnection(wrongIdentity ? A : B,
            new ContinuityHello(major, 0, B, ContinuityFeatures.Files));
        var retry = new ControlledRetry();
        await using var session = new ContinuityPeerSession(A, B, new Connector(connection), retry);
        await session.RunAsync().WaitAsync(Deadline);
        Assert.Equal(ContinuityPeerState.Error, session.Snapshot.State);
        Assert.Equal(wrongIdentity ? ContinuityPeerError.IdentityMismatch : ContinuityPeerError.InvalidHello, session.Snapshot.Error);
        Assert.Equal(ContinuityFeatures.None, session.Snapshot.Features);
        Assert.True(connection.Disposed);
        Assert.Equal(0, retry.Calls);
    }

    [Fact]
    public async Task SpoofedHelloCannotOverrideAuthenticatedIdentity()
    {
        var connection = new MockConnection(B, new ContinuityHello(1, 0, A, ContinuityFeatures.Files));
        await using var session = new ContinuityPeerSession(A, B, new Connector(connection));
        await session.RunAsync().WaitAsync(Deadline);
        Assert.Equal(ContinuityPeerState.Error, session.Snapshot.State);
        Assert.Equal(ContinuityPeerError.InvalidHello, session.Snapshot.Error);
        Assert.False(connection.Connected.Task.IsCompleted);
    }

    [Fact]
    public async Task RetryIsControlledAndCancellationStopsPendingBackoff()
    {
        var retry = new ControlledRetry();
        var connector = new FailingConnector();
        await using var session = new ContinuityPeerSession(A, B, connector, retry);
        using var cancellation = new CancellationTokenSource();
        var run = session.RunAsync(cancellation.Token);
        Assert.Equal(1, await retry.Attempts.Reader.ReadAsync().AsTask().WaitAsync(Deadline));
        Assert.Equal(1, connector.Calls);
        Assert.Equal(ContinuityPeerState.Reconnecting, session.Snapshot.State);
        retry.Release.Writer.TryWrite(true);
        Assert.Equal(2, await retry.Attempts.Reader.ReadAsync().AsTask().WaitAsync(Deadline));
        Assert.Equal(2, connector.Calls);
        cancellation.Cancel();
        await run.WaitAsync(Deadline);
        Assert.Equal(ContinuityPeerState.Stopped, session.Snapshot.State);
    }

    [Fact]
    public async Task RevocationDuringLateHandshakeCannotRestoreConnection()
    {
        var hello = new TaskCompletionSource<ContinuityHello>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new MockConnection(B, hello.Task);
        await using var session = new ContinuityPeerSession(A, B, new Connector(connection));
        var run = session.RunAsync();
        await connection.HandshakeStarted.Task.WaitAsync(Deadline);
        session.Revoke();
        var revision = session.Snapshot.Revision;
        hello.SetResult(new(1, 0, B, ContinuityFeatures.Files));
        await run.WaitAsync(Deadline);
        Assert.Equal(ContinuityPeerState.Revoked, session.Snapshot.State);
        Assert.Equal(revision, session.Snapshot.Revision);
        Assert.Equal(ContinuityFeatures.None, session.Snapshot.Features);
        Assert.False(connection.Connected.Task.IsCompleted);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task StopClearsFeaturesBeforeReturningAndPreventsLateReconnect()
    {
        var connection = new MockConnection(B, new ContinuityHello(1, 0, B, ContinuityFeatures.Files));
        await using var session = new ContinuityPeerSession(A, B, new Connector(connection));
        var run = session.RunAsync();
        await connection.Connected.Task.WaitAsync(Deadline);
        session.Stop();
        Assert.Equal(ContinuityPeerState.Stopped, session.Snapshot.State);
        Assert.Equal(ContinuityFeatures.None, session.Snapshot.Features);
        var revision = session.Snapshot.Revision;
        session.Stop();
        await run.WaitAsync(Deadline);
        Assert.Equal(revision, session.Snapshot.Revision);
        session.Revoke();
        Assert.Equal(ContinuityPeerState.Revoked, session.Snapshot.State);
    }

    [Fact]
    public async Task DisposeBeforeRunAndRepeatedDisposeAreSafe()
    {
        var session = new ContinuityPeerSession(A, B, new FailingConnector());
        await session.DisposeAsync();
        await session.DisposeAsync();
        session.Revoke();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.RunAsync());
        Assert.Equal(ContinuityPeerState.Stopped, session.Snapshot.State);
    }

    [Fact]
    public async Task WatchCoalescesAndCompletesWithRevokedState()
    {
        var retry = new ControlledRetry();
        await using var session = new ContinuityPeerSession(A, B, new FailingConnector(), retry);
        var run = session.RunAsync();
        await retry.Attempts.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
        session.Revoke();
        await run.WaitAsync(Deadline);
        var states = new List<ContinuityPeerSnapshot>();
        await foreach (var state in session.WatchAsync()) states.Add(state);
        Assert.Single(states);
        Assert.Equal(ContinuityPeerState.Revoked, states[0].State);
        Assert.True(states[0].Revision > 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync());
    }

    [Fact]
    public void RejectsNoncanonicalOrSelfIdentity()
    {
        Assert.Throws<ArgumentException>(() => new ContinuityPeerSession(A, A, new FailingConnector()));
        Assert.Throws<ArgumentException>(() => new ContinuityPeerSession("host.local", B, new FailingConnector()));
        Assert.Throws<ArgumentException>(() => new ContinuityPeerSession(A, B.ToUpperInvariant(), new FailingConnector()));
    }

    [Fact]
    public async Task UnknownFeaturesAreMaskedAndDisposalCancelsActiveConnection()
    {
        var connection = new MockConnection(B, new ContinuityHello(1, 99, B,
            ContinuityFeatures.Clipboard | (ContinuityFeatures)128));
        var session = new ContinuityPeerSession(A, B, new Connector(connection));
        var run = session.RunAsync();
        await connection.Connected.Task.WaitAsync(Deadline);
        Assert.Equal(ContinuityFeatures.Clipboard, session.Snapshot.Features);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync());
        await session.DisposeAsync().AsTask().WaitAsync(Deadline);
        await run.WaitAsync(Deadline);
        Assert.Equal(ContinuityPeerState.Stopped, session.Snapshot.State);
        Assert.Equal(ContinuityFeatures.None, session.Snapshot.Features);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task AlreadyCancelledRunDoesNotDial()
    {
        var connector = new FailingConnector();
        await using var session = new ContinuityPeerSession(A, B, connector);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await session.RunAsync(cancellation.Token).WaitAsync(Deadline);
        Assert.Equal(0, connector.Calls);
        Assert.Equal(ContinuityPeerState.Stopped, session.Snapshot.State);
    }

    private sealed class Connector(MockConnection connection) : IContinuityPeerConnector
    {
        public ValueTask<IContinuityPeerConnection> ConnectAsync(string trustedPeerId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IContinuityPeerConnection>(connection);
    }

    private sealed class FailingConnector : IContinuityPeerConnector
    {
        public int Calls;
        public ValueTask<IContinuityPeerConnection> ConnectAsync(string trustedPeerId, CancellationToken cancellationToken)
        {
            Calls++;
            throw new IOException("Synthetic network failure.");
        }
    }

    private sealed class ControlledRetry : IContinuityRetryDelay
    {
        public int Calls;
        public Channel<int> Attempts { get; } = Channel.CreateBounded<int>(1);
        public Channel<bool> Release { get; } = Channel.CreateBounded<bool>(1);
        public async ValueTask WaitAsync(int failureCount, CancellationToken cancellationToken)
        {
            Calls++;
            await Attempts.Writer.WriteAsync(failureCount, cancellationToken);
            await Release.Reader.ReadAsync(cancellationToken);
        }
    }

    // Test-only, bounded, in-memory transport. No TLS bypass exists in production.
    private sealed class MockConnection : IContinuityPeerConnection
    {
        private readonly ChannelReader<ContinuityHello>? _incoming;
        private readonly ChannelWriter<ContinuityHello>? _outgoing;
        private readonly Task<ContinuityHello>? _hello;
        public string AuthenticatedPeerId { get; }
        public bool Disposed { get; private set; }
        public TaskCompletionSource Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource HandshakeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MockConnection(string identity, ChannelReader<ContinuityHello> incoming, ChannelWriter<ContinuityHello> outgoing)
        { AuthenticatedPeerId = identity; _incoming = incoming; _outgoing = outgoing; }
        public MockConnection(string identity, ContinuityHello hello) : this(identity, Task.FromResult(hello)) { }
        public MockConnection(string identity, Task<ContinuityHello> hello)
        { AuthenticatedPeerId = identity; _hello = hello; }
        public async ValueTask<ContinuityHello> ExchangeHelloAsync(ContinuityHello local, CancellationToken cancellationToken)
        {
            HandshakeStarted.TrySetResult();
            if (_hello is not null) return await _hello; // Deliberately late callback in race test.
            await _outgoing!.WriteAsync(local, cancellationToken);
            return await _incoming!.ReadAsync(cancellationToken);
        }
        public async ValueTask WaitForDisconnectAsync(CancellationToken cancellationToken)
        {
            Connected.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
