using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxDisplaySettingsTests
{
    private const string SampleKScreenDoctorJson = """
    {
        "outputs": [
            {
                "connected": true,
                "currentModeId": "5",
                "enabled": true,
                "hdr": true,
                "wcg": true,
                "name": "DP-3",
                "priority": 1,
                "scale": 1.25,
                "size": {
                    "height": 1080,
                    "width": 1920
                },
                "modes": [
                    {
                        "id": "1",
                        "name": "2560x1440@165",
                        "refreshRate": 164.98,
                        "size": { "height": 1440, "width": 2560 }
                    },
                    {
                        "id": "2",
                        "name": "2560x1440@180",
                        "refreshRate": 179.95,
                        "size": { "height": 1440, "width": 2560 }
                    },
                    {
                        "id": "5",
                        "name": "1920x1080@180",
                        "refreshRate": 179.91,
                        "size": { "height": 1080, "width": 1920 }
                    },
                    {
                        "id": "7",
                        "name": "1920x1080@120",
                        "refreshRate": 119.88,
                        "size": { "height": 1080, "width": 1920 }
                    },
                    {
                        "id": "8",
                        "name": "1920x1080@60",
                        "refreshRate": 60.0,
                        "size": { "height": 1080, "width": 1920 }
                    },
                    {
                        "id": "18",
                        "name": "1280x720@60",
                        "refreshRate": 60.0,
                        "size": { "height": 720, "width": 1280 }
                    }
                ]
            }
        ]
    }
    """;

    private const string SampleXrandrOutput = """
    Screen 0: minimum 320 x 200, current 1920 x 1080, maximum 16384 x 16384
    HDMI-1 disconnected (normal left inverted right x axis y axis)
    DP-1 connected primary 1920x1080+0+0 (normal left inverted right x axis y axis) 598mm x 336mm
       2560x1440    143.91 + 119.95   59.95  
       1920x1080    143.85   119.98    60.00*+  50.00  
       1280x720      60.00    59.94  
    """;

    [Fact]
    public void ParseKScreenDoctorJson_ParsesPrimaryDisplayAndModesCorrectly()
    {
        var display = DisplaySettingsHelper.ParseKScreenDoctorJson(SampleKScreenDoctorJson);

        Assert.NotNull(display);
        Assert.Equal("DP-3", display.Name);
        Assert.Equal(1920, display.Width);
        Assert.Equal(1080, display.Height);
        Assert.Equal(1.25, display.Scale);
        Assert.Equal(179.91, display.RefreshRate);
        Assert.True(display.HdrAvailable);
        Assert.True(display.HdrEnabled);
        Assert.True(display.WideColorGamutEnabled);

        // Resolutions ordered by area descending
        Assert.Equal(new[] { "2560 × 1440", "1920 × 1080", "1280 × 720" }, display.AvailableResolutions);

        // Rates available for current resolution (1920x1080)
        Assert.Equal(new[] { 60.0, 119.88, 179.91 }, display.AvailableRefreshRates);
    }

    [Fact]
    public void ParseKScreenDoctorJson_AcceptsNumericModeIdsWithoutFallingBackTo60Hz()
    {
        var json = SampleKScreenDoctorJson.Replace("\"currentModeId\": \"5\"", "\"currentModeId\": 5")
            .Replace("\"id\": \"5\"", "\"id\": 5");

        var display = DisplaySettingsHelper.ParseKScreenDoctorJson(json);

        Assert.NotNull(display);
        Assert.Equal(179.91, display.RefreshRate);
    }

    [Theory]
    [InlineData(179.95199584960938, "180")]
    [InlineData(164.98300170898438, "165")]
    [InlineData(59.94, "60")]
    public void FormatRefreshRate_UsesReadableAccurateLabels(double rate, string expected)
    {
        Assert.Equal(expected, DisplaySettingsHelper.FormatRefreshRate(rate));
    }

    [Fact]
    public void ParseXrandrOutput_ParsesConnectedDisplayAndModes()
    {
        var display = DisplaySettingsHelper.ParseXrandrOutput(SampleXrandrOutput);

        Assert.NotNull(display);
        Assert.Equal("DP-1", display.Name);
        Assert.Equal(1920, display.Width);
        Assert.Equal(1080, display.Height);
        Assert.Equal(60.0, display.RefreshRate);
        Assert.Contains("2560 × 1440", display.AvailableResolutions);
        Assert.Contains("1920 × 1080", display.AvailableResolutions);
        Assert.Contains("1280 × 720", display.AvailableResolutions);
        Assert.Equal("2560 × 1440", display.AvailableResolutions[0]);
    }

    [Theory]
    [InlineData("1920 × 1080", 1920, 1080, true)]
    [InlineData("2560x1440", 2560, 1440, true)]
    [InlineData("3840 X 2160", 3840, 2160, true)]
    [InlineData("invalid", 0, 0, false)]
    [InlineData("", 0, 0, false)]
    public void TryParseResolution_ValidatesAndParses(string input, int expectedW, int expectedH, bool expectedSuccess)
    {
        var success = DisplaySettingsHelper.TryParseResolution(input, out var w, out var h);
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(expectedW, w);
            Assert.Equal(expectedH, h);
        }
    }

    [Fact]
    public void FindBestModeForResolution_PicksClosestRefreshRate()
    {
        var modes = new List<DisplayModeInfo>
        {
            new("1", 2560, 1440, 60.0),
            new("2", 2560, 1440, 144.0),
            new("3", 2560, 1440, 180.0),
            new("4", 1920, 1080, 60.0),
            new("5", 1920, 1080, 180.0),
        };

        var bestMode = DisplaySettingsHelper.FindBestModeForResolution(modes, 2560, 1440, 175.0);

        Assert.NotNull(bestMode);
        Assert.Equal("3", bestMode.Id);
        Assert.Equal(180.0, bestMode.RefreshRate);
    }

    [Fact]
    public void BuildKScreenModeArgument_BuildsByIdOrGeometry()
    {
        var modeWithId = new DisplayModeInfo("42", 1920, 1080, 60.0);
        var argWithId = DisplaySettingsHelper.BuildKScreenModeArgument("eDP-1", modeWithId, 1920, 1080, 60.0);
        Assert.Equal("output.eDP-1.mode.42", argWithId);

        var modeWithoutId = new DisplayModeInfo("", 1920, 1080, 120.0);
        var argWithoutId = DisplaySettingsHelper.BuildKScreenModeArgument("eDP-1", modeWithoutId, 1920, 1080, 60.0);
        Assert.Equal("output.eDP-1.mode.1920x1080@120", argWithoutId);
    }

    [Fact]
    public async Task DetectPrimaryDisplayAsync_FallsBackToXrandrWhenKscreenDoctorFails()
    {
        var runner = new ScriptedCommandRunner(
            CommandResult.Failed("kscreen-doctor not found"),
            CommandResult.Success(SampleXrandrOutput));

        var display = await DisplaySettingsHelper.DetectPrimaryDisplayAsync(runner, waylandSession: false);

        Assert.NotNull(display);
        Assert.Equal("DP-1", display.Name);
        Assert.Equal(1920, display.Width);
        Assert.Equal(1080, display.Height);
    }
}

