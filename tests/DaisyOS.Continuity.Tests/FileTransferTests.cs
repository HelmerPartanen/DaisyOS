using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DaisyOS.Continuity;
using DaisyOS.Continuity.Protocol.V1;
using Google.Protobuf;

namespace DaisyOS.Continuity.Tests;

public sealed class FileTransferTests
{
    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, false, true)]
    public async Task AuthenticatedTransferResumesApprovedManifestAndRejectsCorruption(bool corrupt, bool permitted, bool lockBeforeCommit, bool staleApproval)
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-continuity-transfer-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var sender = new SecureState(Path.Combine(root, "sender"));
            using var receiver = new SecureState(Path.Combine(root, "receiver"));
            receiver.Settings.Enabled = true;
            receiver.Settings.Peers[sender.Id] = new(Convert.ToBase64String(sender.Identity.RawData), "Sender", "127.0.0.1:47621", Files: permitted, GrantId: "current-grant");
            var downloads = Path.Combine(root, "downloads");
            var active = true;
            await using var daemon = new ContinuityDaemon(receiver, 0, () => Volatile.Read(ref active), downloads, "unix:path=/nonexistent-continuity-test-bus");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using var client = new TcpClient();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
            using var accepted = await listener.AcceptTcpClientAsync(timeout.Token);
            var dial = PeerWire.OpenAsync(client, sender, false, false, receiver.Id, (_, _) => true, timeout.Token);
            var accept = PeerWire.OpenAsync(accepted, receiver, true, false, sender.Id, (_, _) => true, timeout.Token);
            await using var outgoing = await dial;
            await using var incoming = await accept;
            if (!permitted)
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => daemon.ReceiveFileAsync(incoming,
                    new FileOffer { TransferId = ByteString.CopyFrom(Guid.NewGuid().ToByteArray()), DisplayName = "forbidden.txt", Length = 1,
                        Sha256 = ByteString.CopyFrom(new byte[32]) }, timeout.Token));
                Assert.False(Directory.Exists(downloads));
                Assert.Empty(daemon.Snapshot().Transfers);
                return;
            }
            var data = RandomNumberGenerator.GetBytes(200_000);
            var hash = SHA256.HashData(data);
            const string name = "resumable-🌼.bin";
            var digest = Convert.ToHexStringLower(hash);
            var key = SecureState.Fingerprint(Encoding.UTF8.GetBytes($"{sender.Id}:{name}:{data.Length}:{digest}"));
            var staging = Path.Combine(receiver.DirectoryPath, "transfers");
            Directory.CreateDirectory(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var part = Path.Combine(staging, key + ".part");
            var record = Path.Combine(staging, key + ".json");
            SecureState.WritePrivate(part, data[..1000]);
            SecureState.WritePrivate(record, JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, Peer = sender.Id, Name = name,
                Length = data.Length, Digest = digest, ApprovedUntil = DateTimeOffset.UtcNow.AddMinutes(5), GrantId = staleApproval ? "prior-revoked-grant" : receiver.Settings.Peers[sender.Id].GrantId }));
            var id = ByteString.CopyFrom(Guid.NewGuid().ToByteArray());
            var offer = new FileOffer { TransferId = id, DisplayName = name, Length = (ulong)data.Length, Sha256 = ByteString.CopyFrom(hash) };
            var transfer = daemon.ReceiveFileAsync(incoming, offer, timeout.Token);
            Assert.Empty(daemon.Snapshot().Prompts);
            var decision = (await outgoing.ReceiveAsync(timeout.Token)).FileDecision;
            Assert.True(decision.Accepted);
            Assert.Equal(1000UL, decision.ResumeOffset);
            if (corrupt) data[^1] ^= 1;
            for (var offset = 1000; offset < data.Length;)
            {
                var count = Math.Min(65536, data.Length - offset);
                await outgoing.SendAsync(new() { FileChunk = new FileChunk { TransferId = id, Offset = (ulong)offset, Data = ByteString.CopyFrom(data, offset, count) } }, timeout.Token);
                offset += count;
            }
            if (lockBeforeCommit)
            {
                while (daemon.Snapshot().Transfers.Single().Completed != data.Length)
                    await Task.Delay(5, timeout.Token);
                Volatile.Write(ref active, false);
            }
            await outgoing.SendAsync(new() { FileComplete = new FileComplete { TransferId = id, VerifiedSha256 = ByteString.CopyFrom(hash) } }, timeout.Token);
            var destination = Path.Combine(downloads, "Continuity", name);
            if (lockBeforeCommit)
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => transfer);
                Assert.False(File.Exists(destination));
                Assert.False(File.Exists(record));
            }
            else if (corrupt)
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => transfer);
                Assert.False(File.Exists(destination));
                Assert.False(File.Exists(part)); Assert.False(File.Exists(record));
            }
            else
            {
                var complete = (await outgoing.ReceiveAsync(timeout.Token)).FileComplete;
                Assert.Equal(ByteString.CopyFrom(hash), complete.VerifiedSha256);
                await transfer;
                Assert.Equal(data, await File.ReadAllBytesAsync(destination, timeout.Token));
                Assert.False(File.Exists(part)); Assert.False(File.Exists(record));
                Assert.Equal("Complete", Assert.Single(daemon.Snapshot().Transfers).Status);
                // Simulate retry after a lost completion ACK. The receipt must
                // acknowledge identical verified bytes without creating a duplicate.
                var retried = offer.Clone(); retried.TransferId = ByteString.CopyFrom(Guid.NewGuid().ToByteArray());
                var retry = daemon.ReceiveFileAsync(incoming, retried, timeout.Token);
                var resumed = (await outgoing.ReceiveAsync(timeout.Token)).FileDecision;
                Assert.Equal((ulong)data.Length, resumed.ResumeOffset);
                await outgoing.SendAsync(new() { FileComplete = new FileComplete { TransferId = retried.TransferId, VerifiedSha256 = retried.Sha256 } }, timeout.Token);
                Assert.Equal(retried.TransferId, (await outgoing.ReceiveAsync(timeout.Token)).FileComplete.TransferId);
                await retry;
                Assert.Single(Directory.GetFiles(Path.Combine(downloads, "Continuity")));

            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
