using DaisyOS.Shell.Services.Shell;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LayerShellSurfaceTests
{
    [Fact]
    public void Desktop_widgets_use_a_distinct_layer_above_wallpaper_without_reserving_space()
    {
        var wallpaper = LayerShellSurfaceOptions.Desktop("eDP-1");
        var desktop = LayerShellSurfaceOptions.DesktopItems("eDP-1");

        Assert.Equal(LayerShellSurfaceRole.Background, wallpaper.Role);
        Assert.Equal(LayerShellSurfaceRole.DesktopContent, desktop.Role);
        Assert.NotEqual(wallpaper.Namespace, desktop.Namespace);
        Assert.Equal(wallpaper.Anchor, desktop.Anchor);
        Assert.Equal("eDP-1", desktop.OutputName);
        Assert.Equal(LayerShellSurfaceOptions.IgnoreOtherExclusiveZones, desktop.ExclusiveZone);
        desktop.Validate();
    }

    [Fact]
    public void Desktop_surface_extends_beneath_reserved_panel_space()
    {
        var options = LayerShellSurfaceOptions.Desktop("HDMI-A-1");

        Assert.Equal(LayerShellSurfaceRole.Background, options.Role);
        Assert.Equal(LayerShellSurfaceOptions.IgnoreOtherExclusiveZones, options.ExclusiveZone);
        Assert.Equal(LayerShellAnchor.Top | LayerShellAnchor.Bottom | LayerShellAnchor.Left | LayerShellAnchor.Right, options.Anchor);
        options.Validate();
    }

    [Fact]
    public void Primary_panel_reserves_only_its_bottom_safe_area()
    {
        var options = LayerShellSurfaceOptions.PrimaryPanel(ShellSurfacePlan.PrimaryPanelExclusiveZone, "eDP-1");

        Assert.Equal(LayerShellSurfaceRole.Overlay, options.Role);
        Assert.Equal(LayerShellAnchor.Bottom | LayerShellAnchor.Left | LayerShellAnchor.Right, options.Anchor);
        Assert.Equal(58, options.ExclusiveZone);
        options.Validate();
    }

    [Fact]
    public void Fullscreen_edge_sensor_stays_above_games_without_taking_keyboard_focus()
    {
        var options = LayerShellSurfaceOptions.BottomEdgeSensor("eDP-1");
        Assert.Equal(LayerShellSurfaceRole.Overlay, options.Role);
        Assert.Equal(0, options.ExclusiveZone);
        Assert.Equal(2u, options.Height);
        Assert.Equal(LayerShellKeyboardInteractivity.None, options.KeyboardInteractivity);
        Assert.True(options.InputPassthrough);
    }

    [Fact]
    public void Native_overlay_spacing_does_not_enlarge_the_panel_reservation()
    {
        Assert.Equal(58, ShellSurfacePlan.PrimaryPanelExclusiveZone);
        Assert.Equal(0, ShellSurfacePlan.OverlayBottomGap);
        Assert.Equal(6, ShellSurfacePlan.OverlayEdgeSafeInset);
    }

    [Fact]
    public void Only_the_protocol_background_sentinel_may_be_negative()
    {
        var options = LayerShellSurfaceOptions.Desktop() with { ExclusiveZone = -2 };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Production_plan_keeps_launcher_above_other_transient_shell_surfaces()
    {
        var plan = ShellSurfacePlan.Create("eDP-1", ["eDP-1", "HDMI-A-1"]);

        Assert.Equal(2, plan.Count(surface => surface.Role == LayerShellSurfaceRole.Background));
        var panel = Assert.Single(plan, surface => surface.Namespace == "daisyos.primary-panel");
        Assert.Equal("eDP-1", panel.OutputName);

        var launcher = Assert.Single(plan, surface => surface.Namespace == "daisyos.overlay.launcher");
        var system = Assert.Single(plan, surface => surface.Namespace == "daisyos.overlay.system");
        Assert.Equal(LayerShellSurfaceRole.Overlay, launcher.Role);
        Assert.Equal(LayerShellSurfaceOptions.IgnoreOtherExclusiveZones, launcher.ExclusiveZone);
        Assert.Equal(LayerShellSurfaceRole.Overlay, system.Role);
    }

    [Fact]
    public void Normal_transient_surfaces_use_the_top_layer_so_the_launcher_is_unambiguously_frontmost()
    {
        var launcher = LayerShellSurfaceOptions.LauncherOverlay();
        var transient = LayerShellSurfaceOptions.TopTransient("daisyos.overlay.system");
        var toasts = LayerShellSurfaceOptions.NotificationOverlay();

        Assert.Equal(LayerShellSurfaceRole.Overlay, launcher.Role);
        Assert.Equal(LayerShellSurfaceRole.Panel, transient.Role);
        Assert.Equal(LayerShellSurfaceRole.Panel, toasts.Role);
        Assert.True(toasts.InputPassthrough);
    }

    [Theory]
    [InlineData("interface: 'zwlr_layer_shell_v1', version: 5", true)]
    [InlineData("interface: 'xdg_wm_base', version: 6", false)]
    [InlineData(null, false)]
    public void Capability_probe_requires_the_standard_layer_shell_global(string? registryDump, bool expected)
    {
        Assert.Equal(expected, LayerShellCapability.Supports(registryDump));
    }
}
