using global::System.Security.Cryptography;
using global::System.Text;
using DaisyOS.Continuity.Protocol.V1;
using Google.Protobuf;

namespace DaisyOS.Continuity;
public sealed partial class ContinuityDaemon
{
    private readonly SemaphoreSlim _clipboardSlots = new(2, 2);
    private readonly Dictionary<string, DateTimeOffset> _clipboardUpdates = new();
    private void UpdateClipboardState()
    {
        if (Ready && _state.Settings.Peers.Values.Any(peer => peer.Clipboard)) _clipboard.Start();
        else _clipboard.Stop();
    }
    private string AddressFor(string id)
    {
        lock (_gate) return _nearby.GetValueOrDefault(id)?.Address ?? _state.Settings.Peers[id].Address;
    }
    public async Task PublishClipboardAsync(byte[] data)
    {
        if (data.Length is 0 or > 1024 * 1024 || _clipboard.IsEcho(data)) return;
        _ = new UTF8Encoding(false, true).GetCharCount(data);
        string[] peers;
        lock (_gate) { if (!Ready) return; peers = _state.Settings.Peers.Where(p => p.Value.Clipboard).Select(p => p.Key).ToArray(); }
        foreach (var id in peers)
        {
            if (!_clipboardSlots.Wait(0)) continue;
            var copy = data.ToArray();
            Track(async () =>
            {
                CancellationTokenSource? lifetime = null;
                try
                {
                    lifetime = PeerLifetime(id, _stop.Token);
                    lifetime.CancelAfter(TimeSpan.FromSeconds(10));
                    Require(id, "clipboard");
                    await using var wire = await DialAsync(AddressFor(id), id, false, lifetime.Token);
                    var update = ByteString.CopyFrom(Guid.NewGuid().ToByteArray());
                    await wire.SendAsync(new() { ClipboardOffer = new ClipboardOffer { UpdateId = update, OriginPeerId = ByteString.CopyFrom(Convert.FromHexString(_state.Id)),
                        MimeTypes = { "text/plain;charset=utf-8" }, Size = (ulong)copy.Length, Sha256 = ByteString.CopyFrom(SHA256.HashData(copy)) } }, lifetime.Token);
                    var request = await wire.ReceiveAsync(lifetime.Token);
                    if (request.ClipboardGet?.UpdateId != update || request.ClipboardGet.MimeType != "text/plain;charset=utf-8") throw new InvalidDataException();
                    Require(id, "clipboard");
                    await wire.SendAsync(new() { ClipboardData = new ClipboardData { UpdateId = update, MimeType = "text/plain;charset=utf-8", Data = UnsafeByteOperations.UnsafeWrap(copy) } }, lifetime.Token);
                    lock (_gate) _diagnostic = "";
                }
                finally { CryptographicOperations.ZeroMemory(copy); if (lifetime is not null) EndPeer(id, lifetime); _clipboardSlots.Release(); }
            });
        }
        await Task.CompletedTask;
    }
    private async Task ReceiveClipboardAsync(PeerWire wire, ClipboardOffer offer, CancellationToken token)
    {
        Require(wire.PeerId, "clipboard");
        if (offer.UpdateId.Length != 16 || offer.OriginPeerId != ByteString.CopyFrom(Convert.FromHexString(wire.PeerId)) || offer.Size is 0 or > 1024 * 1024 ||
            offer.Sha256.Length != 32 || offer.MimeTypes.Count != 1 || offer.MimeTypes[0] != "text/plain;charset=utf-8") throw new InvalidDataException();
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var expired in _clipboardUpdates.Where(p => now - p.Value > TimeSpan.FromMinutes(5)).Select(p => p.Key).ToArray()) _clipboardUpdates.Remove(expired);
            var key = wire.PeerId + ":" + Convert.ToHexStringLower(offer.UpdateId.Span);
            if (_clipboardUpdates.ContainsKey(key) || _clipboardUpdates.Count >= 256) throw new InvalidDataException("Duplicate update or clipboard quota.");
            _clipboardUpdates[key] = now;
        }
        await wire.SendAsync(new() { ClipboardGet = new ClipboardGet { UpdateId = offer.UpdateId, MimeType = "text/plain;charset=utf-8" } }, token);
        var message = await wire.ReceiveAsync(token).WaitAsync(TimeSpan.FromSeconds(10), token);
        var payload = message.ClipboardData ?? throw new InvalidDataException();
        if (payload.UpdateId != offer.UpdateId || payload.MimeType != "text/plain;charset=utf-8" || (ulong)payload.Data.Length != offer.Size ||
            !SHA256.HashData(payload.Data.Span).AsSpan().SequenceEqual(offer.Sha256.Span)) throw new InvalidDataException();
        var data = payload.Data.ToByteArray();
        try { Require(wire.PeerId, "clipboard"); token.ThrowIfCancellationRequested(); await _clipboard.SetAsync(data, wire.PeerId, token); }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
            if (global::System.Runtime.InteropServices.MemoryMarshal.TryGetArray(payload.Data.Memory, out var memory))
                CryptographicOperations.ZeroMemory(memory.AsSpan());
        }
    }
}
