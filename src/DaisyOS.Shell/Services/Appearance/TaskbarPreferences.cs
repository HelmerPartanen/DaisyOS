namespace DaisyOS.Shell.Services.Appearance;

public enum DockEdge { Bottom, Left, Top, Right }
public enum DockAlignment { Start, Center, End }

public sealed record TaskbarPreferences
{
    public DockEdge Edge { get; init; } = DockEdge.Bottom;
    public DockAlignment Alignment { get; init; } = DockAlignment.Center;
    public bool ShowSystemBar { get; init; } = true;
    public bool ShowLauncherButton { get; init; } = true;
    public bool ShowWindowIndicators { get; init; } = true;
    public bool ShowWindowPreviews { get; init; } = true;
    public bool ShowTooltips { get; init; } = true;
    public bool LockIconOrder { get; init; }
    public int HideDelayMilliseconds { get; init; } = 1100;

    public TaskbarPreferences Normalize() => this with
    {
        Edge = Enum.IsDefined(Edge) ? Edge : DockEdge.Bottom,
        Alignment = Enum.IsDefined(Alignment) ? Alignment : DockAlignment.Center,
        HideDelayMilliseconds = Math.Clamp(HideDelayMilliseconds, 300, 5000)
    };
}
