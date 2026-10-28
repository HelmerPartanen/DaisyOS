using global::System.Text;
using DaisyOS.Continuity.Protocol.V1;
using DaisyOS.System.Processes;
using Google.Protobuf;

namespace DaisyOS.Continuity;

public sealed partial class ContinuityDaemon
{
    private async Task<string> PulseAsync(params string[] args)
    {
        var result = await new SafeCommandRunner().RunAsync(_mediaExecutableDirectory is null ? "pactl" : global::System.IO.Path.Combine(_mediaExecutableDirectory, "pactl"), args, TimeSpan.FromSeconds(3));
        if (!result.Succeeded) throw new IOException("PulseAudio/PipeWire routing unavailable.");
        return result.StandardOutput.Trim();
    }
    private string AudioRecoveryPath => global::System.IO.Path.Combine(_state.DirectoryPath, "audio-route");
    internal async Task RecoverAudioRouteAsync()
    {
        if (!File.Exists(AudioRecoveryPath)) return;
        SecureState.Check(AudioRecoveryPath, false);
        if (new FileInfo(AudioRecoveryPath).Length > 2048) throw new IOException("Invalid audio recovery state.");
        var record = await File.ReadAllLinesAsync(AudioRecoveryPath);
        if (record.Length != 2 || !record[0].StartsWith("daisy_continuity_", StringComparison.Ordinal) ||
            !Guid.TryParseExact(record[0][17..], "N", out _) || record[1].Length is < 1 or > 512 || record[1].Any(char.IsControl))
            throw new IOException("Invalid audio recovery state.");
        var current = await PulseAsync("get-default-sink");
        if (current == record[0]) { await PulseAsync("set-default-sink", record[1]); current = record[1]; }
        await RestorePlaybackAsync(record[0], current);
        var modules = await PulseAsync("list", "short", "modules");
        foreach (var line in modules.Split('\n'))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 3 && uint.TryParse(parts[0], out _) && parts[1] == "module-null-sink" &&
                parts[2].Split(' ').Contains("sink_name=" + record[0])) await PulseAsync("unload-module", parts[0]);
        }
        File.Delete(AudioRecoveryPath); SecureState.SyncDirectory(_state.DirectoryPath);
    }
    private async Task SendAudioAsync(PeerWire wire, Live live)
    {
        await RecoverAudioRouteAsync();
        var sink = "daisy_continuity_" + live.Info.Id;
        var previous = await PulseAsync("get-default-sink");
        if (previous.StartsWith("daisy_continuity_", StringComparison.Ordinal)) throw new IOException("Another audio route is active.");
        // Only a generated identifier enters module arguments; peer names are UI-only.
        if (previous.Length is < 1 or > 512 || previous.Any(char.IsControl)) throw new IOException("Invalid local sink.");
        SecureState.WritePrivate(AudioRecoveryPath, Encoding.UTF8.GetBytes(sink + "\n" + previous));
        var module = await PulseAsync("load-module", "module-null-sink", "sink_name=" + sink, "rate=48000", "channels=2", "sink_properties=device.description=Continuity");
        if (!uint.TryParse(module, out _)) throw new IOException("Invalid audio module.");
        try
        {
            await PulseAsync("set-default-sink", sink);
            await MovePlaybackAsync(sink);
            using var record = StartPipe("pacat", ["--record", "--device=" + sink + ".monitor", "--raw", "--format=s16le", "--rate=48000", "--channels=2", "--latency-msec=40", "--client-name=DaisyOS Continuity"]);
            using var kill = live.Stop.Token.Register(() => Kill(record));
            var receive = MonitorAudioPeerAsync(wire, live);
            try
            {
                ulong sequence = 0; var bytes = new byte[AudioBytes];
                while (true)
                {
                    await record.StandardOutput.BaseStream.ReadExactlyAsync(bytes, live.Stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), live.Stop.Token);
                    await wire.SendAsync(new() { LivePacket = new() { Sequence = ++sequence, Data = ByteString.CopyFrom(bytes) } }, live.Stop.Token);
                }
            }
            finally { Kill(record); await live.Stop.CancelAsync(); try { await receive; } catch (OperationCanceledException) { } }
        }
        finally
        {
            try
            {
                var current = await PulseAsync("get-default-sink");
                if (current == sink) await PulseAsync("set-default-sink", previous);
                else previous = current;
                // Only streams belonging to this sink are moved during cleanup.
                await RestorePlaybackAsync(sink, previous);
            }
            finally
            {
                await PulseAsync("unload-module", module);
                File.Delete(AudioRecoveryPath); SecureState.SyncDirectory(_state.DirectoryPath);
            }
        }
    }
    private async Task MonitorAudioPeerAsync(PeerWire wire, Live live)
    {
        try
        {
            ulong expected = 1;
            while (true)
            {
                var packet = (await wire.ReceiveAsync(live.Stop.Token).WaitAsync(TimeSpan.FromSeconds(10), live.Stop.Token)).LivePacket;
                if (packet is null || packet.Sequence != expected++ || packet.Data.Length != 0) throw new InvalidDataException();
            }
        }
        finally { await live.Stop.CancelAsync(); }
    }
    private async Task ReceiveAudioAsync(PeerWire wire, Live live)
    {
        var output = await PulseAsync("get-default-sink");
        if (output.StartsWith("daisy_continuity_", StringComparison.Ordinal)) throw new IOException("Choose local speakers before receiving audio.");
        using var playback = StartPipe("pacat", ["--playback", "--device=" + output, "--raw", "--format=s16le", "--rate=48000", "--channels=2", "--latency-msec=60", "--client-name=DaisyOS Continuity"]);
        using var kill = live.Stop.Token.Register(() => Kill(playback));
        await wire.SendAsync(new() { LiveDecision = new() { Accepted = true } }, live.Stop.Token);
        var heartbeat = Task.Run(async () =>
        {
            ulong sequence = 0;
            while (true) { await Task.Delay(2000, live.Stop.Token); await wire.SendAsync(new() { LivePacket = new() { Sequence = ++sequence } }, live.Stop.Token); }
        });
        try
        {
            ulong expected = 1;
            while (true)
            {
                Require(wire.PeerId, "audio");
                var packet = (await wire.ReceiveAsync(live.Stop.Token).WaitAsync(TimeSpan.FromSeconds(5), live.Stop.Token)).LivePacket ?? throw new InvalidDataException();
                if (packet.Sequence != expected++ || packet.Data.Length != AudioBytes) throw new InvalidDataException();
                await playback.StandardInput.BaseStream.WriteAsync(packet.Data.Memory, live.Stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(3), live.Stop.Token);
            }
        }
        finally { Kill(playback); await live.Stop.CancelAsync(); try { await heartbeat; } catch (OperationCanceledException) { } }
    }
    private async Task MovePlaybackAsync(string sink)
    {
        var streams = await PulseAsync("list", "short", "sink-inputs");
        foreach (var line in streams.Split('\n'))
        {
            var id = line.Split('\t')[0];
            if (uint.TryParse(id, out _)) await PulseAsync("move-sink-input", id, sink);
        }
    }
    private async Task RestorePlaybackAsync(string sink, string previous)
    {
        var sinks = await PulseAsync("list", "short", "sinks");
        var index = sinks.Split('\n').Select(l => l.Split('\t')).FirstOrDefault(p => p.Length >= 2 && p[1] == sink)?[0];
        if (index is null) return;
        var streams = await PulseAsync("list", "short", "sink-inputs");
        foreach (var line in streams.Split('\n'))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2 && parts[1] == index && uint.TryParse(parts[0], out _)) await PulseAsync("move-sink-input", parts[0], previous);
        }
    }
}
