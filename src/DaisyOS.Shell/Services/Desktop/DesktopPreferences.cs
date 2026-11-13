using DaisyOS.Core.Desktop;

namespace DaisyOS.Shell.Services.Desktop;

/// <summary>Desktop behaviour shared by Settings and the running shell.</summary>
public sealed record DesktopPreferences
{
    public bool AutoArrange { get; init; }
    public DesktopAlignment Alignment { get; init; } = DesktopAlignment.Left;
    public bool FillGaps { get; init; } = true;
    public bool LockLayout { get; init; }
    public bool ShowItems { get; init; } = true;
    public bool ShowWidgets { get; init; } = true;
    public bool ShowTrash { get; init; } = true;
    public bool SingleClick { get; init; }
    public int IconSize { get; init; } = 48;

    public static DesktopPreferences Read(Func<string, string, string> get)
    {
        bool Flag(string key, bool fallback) => bool.TryParse(get("desktop." + key, fallback.ToString()), out var value) ? value : fallback;
        return new()
        {
            AutoArrange = Flag("auto-arrange", false), FillGaps = Flag("fill-gaps", true),
            LockLayout = Flag("lock-layout", false), ShowItems = Flag("show-items", true),
            ShowWidgets = Flag("show-widgets", true), ShowTrash = Flag("show-trash", true), SingleClick = Flag("single-click", false),
            Alignment = Enum.TryParse<DesktopAlignment>(get("desktop.alignment", "Left"), out var alignment) && Enum.IsDefined(alignment) ? alignment : DesktopAlignment.Left,
            IconSize = int.TryParse(get("desktop.icon-size", "48"), out var size) && size is 32 or 48 or 64 ? size : 48
        };
    }
}
