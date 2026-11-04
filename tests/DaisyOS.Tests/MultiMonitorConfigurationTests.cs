using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class MultiMonitorConfigurationTests
{
    private static DetectedDisplay Display(string name, int width, int height, double scale, bool primary, int x = 0) =>
        new(name, width, height, scale, 60, [$"{width} × {height}"], [60], [new("mode", width, height, 60)], primary, x, Priority: primary ? 1 : 2);

    [Fact]
    public void ExtendUsesLogicalWidthsAndMirroringTargetsThePrimaryOutput()
    {
        var displays = new[] { Display("eDP-1", 1920, 1080, 1.25, true), Display("HDMI-A-1", 3840, 2160, 2, false) };
        Assert.Contains("output.HDMI-A-1.position.1536,0", DisplaySettingsHelper.LayoutArguments(displays, false));
        var mirror = DisplaySettingsHelper.LayoutArguments(displays, true);
        Assert.Contains("output.HDMI-A-1.mirror.eDP-1", mirror);
        Assert.Contains("output.HDMI-A-1.position.0,0", mirror);
        Assert.Contains("output.eDP-1.mirror.none", mirror);
        Assert.Equal(864, displays[0].LogicalHeight);
        Assert.Equal(1080, (displays[1] with { Rotation = 2 }).LogicalWidth);
    }

    [Fact]
    public void ParserResolvesMirrorIdentityAndChoosesExactlyOnePrimary()
    {
        var outputs = DisplaySettingsHelper.ParseAllDisplaysKScreenDoctorJson("""
        {"outputs":[
        {"id":7,"name":"HDMI-A-1","enabled":true,"connected":true,"priority":2,"scale":0,"replicationSource":9,"size":{"width":3840,"height":2160}},
        {"id":9,"name":"eDP-1","enabled":true,"connected":true,"priority":1,"scale":1.25,"size":{"width":1920,"height":1080}},
        {"name":"bad","enabled":"invalid","connected":true},
        {"name":"unplugged","enabled":true,"connected":false}]}
        """);
        Assert.Equal(2, outputs.Count);
        Assert.Equal("eDP-1", outputs[0].MirrorSource);
        Assert.Equal(1, outputs[0].Scale);
        Assert.Single(outputs, d => d.IsPrimary);
        Assert.Equal("eDP-1", outputs.Single(d => d.IsPrimary).Name);
        Assert.Equal(DisplaySettingsHelper.ConfigurationKey(outputs), DisplaySettingsHelper.ConfigurationKey(outputs.Reverse().ToArray()));
    }

    [Fact]
    public async Task WaylandNeverFallsBackToXwaylandVirtualOutputs()
    {
        var runner = new Runner("{}");
        Assert.Empty(await DisplaySettingsHelper.DetectAllDisplaysAsync(runner, waylandSession: true));
        Assert.Single(runner.Calls);
        Assert.Equal("kscreen-doctor", runner.Calls[0].File);
    }

    [Fact]
    public async Task RestoreIncludesOtherOutputsGeometryButDoesNotTargetUnpluggedConnectors()
    {
        var runner = new Runner("""
        {"outputs":[{"name":"eDP-1","enabled":true,"connected":true,"priority":1,"scale":1,"size":{"width":1920,"height":1080}}]}
        """);
        var previous = new[] { Display("eDP-1", 1920, 1080, 1.25, true, 100), Display("HDMI-A-1", 3840, 2160, 1, false, 1636) };
        Assert.True(await DisplaySettingsHelper.RestoreConfigurationAsync(previous, runner));
        Assert.Equal(2, runner.Calls.Count);
        Assert.Contains("output.eDP-1.scale.1.25", runner.Calls[1].Arguments);
        Assert.Contains("output.eDP-1.position.100,0", runner.Calls[1].Arguments);
        Assert.DoesNotContain(runner.Calls[1].Arguments, a => a.Contains("HDMI-A-1"));
    }

    [Fact]
    public void SecondaryFullscreenCannotOccludePrimaryBarsEvenWhenItIsNotATaskbarWindow()
    {
        var coverage = OutputCoverage.Parse("""
        {"windows":[],"outputCoverage":[{"outputName":"HDMI-A-1","covered":true,"fullscreen":true}]}
        """);
        Assert.False(OutputCoverage.IsCovered("eDP-1", [], coverage, true, true));
        Assert.True(OutputCoverage.IsCovered("HDMI-A-1", [], coverage, true, true));
        Assert.True(OutputCoverage.IsCovered(null, [], coverage, true, true));
    }

    private sealed class Runner(string json) : ICommandRunner
    {
        public readonly List<(string File, IReadOnlyList<string> Arguments)> Calls = [];
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Calls.Add((fileName, arguments));
            return Task.FromResult(CommandResult.Success(json));
        }
    }
}
