using System.Net;
using System.Net.Sockets;
using DaisyOS.Continuity;
using Xunit;

namespace DaisyOS.Continuity.Tests;

public sealed class EdgeInputTests
{
    [Theory]
    [InlineData("6 1 0 0.25 0")]
    [InlineData("7 0 0 0.75 0.000000")]
    [InlineData("3 0 0 -2.5 3.5")]
    public void EdgeMessagesAreBoundedAndTyped(string command) => ContinuityDaemon.ValidateEdgeInput(command);
    [Theory]
    [InlineData("6 4 0 0.5 0")]
    [InlineData("6 1 0 NaN 0")]
    [InlineData("7 0 0 -0.1 0")]
    [InlineData("6 1 0 0.5 0\n1 30 1 0 0")]
    public void MalformedEdgeMessagesAreRejected(string command) => Assert.Throws<InvalidDataException>(() => ContinuityDaemon.ValidateEdgeInput(command));

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("top")]
    [InlineData("bottom")]
    public async Task CapturedInputAndPointerReturnCrossAuthenticatedTls(string side)
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-edge-wire-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourceTools = Tools(Path.Combine(root, "source-tools")); var targetTools = Tools(Path.Combine(root, "target-tools"));
            if (side == "top") File.WriteAllText(Path.Combine(sourceTools, "delay"), "5"); // Human consent may exceed the peer liveness timeout.
            using var source = new SecureState(Path.Combine(root, "source")); using var target = new SecureState(Path.Combine(root, "target"));
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            source.Settings.Enabled = target.Settings.Enabled = true;
            source.Settings.Peers[target.Id] = new(Convert.ToBase64String(target.Identity.RawData), "Target", "127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port);
            target.Settings.Peers[source.Id] = new(Convert.ToBase64String(source.Identity.RawData), "Source", "127.0.0.1:1");
            await using var sender = new ContinuityDaemon(source, 47621, () => true, root, "unix:path=/nonexistent-edge-test", sourceTools);
            await using var receiver = new ContinuityDaemon(target, 47621, () => true, root, "unix:path=/nonexistent-edge-test", targetTools);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var receive = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var wire = await PeerWire.OpenAsync(socket, target, true, false, source.Id, (_, _) => true, timeout.Token);
                var start = await wire.ReceiveAsync(timeout.Token);
                Assert.Equal("input", start.LiveStart.Kind);
                try { await receiver.ReceiveLiveAsync(wire, start.LiveStart, timeout.Token); } catch (Exception e) when (e is IOException or OperationCanceledException) { }
            });
            await sender.ExecuteAsync(new("edge", target.Id, side), "test-ui");
            while (!File.Exists(Path.Combine(sourceTools, "returned"))) await Task.Delay(10, timeout.Token);
            Assert.Contains("--capture " + side, File.ReadAllText(Path.Combine(sourceTools, "args")));
            Assert.Contains("1 30 1", File.ReadAllText(Path.Combine(targetTools, "events")));
            Assert.Contains("release 0.25", File.ReadAllText(Path.Combine(sourceTools, "returned")));
            Assert.True(target.Settings.Peers[source.Id].Input); // target portal grant, not an unrelated sender toggle
            Assert.False(source.Settings.Peers[target.Id].Input);
            Assert.Equal(side, source.Settings.Peers[target.Id].InputEdge);
            await receiver.ExecuteAsync(new("permission", source.Id, "input", false), "test-ui");
            await receive.WaitAsync(timeout.Token);
            while (sender.Snapshot().Sessions!.Any(s => s.State == "Ready")) await Task.Delay(10, timeout.Token);
            Assert.False(target.Settings.Peers[source.Id].Input);
            Assert.Equal(1, target.Settings.Peers[source.Id].BlockedFeatures & 1);
        }
        finally { Directory.Delete(root, true); }
    }
    private static string Tools(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "daisyos-continuity-media");
        File.WriteAllText(path, """
            #!/usr/bin/python3
            import pathlib,struct,sys,threading,time
            root=pathlib.Path(__file__).parent
            (root/'args').write_text(' '.join(sys.argv[1:]))
            lock=threading.Lock()
            def frame(value=b''):
                with lock: sys.stdout.buffer.write(struct.pack('>I',len(value))+value);sys.stdout.buffer.flush()
            if sys.argv[1]=='--capture' and (root/'delay').exists(): time.sleep(float((root/'delay').read_text()))
            frame()
            def heartbeat():
                while True: time.sleep(2);frame()
            threading.Thread(target=heartbeat,daemon=True).start()
            if sys.argv[1]=='--capture':
                time.sleep(.05)
                sides=['left','right','top','bottom']
                frame(('6 %d 0 0.5 0'%sides.index(sys.argv[2])).encode())
                frame(b'1 30 1 0 0')
                frame(b'1 30 0 0 0')
                frame(b'3 0 0 -10 0')
                for line in sys.stdin:
                    if line.startswith('release '): (root/'returned').write_text(line)
            else:
                for line in sys.stdin:
                    with (root/'events').open('a') as log: log.write(line)
                    if line.startswith('3 '): frame(b'7 0 0 0.25 0')
            """ + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return directory;
    }
}
