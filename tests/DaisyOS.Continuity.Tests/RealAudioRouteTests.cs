using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DaisyOS.Continuity;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Continuity.Tests;

public sealed class RealAudioFact : FactAttribute
{
    public RealAudioFact() { if (Environment.GetEnvironmentVariable("DAISYOS_REAL_AUDIO_TEST") != "1") Skip = "Opt in to the isolated real PipeWire/PulseAudio integration test."; }
}
public sealed class RealAudioRouteTests
{
    [RealAudioFact]
    public async Task RealPcmCrossesTlsAndReachesARealPlaybackSinkWithoutChangingUserOutputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-real-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var unique = Guid.NewGuid().ToString("N");
        var targetSink = "daisy_test_target_" + unique;
        var localSink = "daisy_test_local_" + unique;
        var modules = new List<string>();
        async Task<string> Pulse(params string[] args)
        {
            var result = await new SafeCommandRunner().RunAsync("pactl", args, TimeSpan.FromSeconds(3));
            Assert.True(result.Succeeded, result.StandardError); return result.StandardOutput.Trim();
        }
        Process Start(params string[] args)
        {
            var info = new ProcessStartInfo("pacat") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            return Process.Start(info)!;
        }
        var original = await Pulse("get-default-sink");
        try
        {
            modules.Add(await Pulse("load-module", "module-null-sink", "sink_name=" + targetSink, "rate=48000", "channels=2"));
            modules.Add(await Pulse("load-module", "module-null-sink", "sink_name=" + localSink, "rate=48000", "channels=2"));
            string Tools(string name, string defaultSink)
            {
                var directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "default"), defaultSink);
                // Only default selection and the list of streams are isolated.
                // Module creation, capture, playback and TLS all use the real audio server.
                File.WriteAllText(Path.Combine(directory, "pactl"), """
                    #!/usr/bin/python3
                    import pathlib, subprocess, sys
                    root=pathlib.Path(__file__).parent
                    args=sys.argv[1:]
                    if args[0]=='get-default-sink': print((root/'default').read_text())
                    elif args[0]=='set-default-sink': (root/'default').write_text(args[1])
                    elif args[:3]==['list','short','sink-inputs']: pass
                    else: sys.exit(subprocess.call(['/usr/bin/pactl',*args]))
                    """ + "\n");
                File.SetUnixFileMode(Path.Combine(directory, "pactl"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.CreateSymbolicLink(Path.Combine(directory, "pacat"), "/usr/bin/pacat"); return directory;
            }
            var sourceTools = Tools("source-tools", localSink); var targetTools = Tools("target-tools", targetSink);
            using var first = new SecureState(Path.Combine(root, "first")); using var second = new SecureState(Path.Combine(root, "second"));
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            first.Settings.Enabled = second.Settings.Enabled = true;
            first.Settings.Peers[second.Id] = new(Convert.ToBase64String(second.Identity.RawData), "Target", "127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port);
            second.Settings.Peers[first.Id] = new(Convert.ToBase64String(first.Identity.RawData), "Source", "127.0.0.1:1", Audio: true);
            await using var sender = new ContinuityDaemon(first, 47621, () => true, root, "unix:path=/nonexistent-real-audio-test", sourceTools);
            await using var receiver = new ContinuityDaemon(second, 47621, () => true, root, "unix:path=/nonexistent-real-audio-test", targetTools);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var receive = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var wire = await PeerWire.OpenAsync(socket, second, true, false, first.Id, (_, _) => true, timeout.Token);
                var request = await wire.ReceiveAsync(timeout.Token);
                try { await receiver.ReceiveLiveAsync(wire, request.LiveStart, timeout.Token); } catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
            });
            using var monitor = Start("--record", "--device=" + targetSink + ".monitor", "--raw", "--format=s16le", "--rate=48000", "--channels=2", "--latency-msec=20");
            await sender.ExecuteAsync(new("live", second.Id, "audio"), "test-ui");
            string route;
            do { await Task.Delay(10, timeout.Token); route = File.ReadAllText(Path.Combine(sourceTools, "default")); } while (!route.StartsWith("daisy_continuity_"));
            await Task.Delay(6500, timeout.Token); // Select an output before starting music.
            Assert.Contains(sender.Snapshot().Sessions!, session => session.State == "Connected");
            using var tone = Start("--playback", "--device=" + route, "--raw", "--format=s16le", "--rate=48000", "--channels=2", "--latency-msec=20");
            var producer = Task.Run(async () =>
            {
                var buffer = new byte[3840]; var sample = 0;
                while (!timeout.IsCancellationRequested)
                {
                    for (var i = 0; i < buffer.Length; i += 4) { var value = (short)(8000 * Math.Sin(2 * Math.PI * 440 * sample++ / 48000)); buffer[i] = buffer[i+2] = (byte)value; buffer[i+1] = buffer[i+3] = (byte)(value >> 8); }
                    await tone.StandardInput.BaseStream.WriteAsync(buffer, timeout.Token);
                }
            });
            try
            {
                var bytes = new byte[3840]; var found = false;
                while (!found)
                {
                    await monitor.StandardOutput.BaseStream.ReadExactlyAsync(bytes, timeout.Token);
                    found = Enumerable.Range(0, bytes.Length / 2).Any(i => Math.Abs((int)BitConverter.ToInt16(bytes, i*2)) > 2000);
                }
                Assert.True(found, "The 440Hz source reached the real remote playback monitor.");
                Assert.Equal(original, await Pulse("get-default-sink"));
                await sender.ExecuteAsync(new("live-stop", sender.Snapshot().Sessions!.Single(s => s.State == "Connected").Id), "test-ui");
                while (sender.Snapshot().Sessions!.Any(s => s.State == "Connected")) await Task.Delay(10, timeout.Token);
                await receive.WaitAsync(timeout.Token);
                Assert.Equal(localSink, File.ReadAllText(Path.Combine(sourceTools, "default")));
            }
            finally { timeout.Cancel(); if (!tone.HasExited) tone.Kill(); if (!monitor.HasExited) monitor.Kill(); try { await producer; } catch (Exception ex) when (ex is IOException or OperationCanceledException) { } }
        }
        finally
        {
            foreach (var module in modules) await Pulse("unload-module", module);
            Directory.Delete(root, true);
        }
    }
}
