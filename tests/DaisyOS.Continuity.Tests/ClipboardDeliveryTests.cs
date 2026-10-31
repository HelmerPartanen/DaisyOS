using System.Net;
using System.Net.Sockets;
using System.Text;
using DaisyOS.Continuity;
using DaisyOS.Continuity.Protocol.V1;

namespace DaisyOS.Continuity.Tests;

public sealed class ClipboardDeliveryTests
{
    [Fact]
    public async Task LatestCopyArrivesWhenTheTrustedComputerComesOnline()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-clipboard-online-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var source = new SecureState(Path.Combine(root, "source"));
            using var target = new SecureState(Path.Combine(root, "target"));
            using var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
            Pair(source, target, port);
            await using var sender = Host(source, root);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await sender.PublishClipboardAsync(Encoding.UTF8.GetBytes("copied while offline"));
            while (!sender.Snapshot().Diagnostic.Contains("Device unreachable")) await Task.Delay(10, timeout.Token);
            Assert.Equal(1, sender.PendingClipboardPeerCount);
            await sender.PublishClipboardAsync(Encoding.UTF8.GetBytes("latest text 🌼"));
            using var listener = new TcpListener(IPAddress.Loopback, port); listener.Start();
            var receive = Task.Run(async () =>
            {
                while (true)
                {
                    using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
                    await using var wire = await PeerWire.OpenAsync(socket, target, true, false, source.Id, (_, _) => true, timeout.Token);
                    var message = await wire.ReceiveAsync(timeout.Token);
                    if (message.Ping is { } ping) await wire.SendAsync(new() { Pong = new() { Sequence = ping.Sequence } }, timeout.Token);
                    else if (message.LiveStart is not null) await wire.SendAsync(new() { LiveDecision = new() { Accepted = false } }, timeout.Token);
                    else if (message.ClipboardOffer is { } offer) return await ReadClipboard(wire, offer, timeout.Token);
                }
            });
            await sender.ReconcilePeersAsync();
            Assert.Equal("latest text 🌼", await receive);
            while (sender.PendingClipboardPeerCount != 0) await Task.Delay(10, timeout.Token);
            Assert.Empty(sender.Snapshot().Prompts);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RapidCopiesDuringAnActiveTransferKeepTheNewestSelection()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-clipboard-busy-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var source = new SecureState(Path.Combine(root, "source"));
            using var target = new SecureState(Path.Combine(root, "target"));
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            Pair(source, target, ((IPEndPoint)listener.LocalEndpoint).Port);
            await using var sender = Host(source, root);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var offered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var receive = Task.Run(async () =>
            {
                var copies = new List<string>();
                for (var i = 0; i < 2; i++)
                {
                    using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
                    await using var wire = await PeerWire.OpenAsync(socket, target, true, false, source.Id, (_, _) => true, timeout.Token);
                    var offer = (await wire.ReceiveAsync(timeout.Token)).ClipboardOffer;
                    if (i == 0) { offered.SetResult(); await release.Task.WaitAsync(timeout.Token); }
                    copies.Add(await ReadClipboard(wire, offer, timeout.Token));
                }
                return copies;
            });
            await sender.PublishClipboardAsync(Encoding.UTF8.GetBytes("first"));
            await offered.Task.WaitAsync(timeout.Token);
            for (var i = 0; i < 100; i++) await sender.PublishClipboardAsync(Encoding.UTF8.GetBytes("copy " + i));
            release.SetResult();
            Assert.Equal(new[] { "first", "copy 99" }, await receive);
            while (sender.PendingClipboardPeerCount != 0) await Task.Delay(10, timeout.Token);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("forget")]
    [InlineData("clipboard-clear")]
    public async Task PendingTextIsDiscardedWhenSharingStopsOrTheClipboardClears(string action)
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-clipboard-clear-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var source = new SecureState(Path.Combine(root, "source"));
            using var target = new SecureState(Path.Combine(root, "target"));
            Pair(source, target, 1);
            await using var sender = Host(source, root);
            await sender.PublishClipboardAsync(Encoding.UTF8.GetBytes("temporary pending text"));
            await sender.ExecuteAsync(new(action, target.Id, Allowed: true), "fixture");
            Assert.Equal(0, sender.PendingClipboardPeerCount);
        }
        finally { Directory.Delete(root, true); }
    }

    private static ContinuityDaemon Host(SecureState source, string root) =>
        new(source, 47621, () => true, root, "unix:path=/nonexistent-clipboard-delivery", clipboard: new ClipboardBridge { ProcessStarter = _ => null });
    private static void Pair(SecureState source, SecureState target, int port)
    {
        source.Settings.Enabled = target.Settings.Enabled = true;
        source.Settings.Peers[target.Id] = new(Convert.ToBase64String(target.Identity.RawData), "Target", "127.0.0.1:" + port, Clipboard: true);
        target.Settings.Peers[source.Id] = new(Convert.ToBase64String(source.Identity.RawData), "Source", "127.0.0.1:1", Clipboard: true);
    }
    private static async Task<string> ReadClipboard(PeerWire wire, ClipboardOffer offer, CancellationToken token)
    {
        await wire.SendAsync(new() { ClipboardGet = new() { UpdateId = offer.UpdateId, MimeType = "text/plain;charset=utf-8" } }, token);
        var data = (await wire.ReceiveAsync(token)).ClipboardData;
        Assert.Equal(offer.UpdateId, data.UpdateId);
        return Encoding.UTF8.GetString(data.Data.Span);
    }
}
