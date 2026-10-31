using DaisyOS.Continuity.Protocol.V1;

namespace DaisyOS.Continuity;

public sealed partial class ContinuityDaemon
{
    internal bool CheckMutualTrust(PeerWire wire)
    {
        lock (_gate)
        {
            if (!_state.Settings.Peers.ContainsKey(wire.PeerId)) return false;
            if (wire.RemoteTrusted) return true;
            ForgetPeer(wire.PeerId);
            return false;
        }
    }

    private void ForgetPeer(string id)
    {
        lock (_gate)
        {
            _trustEpoch[id] = _trustEpoch.GetValueOrDefault(id) + 1;
            _revoked.Add(id);
            _state.Settings.Peers.Remove(id);
            _nearby.Remove(id);
            _recentLive.RemoveAll(l => l.Info.PeerId == id);
            _peerConnectionFailures.Remove(id);
            if (_peerOperations.TryGetValue(id, out var operations))
                foreach (var operation in operations.ToArray()) _ = operation.CancelAsync();
            _clipboard.ClearPeer(id);
            _pendingClipboard?.Peers.Remove(id);
            PrunePendingClipboard();
            foreach (var request in _prompts.Values.Where(p => p.Prompt.PeerId == id)) request.Decision.TrySetResult(false);
            _diagnostic = "";
            SaveSettings();
            UpdateClipboardState();
        }
    }

    private async Task ForgetRemoteAsync(string id)
    {
        string address;
        lock (_gate)
        {
            if (!_state.Settings.Peers.TryGetValue(id, out var peer)) return;
            address = _nearby.GetValueOrDefault(id)?.Address ?? peer.Address;
        }
        try
        {
            using var timeout = CreateSharingLifetime(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await using var wire = await DialAsync(address, id, false, timeout.Token);
            await wire.SendAsync(new() { Ping = new Ping { Sequence = ulong.MaxValue } }, timeout.Token);
            await wire.ReceiveAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or global::System.Net.Sockets.SocketException or global::System.Security.Authentication.AuthenticationException or TimeoutException) { }
        finally { ForgetPeer(id); } // Offline peers reconcile trust on their next authenticated connection.
    }

    private async Task<PeerWire> DialPeerAsync(string id, CancellationToken token)
    {
        var discovered = _discovery.AddressesFor(id);
        string[] addresses;
        lock (_gate)
        {
            Require(id, null);
            addresses = new[] { _state.Settings.Peers[id].Address, _nearby.GetValueOrDefault(id)?.Address }
                .Concat(discovered).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!).Distinct().Take(4).ToArray();
        }
        Exception? failure = null;
        foreach (var address in addresses)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                attempt.CancelAfter(TimeSpan.FromSeconds(3));
                var wire = await DialAsync(address, id, false, attempt.Token);
                try
                {
                    lock (_gate)
                    {
                        Require(id, null);
                        var peer = _state.Settings.Peers[id];
                        if (peer.Address != address) { _state.Settings.Peers[id] = peer with { Address = address }; SaveSettings(); }
                        if (_nearby.TryGetValue(id, out var nearby)) _nearby[id] = nearby with { Address = address };
                    }
                    return wire;
                }
                catch { await wire.DisposeAsync(); throw; }
            }
            catch (Exception ex) when (!token.IsCancellationRequested && ex is IOException or UnauthorizedAccessException or OperationCanceledException or global::System.Net.Sockets.SocketException or global::System.Security.Authentication.AuthenticationException or TimeoutException)
            { failure = ex is OperationCanceledException ? new TimeoutException("Trusted-device connection attempt timed out.", ex) : ex; }
        }
        if (failure is not null) global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        throw new IOException("No known address for the trusted device.");
    }

    private readonly Dictionary<string, string> _peerConnectionFailures = [];
    private readonly SemaphoreSlim _peerReconcile = new(1, 1);
    private async Task MaintainPeersAsync()
    {
        await ReconcilePeersAsync();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(_stop.Token)) await ReconcilePeersAsync();
    }
    internal async Task ReconcilePeersAsync()
    {
        if (!_peerReconcile.Wait(0)) return;
        try
        {
            string[] peers;
            lock (_gate)
            {
                // Clipboard helpers can exit across compositor changes or a failed
                // transfer. Reconcile them even when pairing and enablement stay unchanged.
                UpdateClipboardState();
                peers = Ready ? _state.Settings.Peers.Keys.ToArray() : [];
            }
            foreach (var id in peers)
            {
                try
                {
                    using var timeout = CreateSharingLifetime(_stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(12));
                    await using var wire = await DialPeerAsync(id, timeout.Token);
                    await wire.SendAsync(new() { Ping = new Ping { Sequence = 1, Position = GetInputPosition(id) } }, timeout.Token);
                    var pong = (await wire.ReceiveAsync(timeout.Token)).Pong;
                    if (pong?.Sequence != 1) continue;
                    ApplyInputPosition(id, pong.Position);
                    lock (_gate)
                    {
                        if (!Ready || !_state.Settings.Peers.TryGetValue(id, out var peer)) continue;
                        _peerConnectionFailures.Remove(id);
                        _nearby[id] = new(id, peer.Name, PeerAddress(id), "Trusted · verified", true, peer.Clipboard, peer.Files,
                            peer.Input, peer.Handoff, peer.Screen, peer.Audio, peer.InputEdge);
                        StartPendingClipboard(id);
                        if (!_live.Values.Any(l => l.Info.PeerId == id && l.Info.Kind == "input" && l.Info.Outgoing))
                        {
                            try { StartLive(id, "input", false, peer.InputEdge); }
                            catch (InvalidDataException) { } // A full live-session pool is retried on the next pass.
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or global::System.Net.Sockets.SocketException or global::System.Security.Authentication.AuthenticationException or TimeoutException)
                {
                    // Sleep, network changes, and forget are normal lifecycle events.
                    lock (_gate)
                    {
                        if (_nearby.TryGetValue(id, out var peer)) _nearby[id] = peer with { Status = "Trusted · unreachable" };
                        var failure = ex.GetType().Name + ": " + ex.Message;
                        if (_peerConnectionFailures.GetValueOrDefault(id) != failure)
                            Console.Error.WriteLine("[continuity] trusted_peer_retry: " + failure);
                        _peerConnectionFailures[id] = failure;
                    }
                }
            }
        }
        finally { _peerReconcile.Release(); }
    }
}
