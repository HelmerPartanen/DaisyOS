namespace DaisyOS.Shell.Services.Shell;

/// <summary>
/// The compositor role of a DaisyOS-owned Wayland surface. These are deliberately
/// distinct from Avalonia's normal xdg_toplevel windows: assigning a layer role
/// after an xdg role has been created is a Wayland protocol error.
/// </summary>
public enum LayerShellSurfaceRole
{
    Background,
    Panel,
    Overlay,
    Toplevel,
    DesktopContent
}

[Flags]
public enum LayerShellAnchor
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8
}

public enum LayerShellKeyboardInteractivity
{
    None,
    OnDemand,
    Exclusive
}

/// <summary>
/// Immutable request passed to the DaisyOS-maintained Avalonia Wayland backend
/// before it creates a wl_surface. It is intentionally not an attached property
/// because changing a Wayland surface role after creation is invalid.
/// </summary>
public sealed record LayerShellSurfaceOptions(
    LayerShellSurfaceRole Role,
    string Namespace,
    LayerShellAnchor Anchor,
    int ExclusiveZone,
    LayerShellKeyboardInteractivity KeyboardInteractivity,
    string? OutputName = null,
    uint Width = 0,
    uint Height = 0,
    bool InputPassthrough = false)
{
    /// <summary>
    /// Layer-shell's sentinel for a surface that must extend beneath other
    /// surfaces' exclusive zones. This is essential for wallpaper/background
    /// content: a zero zone asks the compositor to move it above a panel,
    /// leaving the panel's reserved space unpainted.
    /// </summary>
    public const int IgnoreOtherExclusiveZones = -1;

    public static LayerShellSurfaceOptions Desktop(string? outputName = null) => new(
        LayerShellSurfaceRole.Background,
        "daisyos.desktop",
        LayerShellAnchor.Top | LayerShellAnchor.Bottom | LayerShellAnchor.Left | LayerShellAnchor.Right,
        IgnoreOtherExclusiveZones,
        LayerShellKeyboardInteractivity.None,
        outputName);

    public static LayerShellSurfaceOptions DesktopItems(string? outputName = null) =>
        Desktop(outputName) with
        {
            Role = LayerShellSurfaceRole.DesktopContent,
            Namespace = "daisyos.desktop-content"
        };

    public static LayerShellSurfaceOptions PrimaryPanel(
        int exclusiveZone,
        string? outputName = null,
        uint? surfaceHeight = null) => new(
        LayerShellSurfaceRole.Overlay,
        "daisyos.primary-panel",
        LayerShellAnchor.Bottom | LayerShellAnchor.Left | LayerShellAnchor.Right,
        exclusiveZone,
        LayerShellKeyboardInteractivity.OnDemand,
        outputName,
        Height: surfaceHeight ?? (uint)exclusiveZone);

    /// <summary>A narrow, non-reserving edge target used to reveal auto-hidden bottom bars.</summary>
    public static LayerShellSurfaceOptions BottomEdgeSensor(string? outputName = null) =>
        PrimaryPanel(0, outputName, 2) with
        {
            Namespace = "daisyos.primary-panel-edge-sensor",
            KeyboardInteractivity = LayerShellKeyboardInteractivity.None,
            InputPassthrough = true
        };

    /// <summary>
    /// Interactive shell cards cover the output but remain transparent outside
    /// their content. They use the overlay layer so detached launcher menus
    /// composite above the launcher while KWin samples it for blur.
    /// </summary>
    public static LayerShellSurfaceOptions SystemFlyoutOverlay(string? outputName = null) => new(
        LayerShellSurfaceRole.Overlay,
        "daisyos.overlay.system",
        LayerShellAnchor.Top | LayerShellAnchor.Bottom | LayerShellAnchor.Left | LayerShellAnchor.Right,
        IgnoreOtherExclusiveZones,
        LayerShellKeyboardInteractivity.OnDemand,
        outputName);

    // The launcher host already reserves the bottom chrome plus its 3px gap.
    // Use full-output geometry so KWin does not apply the panel zone a second time.
    public static LayerShellSurfaceOptions LauncherOverlay(string? outputName = null) =>
        Overlay("daisyos.overlay.launcher", outputName) with { ExclusiveZone = IgnoreOtherExclusiveZones };

    public static LayerShellSurfaceOptions Overlay(string surfaceNamespace, string? outputName = null) => new(
        LayerShellSurfaceRole.Overlay,
        surfaceNamespace,
        LayerShellAnchor.Top | LayerShellAnchor.Bottom | LayerShellAnchor.Left | LayerShellAnchor.Right,
        0,
        LayerShellKeyboardInteractivity.OnDemand,
        outputName);

    /// <summary>
    /// Lock screen overlay surface that completely covers the output and intercepts
    /// all keyboard input via compositor-level exclusive interactivity.
    /// </summary>
    public static LayerShellSurfaceOptions LockScreen(string? outputName = null, bool isSecondary = false) => new(
        LayerShellSurfaceRole.Overlay,
        "daisyos.overlay.lockscreen",
        LayerShellAnchor.Top | LayerShellAnchor.Bottom | LayerShellAnchor.Left | LayerShellAnchor.Right,
        IgnoreOtherExclusiveZones,
        isSecondary ? LayerShellKeyboardInteractivity.None : LayerShellKeyboardInteractivity.Exclusive,
        outputName);

    /// <summary>
    /// A transient shell surface that must appear above normal applications
    /// but below the launcher.  Keeping these on the protocol's <c>top</c>
    /// layer gives the launcher the sole <c>overlay</c> layer, so opening it
    /// is a compositor-level guarantee rather than an accidental creation
    /// order between several equally stacked surfaces.
    /// </summary>
    public static LayerShellSurfaceOptions TopTransient(string surfaceNamespace, string? outputName = null) => new(
        LayerShellSurfaceRole.Panel,
        surfaceNamespace,
        LayerShellAnchor.Top | LayerShellAnchor.Bottom | LayerShellAnchor.Left | LayerShellAnchor.Right,
        0,
        LayerShellKeyboardInteractivity.OnDemand,
        outputName);

    public static LayerShellSurfaceOptions PassiveOverlay(string surfaceNamespace, string? outputName = null) => new(
        LayerShellSurfaceRole.Overlay,
        surfaceNamespace,
        LayerShellAnchor.Top | LayerShellAnchor.Bottom | LayerShellAnchor.Left | LayerShellAnchor.Right,
        0,
        LayerShellKeyboardInteractivity.None,
        outputName,
        InputPassthrough: true);

    /// <summary>
    /// Toasts need input for their action and dismiss buttons, but only within
    /// their own small top-right footprint. This avoids an invisible full-
    /// output overlay intercepting pointer input from applications.
    /// </summary>
    public static LayerShellSurfaceOptions NotificationOverlay(string? outputName = null) => new(
        LayerShellSurfaceRole.Panel,
        "daisyos.overlay.toasts",
        LayerShellAnchor.Top | LayerShellAnchor.Right,
        0,
        LayerShellKeyboardInteractivity.None,
        outputName,
        Width: 420,
        Height: 460,
        // A notification host remains mapped while it has no cards. Its
        // native input region must therefore begin empty so it cannot cover
        // normal application title-bar controls in the upper-right output.
        InputPassthrough: true);

    /// <summary>A passive HUD above fullscreen applications, without reserving desktop space.</summary>
    public static LayerShellSurfaceOptions GamingOverlay(string? outputName = null) =>
        PassiveOverlay("daisyos.overlay.gaming", outputName) with
        {
            Anchor = LayerShellAnchor.Top | LayerShellAnchor.Right,
            ExclusiveZone = IgnoreOtherExclusiveZones,
            Width = 340,
            Height = 88
        };

    /// <summary>A compact, click-through hardware volume display above applications.</summary>
    public static LayerShellSurfaceOptions VolumeIndicator(string? outputName = null) =>
        PassiveOverlay("daisyos.overlay.volume", outputName) with
        {
            Anchor = LayerShellAnchor.Bottom,
            ExclusiveZone = IgnoreOtherExclusiveZones,
            Width = 368,
            Height = 160
        };

    /// <summary>Passive bottom labels never own pointer or keyboard input.</summary>
    public static LayerShellSurfaceOptions BottomTooltipOverlay(string? outputName = null) =>
        Overlay("daisyos.overlay.bottom-tooltips", outputName) with
        {
            Anchor = LayerShellAnchor.Bottom | LayerShellAnchor.Left | LayerShellAnchor.Right,
            Height = 160,
            ExclusiveZone = IgnoreOtherExclusiveZones,
            InputPassthrough = true,
            KeyboardInteractivity = LayerShellKeyboardInteractivity.None
        };

    public void Validate()
    {
        if (Role == LayerShellSurfaceRole.Toplevel)
        {
            throw new InvalidOperationException("Normal application windows must not request layer-shell options.");
        }

        if (string.IsNullOrWhiteSpace(Namespace))
        {
            throw new InvalidOperationException("Every DaisyOS layer surface needs a stable namespace.");
        }

        if (ExclusiveZone < IgnoreOtherExclusiveZones)
        {
            throw new InvalidOperationException("A layer-shell exclusive zone cannot be less than -1.");
        }
    }
}
