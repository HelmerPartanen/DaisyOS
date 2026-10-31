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
            if (_peerOperations.TryGetValue(id, out var operations))
                foreach (var operation in operations.ToArray()) _ = operation.CancelAsync();
            _clipboard.ClearPeer(id);
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
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    await using var wire = await DialAsync(PeerAddress(id), id, false, timeout.Token);
                    await wire.SendAsync(new() { Ping = new Ping { Sequence = 1 } }, timeout.Token);
                    if ((await wire.ReceiveAsync(timeout.Token)).Pong?.Sequence != 1) continue;
                    lock (_gate)
                    {
                        if (!Ready || !_state.Settings.Peers.TryGetValue(id, out var peer)) continue;
                        _nearby[id] = new(id, peer.Name, PeerAddress(id), "Trusted · verified", true, peer.Clipboard, peer.Files,
                            peer.Input, peer.Handoff, peer.Screen, peer.Audio, peer.InputEdge);
                        if (!_live.Values.Any(l => l.Info.PeerId == id && l.Info.Kind == "input" && l.Info.Outgoing))
                            StartLive(id, "input", false, peer.InputEdge);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or global::System.Net.Sockets.SocketException or global::System.Security.Authentication.AuthenticationException or TimeoutException)
                {
                    // Sleep, network changes, and forget are normal lifecycle events.
                    lock (_gate) if (_nearby.TryGetValue(id, out var peer)) _nearby[id] = peer with { Status = "Trusted · unreachable" };
                }
            }
        }
        finally { _peerReconcile.Release(); }
    }
}
