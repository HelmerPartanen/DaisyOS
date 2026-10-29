using System.Globalization;
using System.Text.RegularExpressions;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Audio;

public sealed partial class LinuxAudioService : IAudioService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(3);
    private readonly ICommandRunner _commandRunner;
    private readonly DaisyOS.Core.Continuity.IContinuityClient _continuity;

    public LinuxAudioService(ICommandRunner commandRunner, DaisyOS.Core.Continuity.IContinuityClient? continuity = null)
    {
        _commandRunner = commandRunner;
        _continuity = continuity ?? new Continuity.ContinuityClient();
    }

    public async Task<double?> GetVolumeAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            "wpctl",
            ["get-volume", "@DEFAULT_AUDIO_SINK@"],
            CommandTimeout,
            cancellationToken);

        if (!result.Succeeded)
        {
            return null;
        }

        var match = VolumeRegex().Match(result.StandardOutput);
        if (!match.Success ||
            !double.TryParse(match.Groups["volume"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawVolume))
        {
            return null;
        }

        return Math.Clamp(rawVolume * 100, 0, 100);
    }

    public Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default)
    {
        var normalized = (int)Math.Round(Math.Clamp(volume, 0, 100));
        return _commandRunner.RunAsync(
            "wpctl",
            ["set-volume", "@DEFAULT_AUDIO_SINK@", $"{normalized}%"],
            CommandTimeout,
            cancellationToken);
    }

    public async Task<bool> GetMutedAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            "wpctl",
            ["get-volume", "@DEFAULT_AUDIO_SINK@"],
            CommandTimeout,
            cancellationToken);

        if (!result.Succeeded)
        {
            return false;
        }

        return result.StandardOutput.Contains("[MUTED]", StringComparison.OrdinalIgnoreCase);
    }

    public Task SetMutedAsync(bool muted, CancellationToken cancellationToken = default)
    {
        return _commandRunner.RunAsync(
            "wpctl",
            ["set-mute", "@DEFAULT_AUDIO_SINK@", muted ? "1" : "0"],
            CommandTimeout,
            cancellationToken);
    }



    public async Task<double?> GetInputVolumeAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            "wpctl",
            ["get-volume", "@DEFAULT_AUDIO_SOURCE@"],
            CommandTimeout,
            cancellationToken);

        if (!result.Succeeded)
        {
            return null;
        }

        var match = VolumeRegex().Match(result.StandardOutput);
        if (!match.Success ||
            !double.TryParse(match.Groups["volume"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawVolume))
        {
            return null;
        }

        return Math.Clamp(rawVolume * 100, 0, 100);
    }

    public Task SetInputVolumeAsync(double volume, CancellationToken cancellationToken = default)
    {
        var normalized = (int)Math.Round(Math.Clamp(volume, 0, 100));
        return _commandRunner.RunAsync(
            "wpctl",
            ["set-volume", "@DEFAULT_AUDIO_SOURCE@", $"{normalized}%"],
            CommandTimeout,
            cancellationToken);
    }

    public async Task<bool> GetInputMutedAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            "wpctl",
            ["get-volume", "@DEFAULT_AUDIO_SOURCE@"],
            CommandTimeout,
            cancellationToken);

        if (!result.Succeeded)
        {
            return false;
        }
        
        return result.StandardOutput.Contains("[MUTED]", StringComparison.OrdinalIgnoreCase);
    }

    public Task SetInputMutedAsync(bool muted, CancellationToken cancellationToken = default)
    {
        return _commandRunner.RunAsync(
            "wpctl",
            ["set-mute", "@DEFAULT_AUDIO_SOURCE@", muted ? "1" : "0"],
            CommandTimeout,
            cancellationToken);
    }

    public async Task<string> GetDefaultDeviceNameAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            "wpctl",
            ["status"],
            CommandTimeout,
            cancellationToken);

        if (!result.Succeeded)
        {
            return "System Output";
        }

        var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool inSinks = false;
        foreach (var line in lines)
        {
            if (line.Contains("Sinks:", StringComparison.OrdinalIgnoreCase))
            {
                inSinks = true;
                continue;
            }
            if (inSinks)
            {
                if (line.Contains("Sources:", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Video", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                if (line.StartsWith("*"))
                {
                    var clean = line.TrimStart('*', ' ', '\t');
                    var firstDot = clean.IndexOf('.');
                    if (firstDot > 0 && int.TryParse(clean[..firstDot], out _))
                    {
                        clean = clean[(firstDot + 1)..].Trim();
                    }
                    var volStart = clean.IndexOf('[');
                    if (volStart > 0)
                    {
                        clean = clean[..volStart].Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        return clean;
                    }
                }
            }
        }

        return "System Output";
    }

    public async Task<IReadOnlyList<AudioDeviceInfo>> GetAudioDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            "wpctl",
            ["status"],
            CommandTimeout,
            cancellationToken);

        var list = new List<AudioDeviceInfo>();

        if (result.Succeeded)
        {
            var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool inSinks = false;
            foreach (var line in lines)
            {
                if (line.Contains("Sinks:", StringComparison.OrdinalIgnoreCase))
                {
                    inSinks = true;
                    continue;
                }
                if (inSinks)
                {
                    if (line.Contains("Sources:", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Filters:", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Streams:", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Video", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    var clean = line.TrimStart(' ', '\t', '│', '├', '─', '└');
                    var isDefault = clean.StartsWith('*');
                    clean = clean.TrimStart('*', ' ', '\t');
                    var firstDot = clean.IndexOf('.');
                    if (firstDot > 0 && int.TryParse(clean[..firstDot], out var idVal))
                    {
                        var deviceId = idVal.ToString();
                        var name = clean[(firstDot + 1)..].Trim();
                        var volStart = name.IndexOf('[');
                        if (volStart > 0) name = name[..volStart].Trim();

                        // Stream-channel records (for example output_FL) are not output devices.
                        if (!string.IsNullOrWhiteSpace(name) &&
                            !name.StartsWith("output_", StringComparison.OrdinalIgnoreCase) && name != "Continuity")
                        {
                            var isBt = name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) || IsHeadphones(name);
                            var icon = IsHeadphones(name) ? "headphones" : "speaker";
                            list.Add(new AudioDeviceInfo(deviceId, name, icon, isDefault, isBt));
                        }
                    }
                }
            }
        }

        if (list.Count == 0 &&
            string.Equals(Environment.GetEnvironmentVariable("DAISYOS_MOCK_AUDIO"), "1", StringComparison.Ordinal))
        {
            list.Add(new AudioDeviceInfo("default", "Internal Speakers", "speaker", true));
            list.Add(new AudioDeviceInfo("hdmi", "HDMI / DisplayPort Output", "speaker", false));
            list.Add(new AudioDeviceInfo("bt", "Wireless Headphones", "headphones", false, true));
        }

        try
        {
            var status = await _continuity.ExecuteAsync(new("status"), cancellationToken);
            if (status.Enabled && status.SessionReady && !status.Paused)
            {
                var route = status.Sessions?.FirstOrDefault(s => s.Kind == "audio" && s.Outgoing && s.State is not ("Failed" or "Declined"));
                if (route?.State == "Connected") for (var i = 0; i < list.Count; i++) list[i] = list[i] with { IsDefault = false };
                foreach (var peer in status.Devices.Where(p => p.Trusted && (route?.PeerId == p.Id || !p.Status.Contains("unreachable", StringComparison.OrdinalIgnoreCase))))
                    list.Add(new AudioDeviceInfo("continuity:" + peer.Id, peer.Name, "continuity", route?.PeerId == peer.Id && route.State == "Connected") { Status = route?.PeerId == peer.Id ? route.State : null });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { /* The local audio device list works while Continuity is stopped. */ }
        return list;
    }

    private static bool IsHeadphones(string deviceName) =>
        deviceName.Contains("headphone", StringComparison.OrdinalIgnoreCase) ||
        deviceName.Contains("headset", StringComparison.OrdinalIgnoreCase) ||
        deviceName.Contains("earbud", StringComparison.OrdinalIgnoreCase) ||
        deviceName.Contains("airpod", StringComparison.OrdinalIgnoreCase) ||
        deviceName.Contains("hands-free", StringComparison.OrdinalIgnoreCase) ||
        deviceName.Contains("handsfree", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<AudioDeviceInfo>> GetAudioInputDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            "wpctl",
            ["status"],
            CommandTimeout,
            cancellationToken);

        var list = new List<AudioDeviceInfo>();

        if (result.Succeeded)
        {
            var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool inSources = false;
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Contains("Sources:", StringComparison.OrdinalIgnoreCase))
                {
                    inSources = true;
                    continue;
                }
                if (inSources)
                {
                    if (line.Contains("Filters:", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Streams:", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Video", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Settings", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    var clean = line.TrimStart(' ', '\t', '│', '├', '─', '└');
                    var isDefault = clean.StartsWith('*');
                    clean = clean.TrimStart('*', ' ', '\t');
                    var firstDot = clean.IndexOf('.');
                    if (firstDot > 0 && int.TryParse(clean[..firstDot], out var idVal))
                    {
                        var deviceId = idVal.ToString();
                        var name = clean[(firstDot + 1)..].Trim();
                        var volStart = name.IndexOf('[');
                        if (volStart > 0) name = name[..volStart].Trim();

                        if (!string.IsNullOrWhiteSpace(name) &&
                            !name.StartsWith("input_", StringComparison.OrdinalIgnoreCase))
                        {
                            var isBt = name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);
                            var icon = IsHeadphones(name) ? "headset" : "microphone";
                            list.Add(new AudioDeviceInfo(deviceId, name, icon, isDefault, isBt));
                        }
                    }
                }
            }
        }

        if (list.Count == 0 &&
            string.Equals(Environment.GetEnvironmentVariable("DAISYOS_MOCK_AUDIO"), "1", StringComparison.Ordinal))
        {
            list.Add(new AudioDeviceInfo("mic-default", "Internal Microphone", "microphone", true));
        }

        return list;
    }

    public async Task<bool> SetDefaultAudioInputDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || !int.TryParse(deviceId, out _))
        {
            return false;
        }

        var result = await _commandRunner.RunAsync(
            "wpctl",
            ["set-default", deviceId],
            CommandTimeout,
            cancellationToken);
        return result.Succeeded;
    }

    public async Task<IReadOnlyList<AudioAppStreamInfo>> GetAudioAppStreamsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            "wpctl",
            ["status"],
            CommandTimeout,
            cancellationToken);

        var list = new List<AudioAppStreamInfo>();

        if (result.Succeeded)
        {
            var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            bool inStreams = false;
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Contains("Streams:", StringComparison.OrdinalIgnoreCase))
                {
                    inStreams = true;
                    continue;
                }
                if (inStreams)
                {
                    if (line.Contains("Video", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Settings", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    // Look for lines like "77. Spotify"
                    var clean = line.TrimStart('*', ' ', '\t', '│', '├', '─', '└');
                    var firstDot = clean.IndexOf('.');
                    if (firstDot > 0 && int.TryParse(clean[..firstDot], out var streamIdVal))
                    {
                        var streamId = streamIdVal.ToString();
                        var appName = clean[(firstDot + 1)..].Trim();
                        if (appName.Contains('>')) continue; // Sub-channel routing line

                        // Query volume for this stream
                        var volRes = await _commandRunner.RunAsync(
                            "wpctl",
                            ["get-volume", streamId],
                            CommandTimeout,
                            cancellationToken);

                        double vol = 100;
                        bool isMuted = false;
                        if (volRes.Succeeded)
                        {
                            isMuted = volRes.StandardOutput.Contains("[MUTED]", StringComparison.OrdinalIgnoreCase);
                            var match = VolumeRegex().Match(volRes.StandardOutput);
                            if (match.Success && double.TryParse(match.Groups["volume"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawVol))
                            {
                                vol = Math.Clamp(rawVol * 100, 0, 100);
                            }
                        }

                        list.Add(new AudioAppStreamInfo(streamId, appName, vol, isMuted));
                    }
                }
            }
        }

        return list;
    }

    public Task SetAppStreamVolumeAsync(string streamId, double volume, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(streamId) || !int.TryParse(streamId, out _))
        {
            return Task.CompletedTask;
        }

        var normalized = (int)Math.Round(Math.Clamp(volume, 0, 100));
        return _commandRunner.RunAsync(
            "wpctl",
            ["set-volume", streamId, $"{normalized}%"],
            CommandTimeout,
            cancellationToken);
    }

    public async Task<bool> SetDefaultAudioDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var continuity = _continuity;
            if (deviceId.StartsWith("continuity:", StringComparison.Ordinal))
            {
                var state = await continuity.ExecuteAsync(new("status"), cancellationToken);
                if (state.Sessions?.Any(s => s.Kind == "audio" && s.Outgoing && s.PeerId == deviceId[11..]) == true) return true;
                await StopContinuityAudioAsync(state, cancellationToken);
                await continuity.ExecuteAsync(new("live", deviceId[11..], "audio"), cancellationToken);
                return true;
            }
            if (!int.TryParse(deviceId, out _)) return false;
            try
            {
                var state = await continuity.ExecuteAsync(new("status"), cancellationToken);
                await StopContinuityAudioAsync(state, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return false; }

        var result = await _commandRunner.RunAsync(
            "wpctl",
            ["set-default", deviceId],
            CommandTimeout,
            cancellationToken);
        return result.Succeeded;
    }

    private async Task StopContinuityAudioAsync(DaisyOS.Core.Continuity.ContinuityStatus state, CancellationToken token)
    {
        var sessions = (state.Sessions ?? []).Where(s => s.Kind == "audio" && s.Outgoing && s.State is not ("Failed" or "Declined")).ToArray();
        foreach (var session in sessions) await _continuity.ExecuteAsync(new("live-stop", session.Id), token);
        if (sessions.Length == 0) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        // Cleanup restores/moves the old streams before a new output is selected.
        while (true)
        {
            state = await _continuity.ExecuteAsync(new("status"), timeout.Token);
            if (state.Sessions?.Any(s => sessions.Any(old => old.Id == s.Id)) != true) return;
            await Task.Delay(50, timeout.Token);
        }
    }

    [GeneratedRegex(@"Volume:\s+(?<volume>[0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex VolumeRegex();
}
