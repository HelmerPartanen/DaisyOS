using Avalonia;
using Avalonia.Controls;

namespace DaisyOS.Shell.Services.Shell;

/// <summary>
/// Full-output overlay root with a shared viewport clipped three pixels
/// above the reserved bottom chrome.
/// </summary>
public sealed class BottomChromeOverlayHost : Grid
{
    public Grid Viewport { get; }

    public BottomChromeOverlayHost()
    {
        ClipToBounds = false;
        Viewport = new Grid
        {
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 0, ShellSurfacePlan.PrimaryPanelExclusiveZone)
        };
        Children.Add(Viewport);
    }
}
