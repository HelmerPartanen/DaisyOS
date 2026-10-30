using System.Net;
using System.Net.Sockets;
using DaisyOS.Continuity;

namespace DaisyOS.Continuity.Tests;

public sealed class AudioRouteTests
{
    [Fact]
    public async Task AuthenticatedAudioReachesPlaybackAndRevokeRestoresLocalOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-audio-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourceTools = CreateAudioTools(Path.Combine(root, "source-tools"));
            var targetTools = CreateAudioTools(Path.Combine(root, "target-tools"));
            using var source = new SecureState(Path.Combine(root, "source"));
            using var target = new SecureState(Path.Combine(root, "target"));
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var address = "127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            source.Settings.Enabled = target.Settings.Enabled = true;
            source.Settings.Peers[target.Id] = new(Convert.ToBase64String(target.Identity.RawData), "Target", address, Audio: true);
            target.Settings.Peers[source.Id] = new(Convert.ToBase64String(source.Identity.RawData), "Source", "127.0.0.1:1", Audio: false);
            await using var sender = new ContinuityDaemon(source, 47621, () => true, root, "unix:path=/nonexistent-audio-test", sourceTools);
            await using var receiver = new ContinuityDaemon(target, 47621, () => true, root, "unix:path=/nonexistent-audio-test", targetTools);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var receive = Task.Run(async () =>
            {
                using var incoming = await listener.AcceptTcpClientAsync(deadline.Token);
                await using var wire = await PeerWire.OpenAsync(incoming, target, true, false, source.Id, (_, _) => true, deadline.Token);
                var request = await wire.ReceiveAsync(deadline.Token);
                try { await receiver.ReceiveLiveAsync(wire, request.LiveStart, deadline.Token); }
                catch (Exception e) when (e is IOException or OperationCanceledException) { }
            });
            await sender.ExecuteAsync(new("live", target.Id, "audio"), "fixture-local-ui");
            Assert.Empty(receiver.Snapshot().Prompts);
            var pcm = Path.Combine(targetTools, "received.pcm");
            while (!File.Exists(pcm) || new FileInfo(pcm).Length < 7680) await Task.Delay(10, deadline.Token);
            Assert.All(File.ReadAllBytes(pcm), value => Assert.Equal((byte)42, value));
            await sender.ExecuteAsync(new("forget", target.Id), "fixture-local-ui");
            while (sender.Snapshot().Sessions!.Length != 0) await Task.Delay(10, deadline.Token);
            await receive.WaitAsync(deadline.Token);
            Assert.Empty(receiver.Snapshot().Sessions!);
            Assert.Equal("local.speakers", File.ReadAllText(Path.Combine(sourceTools, "default")));
            var commands = File.ReadAllText(Path.Combine(sourceTools, "commands"));
            Assert.Contains("set-default-sink daisy_continuity_", commands);
            Assert.Contains("move-sink-input 4 local.speakers", commands);
            Assert.Contains("unload-module 9", commands);
            Assert.False(source.Settings.Peers.ContainsKey(target.Id));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task CrashRecoveryRemovesOnlyTheRecordedSinkAndRestoresPlayback()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-audio-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tools = CreateAudioTools(Path.Combine(root, "tools"));
            using var state = new SecureState(Path.Combine(root, "state"));
            var sink = "daisy_continuity_" + Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(tools, "sink"), sink);
            File.WriteAllText(Path.Combine(tools, "default"), sink);
            File.WriteAllText(Path.Combine(tools, "stream"), "27");
            var recovery = Path.Combine(state.DirectoryPath, "audio-route");
            SecureState.WritePrivate(recovery, global::System.Text.Encoding.UTF8.GetBytes(sink + "\nlocal.speakers"));
            await using var daemon = new ContinuityDaemon(state, 47621, () => true, root, "unix:path=/nonexistent-recovery-test", tools);
            await daemon.RecoverAudioRouteAsync();
            Assert.False(File.Exists(recovery));
            Assert.Equal("local.speakers", File.ReadAllText(Path.Combine(tools, "default")));
            Assert.Equal("12", File.ReadAllText(Path.Combine(tools, "stream")));
            Assert.Contains("unload-module 9", File.ReadAllText(Path.Combine(tools, "commands")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task AudioCannotBeSentAndReceivedAtTheSameTimeEvenWhileConsentIsPending()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-audio-loop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var state = new SecureState(Path.Combine(root, "host"));
            using var peer = new SecureState(Path.Combine(root, "peer"));
            using var blocker = new TcpListener(IPAddress.Loopback, 0); blocker.Start();
            state.Settings.Enabled = true;
            state.Settings.Peers[peer.Id] = new(Convert.ToBase64String(peer.Identity.RawData), "Peer",
                "127.0.0.1:" + ((IPEndPoint)blocker.LocalEndpoint).Port, Audio: true);
            await using var daemon = new ContinuityDaemon(state, 47621, () => true, root, "unix:path=/nonexistent-loop-test");
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var incoming = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
                await using var wire = await PeerWire.OpenAsync(socket, state, true, false, peer.Id, (_, _) => true, deadline.Token);
                await daemon.ExecuteAsync(new("live", peer.Id, "audio"), "local-ui");
                Assert.Single(daemon.Snapshot().Sessions!);
                await Assert.ThrowsAsync<InvalidDataException>(() => daemon.ReceiveLiveAsync(wire,
                    new() { SessionId = Guid.NewGuid().ToString("N"), Kind = "audio" }, deadline.Token));
                Assert.Empty(daemon.Snapshot().Prompts);
            });
            using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            await using var remote = await PeerWire.OpenAsync(client, peer, false, false, state.Id, (_, _) => true, deadline.Token);
            await incoming;
        }
        finally { Directory.Delete(root, true); }
    }
    private static string CreateAudioTools(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "default"), "local.speakers");
        File.WriteAllText(Path.Combine(directory, "stream"), "12");
        File.WriteAllText(Path.Combine(directory, "pactl"), """
            #!/usr/bin/python3
            import pathlib, sys
            root = pathlib.Path(__file__).parent
            args = sys.argv[1:]
            with (root/'commands').open('a') as log: log.write(' '.join(args)+'\n')
            if args[0] == 'get-default-sink': print((root/'default').read_text())
            elif args[0] == 'set-default-sink': (root/'default').write_text(args[1])
            elif args[0] == 'load-module':
                sink = next(a[10:] for a in args if a.startswith('sink_name='))
                (root/'sink').write_text(sink)
                print('9')
            elif args[:3] == ['list', 'short', 'sink-inputs']: print('4\t'+(root/'stream').read_text())
            elif args[:3] == ['list', 'short', 'sinks']:
                print('12\tlocal.speakers')
                print('27\t'+(root/'sink').read_text())
            elif args[:3] == ['list', 'short', 'modules']: print('9\tmodule-null-sink\tsink_name='+(root/'sink').read_text())
            elif args[0] == 'move-sink-input': (root/'stream').write_text('12' if args[2] == 'local.speakers' else '27')
            elif args[0] == 'unload-module': pass
            else: sys.exit(2)
            """ + "\n");
        File.WriteAllText(Path.Combine(directory, "pacat"), """
            #!/usr/bin/python3
            import pathlib, sys, time
            root = pathlib.Path(__file__).parent
            if '--record' in sys.argv:
                while True:
                    sys.stdout.buffer.write(bytes([42])*3840)
                    sys.stdout.buffer.flush()
                    time.sleep(.02)
            else:
                with (root/'received.pcm').open('ab', buffering=0) as output:
                    while True:
                        data = sys.stdin.buffer.read(3840)
                        if not data: break
                        output.write(data)
            """ + "\n");
        foreach (var file in new[] { "pactl", "pacat" }) File.SetUnixFileMode(Path.Combine(directory, file),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return directory;
    }
}
