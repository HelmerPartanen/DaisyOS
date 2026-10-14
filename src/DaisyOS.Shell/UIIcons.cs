// Semantic HugeIcons SVG icon definitions. Values are logical DaisyIcon names;
// the generated resource bundle resolves them to Stroke Rounded SVG assets.
#nullable enable

namespace DaisyOS.Shell;

/// <summary>
/// Semantic HugeIcons SVG icon definitions used throughout the shell.
/// </summary>
public static class UIIcons
{
    private const string FilledSuffix = "-fill";

    /// <summary>Returns a legacy filled token; consumers normalize it to Stroke Rounded.</summary>
    public static string Filled(string icon) => IsFilled(icon) ? icon : $"{icon}{FilledSuffix}";

    /// <summary>Returns the icon's semantic name.</summary>
    public static string Regular(string icon) => IsFilled(icon)
        ? icon[..^FilledSuffix.Length]
        : icon;

    /// <summary>Splits an icon token into its logical name and fill variant.</summary>
    internal static (string Glyph, bool IsFilled) Parse(string? icon)
    {
        var token = icon ?? string.Empty;
        return IsFilled(token)
            ? (token[..^FilledSuffix.Length], true)
            : (token, false);
    }

    private static bool IsFilled(string icon) => icon?.EndsWith(FilledSuffix, StringComparison.Ordinal) == true;

    // General UI
    public const string Search = "magnifying-glass";
    public const string Add = "plus";
    public const string Close = "x";
    public const string Check = "check";
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "warning-circle";
    public const string Settings = "gear-six";
    public const string MoreHorizontal = "dots-three";
    public const string MoreVertical = "dots-three-vertical";
    public const string Copy = "copy";
    public const string Edit = "pencil-simple";
    public const string Delete = "trash";
    public const string Share = "share-network";
    public const string Filter = "funnel";

    // Navigation & Arrows
    public const string Back = "arrow-left";
    public const string Forward = "arrow-right";
    public const string Up = "arrow-up";
    public const string Down = "arrow-down";
    public const string ChevronLeft = "caret-left";
    public const string ChevronRight = "caret-right";
    public const string ChevronUp = "caret-up";
    public const string ChevronDown = "caret-down";
    public const string Refresh = "arrows-clockwise";

    // Settings Categories
    public const string Profile = "user-circle";
    public const string ProfileFill = Profile + FilledSuffix;
    public const string Appearance = "palette";
    public const string Accessibility = "person-arms-spread";
    public const string AccessibilityFill = Accessibility + FilledSuffix;
    public const string Sound = "speaker-high";
    public const string Network = "wifi-high";
    public const string Dock = "app-window";
    public const string Notifications = "bell";
    public const string Bell = Notifications;
    public const string Privacy = "shield";
    public const string Gaming = "game-controller";
    public const string Updates = "download-simple";
    public const string DateTime = "clock";
    public const string SystemInfo = "info";
    public const string DoNotDisturb = "bell-slash";

    // System Bar & Status
    public const string Wifi = "wifi-high";
    public const string WifiOff = "wifi-none";
    public const string Ethernet = "Ethernet";
    public const string Bluetooth = "bluetooth";
    public const string BluetoothOff = "bluetooth-slash";
    public const string VolumeHigh = "speaker-high";
    public const string VolumeLow = "speaker-low";
    public const string VolumeMute = "speaker-none";
    public const string VolumeOff = "speaker-slash";
    public const string BatteryFull = "battery-full";
    public const string BatteryCharging = "battery-charging";
    public const string BatteryAlert = "battery-warning";
    public const string Sleep = "moon";
    public const string SleepFill = Sleep + FilledSuffix;
    public const string Restart = "arrow-counter-clockwise";
    public const string ShutDown = "power";
    public const string User = "user";
    public const string AccountSettings = "users-three";
    public const string LockScreen = "lock";
    public const string ThemeDark = "moon";
    public const string ThemeLight = "sun";

    // File Manager & Launcher
    public const string Folder = "folder";
    public const string FolderOpen = "folder-open";
    public const string GridView = "squares-four";
    public const string ListView = "list";
    public const string Eye = "eye";
    public const string EyeOff = "eye-slash";
    public const string Pin = "push-pin";
    public const string PinOff = "push-pin-slash";
    public const string HardDrive = "hard-drives";
    public const string TextFormat = "text-t";
    public const string Document = "file-text";
    public const string Calculator = "calculator";
    public const string Code = "code";
    public const string Globe = "globe";
    public const string Music = "music-note";
    public const string Chat = "chat-circle";
    public const string School = "student";
    public const string Bank = "bank";
    public const string Play = "play";
    public const string Pause = "pause";
}
