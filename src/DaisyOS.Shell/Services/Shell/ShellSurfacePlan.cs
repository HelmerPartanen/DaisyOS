namespace DaisyOS.Shell.Services.Shell;

/// <summary>
/// Declarative production surface plan. KWin owns normal application windows;
/// DaisyOS owns only the layer surfaces listed here. The primary-only panel is
/// an explicit product decision, not an accidental first-output assumption.
/// </summary>
public static class ShellSurfacePlan
{
    // The visible bottom chrome is 52px high inside a 3px top/bottom safe
    // area. The wallpaper layer remains visible through both margins.
    public const int PrimaryPanelExclusiveZone = 58;
    public const int VerticalSystemBarWidth = 52;
    public static int PanelExtent(Appearance.DockEdge edge, int size, bool systemBarVisible) =>
        Math.Max(DockGeometry.IsVertical(edge) && systemBarVisible ? VerticalSystemBarWidth : 52,
            size == 2 ? 55 : size == 0 ? 44 : 52) + 6;

    // The Wayland panel buffer needs invisible room above the reserved 58px
    // workspace strip so 52px floating widgets can paint their elevation.
    // This does not alter KWin's exclusive zone or grow the visible chrome.
    public const int PrimaryPanelShadowBleed = 24;
    public const int PrimaryPanelSurfaceHeight = PrimaryPanelExclusiveZone + PrimaryPanelShadowBleed;
    public const int PrimaryPanelContentSafeInset = 3;

    // Temporary surfaces attach directly to the top of the reserved bottom
    // section. Right-aligned flyouts retain the desktop's 6px edge safe area.
    public const int OverlayBottomGap = 0;
    public const int OverlayEdgeSafeInset = 6;

    public static IReadOnlyList<LayerShellSurfaceOptions> Create(string primaryOutputName, IEnumerable<string> outputNames)
    {
        var outputs = outputNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).ToArray();
        if (outputs.Length == 0)
        {
            outputs = [primaryOutputName];
        }

        var surfaces = outputs.Select(name => LayerShellSurfaceOptions.Desktop(name) with
        {
            Namespace = "daisyos.wallpaper",
            InputPassthrough = true
        }).ToList();
        surfaces.Add(LayerShellSurfaceOptions.DesktopItems(primaryOutputName));
        surfaces.Add(LayerShellSurfaceOptions.PrimaryPanel(
            PrimaryPanelExclusiveZone,
            primaryOutputName,
            PrimaryPanelSurfaceHeight));
        surfaces.Add(LayerShellSurfaceOptions.LauncherOverlay(primaryOutputName));
        surfaces.Add(LayerShellSurfaceOptions.SystemFlyoutOverlay(primaryOutputName));
        return surfaces;
    }
}
