using DaisyOS.Shell.Services.Shell;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class GamingOverlaySurfaceTests
{
    [Fact]
    public void Gaming_hud_uses_passive_overlay_above_fullscreen_without_reserving_space()
    {
        var options = LayerShellSurfaceOptions.GamingOverlay("DP-1");
        Assert.Equal(LayerShellSurfaceRole.Overlay, options.Role);
        Assert.Equal(LayerShellAnchor.Top | LayerShellAnchor.Right, options.Anchor);
        Assert.Equal(LayerShellKeyboardInteractivity.None, options.KeyboardInteractivity);
        Assert.True(options.InputPassthrough);
        Assert.Equal(-1, options.ExclusiveZone);
        Assert.Equal("DP-1", options.OutputName);
        Assert.Equal(340u, options.Width);
        Assert.Equal(88u, options.Height);
        options.Validate();
    }

}
