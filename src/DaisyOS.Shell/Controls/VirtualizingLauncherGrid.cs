namespace DaisyOS.Shell.Controls;

public sealed class VirtualizingLauncherGrid : Apps.Files.VirtualizingFileGrid
{
    // A launcher page is already bounded. Its translated clipping viewport must
    // not recycle icons while that page is entering or leaving the screen.
    protected override bool RealizeEntirePage => true;
    protected override double CellWidth => 136;
    protected override double CellHeight => 128;
}
