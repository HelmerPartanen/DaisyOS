using global::System.Security.Cryptography;
using global::System.Text;
using global::System.Text.Json;
using DaisyOS.Core.Continuity;
using DaisyOS.Continuity.Protocol.V1;
using Google.Protobuf;

namespace DaisyOS.Continuity;
public sealed partial class ContinuityDaemon
{
    private const long MaxFile = 100L * 1024 * 1024 * 1024;
    private readonly Dictionary<string, CancellationTokenSource> _transferCancellation = new();
    private readonly HashSet<string> _receivingManifests = new();
    private readonly Dictionary<string, string> _transferResumeRecords = new();
    private sealed record CompletedReceipt(int Version, string Peer, string Name, long Length, string Digest, string SavedName, DateTimeOffset Expires, string GrantId);
    private sealed record ResumeRecord(int Version, string Peer, string Name, long Length, string Digest, DateTimeOffset ApprovedUntil, string GrantId);

    public static bool IsSafeFileName(string name) => name.Length > 0 && name is not "." and not ".." &&
        Encoding.UTF8.GetByteCount(name) <= 255 && !name.Any(c => char.IsControl(c) || c is '/' or '\\' or '\0' or '\u202e' or '\u202d') &&
        global::System.IO.Path.GetFileName(name) == name;
    private string CurrentGrant(string peer) { lock (_gate) return _state.Settings.Peers[peer].GrantId; }
    private static string TruncateFileName(string name)
    {
        var result = new StringBuilder(); var size = 0;
        foreach (var rune in name.EnumerateRunes())
        {
            if (size + rune.Utf8SequenceLength > 240) break;
            result.Append(rune.ToString()); size += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }
    private void Progress(string id, string peer, string name, long length, long done, string status)
    {
        lock (_gate)
        {
            if (_transfers.Count >= 64 && !_transfers.ContainsKey(id))
            {
                var completed = _transfers.FirstOrDefault(t => t.Value.Status is "Complete" or "Failed" or "Cancelled");
                if (completed.Key is not null) _transfers.Remove(completed.Key);
                else throw new IOException("Transfer limit.");
            }
            _transfers[id] = new(id, peer, name, length, done, status);
        }
    }
    private async Task SendFileAsync(string peer, string path, CancellationToken token)
    {
        if (!global::System.IO.Path.IsPathFullyQualified(path) || !SecureState.IsRegularFile(path)) throw new InvalidDataException("Choose one regular file.");
        var name = global::System.IO.Path.GetFileName(path);
        if (!IsSafeFileName(name)) throw new InvalidDataException("Unsafe filename.");
        var id = Guid.NewGuid().ToString("N");
        using var lifetime = PeerLifetime(peer, token);
        lock (_gate) _transferCancellation[id] = lifetime;
        long length = 0, done = 0;
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            length = file.Length;
            if (length > MaxFile) throw new IOException("File limit exceeded.");
            Progress(id, peer, name, length, 0, "Hashing");
            var digest = await SHA256.HashDataAsync(file, lifetime.Token);
            var transferId = ByteString.CopyFrom(Convert.FromHexString(id));
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    Require(peer, "files");
                    await using var wire = await DialPeerAsync(peer, lifetime.Token);
                    await wire.SendAsync(new() { FileOffer = new FileOffer { TransferId = transferId, DisplayName = name,
                        Length = (ulong)length, Sha256 = ByteString.CopyFrom(digest) } }, lifetime.Token);
                    Progress(id, peer, name, length, done, "Awaiting receiver");
                    var response = await wire.ReceiveAsync(lifetime.Token).WaitAsync(TimeSpan.FromMinutes(3), lifetime.Token);
                    var decision = response.FileDecision ?? throw new InvalidDataException();
                    if (decision.TransferId != transferId || !decision.Accepted) throw new OperationCanceledException("Receiver declined.");
                    if (decision.ResumeOffset > (ulong)length) throw new InvalidDataException();
                    done = (long)decision.ResumeOffset; file.Position = done;
                    var buffer = new byte[65536];
                    try
                    {
                        while (done < length)
                        {
                            Require(peer, "files");
                            var count = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - done)), lifetime.Token);
                            if (count == 0) throw new IOException("Source file changed.");
                            await wire.SendAsync(new() { FileChunk = new FileChunk { TransferId = transferId, Offset = (ulong)done,
                                Data = ByteString.CopyFrom(buffer, 0, count) } }, lifetime.Token);
                            done += count; Progress(id, peer, name, length, done, "Sending");
                        }
                    }
                    finally { CryptographicOperations.ZeroMemory(buffer); }
                    await wire.SendAsync(new() { FileComplete = new FileComplete { TransferId = transferId, VerifiedSha256 = ByteString.CopyFrom(digest) } }, lifetime.Token);
                    var complete = await wire.ReceiveAsync(lifetime.Token).WaitAsync(TimeSpan.FromHours(2), lifetime.Token);
                    if (complete.FileComplete?.TransferId != transferId || complete.FileComplete.VerifiedSha256 != ByteString.CopyFrom(digest)) throw new InvalidDataException();
                    Progress(id, peer, name, length, length, "Complete"); return;
                }
                catch (Exception ex) when ((ex is IOException or global::System.Net.Sockets.SocketException or TimeoutException) && attempt < 5 && !lifetime.IsCancellationRequested)
                {
                    Progress(id, peer, name, length, done, "Reconnecting");
                    await new ContinuityRetryDelay().WaitAsync(attempt + 1, lifetime.Token);
                }
            }
        }
        catch (Exception)
        {
            Progress(id, peer, name, length, done, lifetime.IsCancellationRequested ? "Cancelled" : "Failed");
            throw;
        }
        finally { lock (_gate) _transferCancellation.Remove(id); EndPeer(peer, lifetime); }
    }
    internal async Task ReceiveFileAsync(PeerWire wire, FileOffer offer, CancellationToken token)
    {
        Require(wire.PeerId, "files");
        if (!IsSafeFileName(offer.DisplayName) || offer.Length > MaxFile || offer.Sha256.Length != 32 || offer.TransferId.Length != 16) throw new InvalidDataException();
        var id = Convert.ToHexStringLower(offer.TransferId.Span);
        var manifest = new ResumeRecord(1, wire.PeerId, offer.DisplayName, (long)offer.Length,
            Convert.ToHexStringLower(offer.Sha256.Span), DateTimeOffset.UtcNow.AddDays(1), CurrentGrant(wire.PeerId));
        var key = SecureState.Fingerprint(Encoding.UTF8.GetBytes($"{manifest.Peer}:{manifest.Name}:{manifest.Length}:{manifest.Digest}"));
        var staging = global::System.IO.Path.Combine(_state.DirectoryPath, "transfers");
        Directory.CreateDirectory(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        SecureState.Check(staging, true);
        var part = global::System.IO.Path.Combine(staging, key + ".part");
        var recordPath = global::System.IO.Path.Combine(staging, key + ".json");
        var receiptPath = global::System.IO.Path.Combine(staging, key + ".complete.json");
        lock (_gate) if (_receivingManifests.Count >= 2 || !_receivingManifests.Add(key)) throw new IOException("Transfer already active.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (_gate) { _transferCancellation[id] = lifetime; _transferResumeRecords[id] = recordPath; }
        long done = 0;
        try
        {
            var destinationDirectory = global::System.IO.Path.Combine(_downloadsDirectory, "Continuity");
            if (File.Exists(receiptPath))
            {
                SecureState.Check(receiptPath, false);
                if (new FileInfo(receiptPath).Length > 8192) throw new InvalidDataException();
                var receipt = JsonSerializer.Deserialize<CompletedReceipt>(await File.ReadAllTextAsync(receiptPath, lifetime.Token));
                if (receipt is not null && receipt.Version == 1 && receipt.Peer == manifest.Peer && receipt.Name == manifest.Name &&
                    receipt.Length == manifest.Length && receipt.Digest == manifest.Digest && receipt.GrantId == manifest.GrantId && receipt.Expires > DateTimeOffset.UtcNow && IsSafeFileName(receipt.SavedName))
                {
                    var saved = global::System.IO.Path.Combine(destinationDirectory, receipt.SavedName);
                    if (File.Exists(saved))
                    {
                        SecureState.Check(destinationDirectory, true); SecureState.Check(saved, false);
                        await using var completed = File.OpenRead(saved);
                        if (completed.Length == manifest.Length && (await SHA256.HashDataAsync(completed, lifetime.Token)).AsSpan().SequenceEqual(offer.Sha256.Span))
                        {
                            Require(wire.PeerId, "files");
                            await wire.SendAsync(new() { FileDecision = new FileDecision { TransferId = offer.TransferId, Accepted = true, ResumeOffset = offer.Length } }, lifetime.Token);
                            var end = await wire.ReceiveAsync(lifetime.Token).WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
                            if (end.FileComplete?.TransferId != offer.TransferId || end.FileComplete.VerifiedSha256 != offer.Sha256) throw new InvalidDataException();
                            Require(wire.PeerId, "files");
                            await wire.SendAsync(new() { FileComplete = new FileComplete { TransferId = offer.TransferId, VerifiedSha256 = offer.Sha256 } }, lifetime.Token);
                            Progress(id, wire.PeerId, offer.DisplayName, manifest.Length, manifest.Length, "Complete");
                            File.Delete(part); File.Delete(recordPath); return;
                        }
                    }
                }
            }
            Require(wire.PeerId, "files");
            lifetime.Token.ThrowIfCancellationRequested();
            SecureState.WritePrivate(recordPath, JsonSerializer.SerializeToUtf8Bytes(manifest));
            if (File.Exists(part)) SecureState.Check(part, false);
            await using (var output = new FileStream(part, new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite,
                Share = FileShare.None, Options = FileOptions.Asynchronous, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
            {
                if (output.Length > manifest.Length) output.SetLength(0);
                done = output.Length; output.Position = done;
                if (new DriveInfo(staging).AvailableFreeSpace < manifest.Length - done + 16 * 1024 * 1024) throw new IOException("Insufficient disk space.");
                await wire.SendAsync(new() { FileDecision = new FileDecision { TransferId = offer.TransferId, Accepted = true, ResumeOffset = (ulong)done } }, lifetime.Token);
                Progress(id, wire.PeerId, offer.DisplayName, manifest.Length, done, "Receiving");
                while (true)
                {
                    var envelope = await wire.ReceiveAsync(lifetime.Token).WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
                    Require(wire.PeerId, "files");
                    if (envelope.FileChunk is { } chunk)
                    {
                        if (chunk.TransferId != offer.TransferId || chunk.Offset != (ulong)done || chunk.Data.Length is 0 or > 65536 || chunk.Data.Length > manifest.Length - done) throw new InvalidDataException();
                        await output.WriteAsync(chunk.Data.Memory, lifetime.Token); done += chunk.Data.Length;
                        Progress(id, wire.PeerId, offer.DisplayName, manifest.Length, done, "Receiving");
                    }
                    else if (envelope.FileComplete is { } complete && complete.TransferId == offer.TransferId && complete.VerifiedSha256 == offer.Sha256 && done == manifest.Length) break;
                    else throw new InvalidDataException();
                }
                await output.FlushAsync(lifetime.Token); output.Flush(true); output.Position = 0;
                var actual = await SHA256.HashDataAsync(output, lifetime.Token);
                if (!actual.AsSpan().SequenceEqual(offer.Sha256.Span)) throw new InvalidDataException("Checksum mismatch.");
            }
            Require(wire.PeerId, "files"); lifetime.Token.ThrowIfCancellationRequested();
            var destination = destinationDirectory;
            Directory.CreateDirectory(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            SecureState.Check(destination, true);
            // Copy into the destination filesystem, then atomically publish there.
            var temporary = global::System.IO.Path.Combine(destination, "." + Guid.NewGuid().ToString("N") + ".part");
            try
            {
                await using (var input = File.OpenRead(part))
                await using (var output = new FileStream(temporary, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
                    Options = FileOptions.Asynchronous, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
                { await input.CopyToAsync(output, 65536, lifetime.Token); await output.FlushAsync(lifetime.Token); output.Flush(true); }
                Require(wire.PeerId, "files"); lifetime.Token.ThrowIfCancellationRequested();
                var final = global::System.IO.Path.Combine(destination, offer.DisplayName);
                if (File.Exists(final) || Directory.Exists(final)) final = global::System.IO.Path.Combine(destination, id[..8] + "-" + TruncateFileName(offer.DisplayName));
                // Record the planned basename first. Recovery acknowledges it only
                // after verifying the destination bytes; lost ACK cannot duplicate it.
                SecureState.WritePrivate(receiptPath, JsonSerializer.SerializeToUtf8Bytes(new CompletedReceipt(1, manifest.Peer,
                    manifest.Name, manifest.Length, manifest.Digest, global::System.IO.Path.GetFileName(final), DateTimeOffset.UtcNow.AddDays(1), manifest.GrantId)));
                lock (_gate) { Require(wire.PeerId, "files"); lifetime.Token.ThrowIfCancellationRequested(); File.Move(temporary, final, false); SecureState.SyncDirectory(destination); }
            }
            finally { File.Delete(temporary); }
            File.Delete(part); File.Delete(recordPath);
            Progress(id, wire.PeerId, offer.DisplayName, manifest.Length, done, "Complete");
            await wire.SendAsync(new() { FileComplete = new FileComplete { TransferId = offer.TransferId, VerifiedSha256 = offer.Sha256 } }, lifetime.Token);
            await NotifyAsync("File received", offer.DisplayName + " · Downloads/Continuity", true);
        }
        catch (Exception ex)
        {
            if (ex is InvalidDataException) File.Delete(part);
            if (ex is InvalidDataException or OperationCanceledException or UnauthorizedAccessException) File.Delete(recordPath);
            Progress(id, wire.PeerId, offer.DisplayName, manifest.Length, done, lifetime.IsCancellationRequested ? "Cancelled" : "Failed");
            throw;
        }
        finally { lock (_gate) { _receivingManifests.Remove(key); _transferCancellation.Remove(id); _transferResumeRecords.Remove(id); } }
    }
}
