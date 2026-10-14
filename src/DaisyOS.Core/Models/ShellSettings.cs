namespace DaisyOS.Core.Models;

public sealed class ShellSettings
{
    public const string DefaultWallpaperUri = "solid:#2E3440";

    public const string DefaultProfilePictureUri = "";

    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;

    public AccentColor AccentColor { get; set; } = AccentColor.Blue;

    public bool HighContrast { get; set; }

    public bool ReduceMotion { get; set; }

    public double Volume { get; set; } = 72;

    public DockPosition DockPosition { get; set; } = DockPosition.Bottom;

    public List<SystemBarItem> VisibleSystemBarItems { get; set; } = SystemBarItemDefaults.All.ToList();

    public MediaWidgetSize MediaWidgetSize { get; set; } = MediaWidgetSize.Basic;

    public bool UseAdaptiveMediaTint { get; set; }

    public bool ShowDateInSystemBar { get; set; }

    public bool NotificationToastsEnabled { get; set; } = true;
    public bool NotificationSoundsEnabled { get; set; } = true;
    public bool SilenceNotificationsWhileGaming { get; set; } = true;

    public bool DoNotDisturb { get; set; }

    public bool GamingMode { get; set; }

    public bool SearchFileContents { get; set; }

    public bool AutomaticUpdateChecks { get; set; } = true;

    public List<UpdateCheckRecord> UpdateCheckHistory { get; set; } = [];

    public OnboardingPreferences OnboardingPreferences { get; set; } = new();

    public List<AppEntry> PinnedDockApps { get; set; } = [];

    public List<string> DockItemOrder { get; set; } = [];

    public List<AppEntry> RecentApps { get; set; } = [];
    public List<string> FavoriteAppIds { get; set; } = [];

    public List<FileManagerLocation>? PinnedFileManagerLocations { get; set; }

    public string WallpaperUri { get; set; } = DefaultWallpaperUri;

    public string ProfilePictureUri { get; set; } = DefaultProfilePictureUri;
}

public enum SystemBarItem
{
    Media,
    Search,
    Network,
    Bluetooth,
    Sound,
    Battery,
    Focus,
    Gaming,
    Theme,
    Notifications,
    User,
    Clock
}

public static class SystemBarItemDefaults
{
    public static readonly IReadOnlyList<SystemBarItem> All = Enum.GetValues<SystemBarItem>();
}

public enum MediaWidgetSize
{
    Compact,
    Basic,
    Large
}
