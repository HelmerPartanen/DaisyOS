using Xunit;
using DaisyOS.Core.Continuity;
using DaisyOS.System.Audio;
using DaisyOS.System.Processes;

namespace DaisyOS.Tests;

public sealed class ContinuityAudioDeviceTests
{
    [Fact]
    public async Task OnlyEligibleContinuityPeersBecomeAudioOutputsWithContinuityIcon()
    {
        var state = new Client();
        var service = new LinuxAudioService(new ScriptedCommandRunner(new CommandResult(0, "Audio\n Sinks:\n * 12. Local speakers [vol: 1.00]\n Sources:\n", "", false)), state);
        var devices = await service.GetAudioDevicesAsync();
        var remote = Assert.Single(devices, d => d.Id.StartsWith("continuity:"));
        Assert.Equal("continuity", remote.IconGlyph);
        Assert.Equal("Laptop", remote.Name);
        Assert.False(remote.IsDefault);
        state.State = state.State with { Paused = true };
        Assert.DoesNotContain(await service.GetAudioDevicesAsync(), d => d.Id.StartsWith("continuity:"));
    }
    [Fact]
    public async Task SelectingContinuityOutputStartsAuthenticatedAudioSession()
    {
        var client = new Client();
        var runner = new ScriptedCommandRunner(new CommandResult(0, "", "", false));
        var service = new LinuxAudioService(runner, client);
        Assert.True(await service.SetDefaultAudioDeviceAsync("continuity:peer"));
        Assert.Contains(client.Commands, c => c.Action == "live" && c.Id == "peer" && c.Value == "audio");
        Assert.Empty(runner.Calls);
    }
    [Fact]
    public async Task SelectingLocalOutputStopsRemoteAudioAndKeepsLocalDeviceSelection()
    {
        var client = new Client();
        client.State = client.State with { Sessions = [new("session", "peer", "Laptop", "audio", true, false)] };
        var runner = new ScriptedCommandRunner(new CommandResult(0, "", "", false));
        var service = new LinuxAudioService(runner, client);
        Assert.True(await service.SetDefaultAudioDeviceAsync("12"));
        Assert.Contains(client.Commands, c => c.Action == "live-stop" && c.Id == "session");
        Assert.Equal(new[] { "set-default", "12" }, Assert.Single(runner.Calls).Arguments);
    }
    [Fact]
    public async Task MicrophoneSelectionNeverStartsOrStopsContinuityPlayback()
    {
        var client = new Client();
        var runner = new ScriptedCommandRunner(new CommandResult(0, "", "", false));
        var service = new LinuxAudioService(runner, client);
        Assert.True(await service.SetDefaultAudioInputDeviceAsync("14"));
        Assert.Empty(client.Commands);
        Assert.Equal(new[] { "set-default", "14" }, Assert.Single(runner.Calls).Arguments);
    }
    private sealed class Client : IContinuityClient
    {
        public ContinuityStatus State = new(true, true, false, true, "This computer", "id", "", "",
            [new("peer", "Laptop", "192.168.1.20", "Trusted · discovered", true, false, false, Audio: true),
             new("disabled", "No permission", "192.168.1.21", "Trusted · discovered", true, false, false),
             new("offline", "Asleep", "192.168.1.22", "Trusted · unreachable", true, false, false, Audio: true)], [], []);
        public List<ContinuityCommand> Commands { get; } = [];
        public Task<ContinuityStatus> ExecuteAsync(ContinuityCommand command, CancellationToken token = default)
        { Commands.Add(command); if (command.Action == "live-stop") State = State with { Sessions = [] }; return Task.FromResult(State); }
    }
}
