using DaisyOS.Shell.Apps.Files;
using Xunit;

namespace DaisyOS.Tests;

public sealed class FileLocationPresentationTests
{
    private const string Home = "/home/tester";

    [Theory]
    [InlineData("/", FileLocationKind.Root, "hard-drives")]
    [InlineData(Home, FileLocationKind.Home, "house")]
    [InlineData(Home + "/Desktop", FileLocationKind.Desktop, "desktop")]
    [InlineData(Home + "/Documents", FileLocationKind.Documents, "file-text")]
    [InlineData(Home + "/Downloads", FileLocationKind.Downloads, "download-simple")]
    [InlineData(Home + "/Pictures", FileLocationKind.Pictures, "image")]
    [InlineData(Home + "/Videos", FileLocationKind.Videos, "film-strip")]
    [InlineData(Home + "/Music", FileLocationKind.Music, "music-note")]
    [InlineData(Home + "/.local/share/Trash/files", FileLocationKind.Trash, "trash")]
    [InlineData(Home + "/projects/Music", FileLocationKind.Generic, "folder")]
    public void ResolveUsesCanonicalLocationRatherThanFolderName(string path, FileLocationKind kind, string icon)
    {
        var result = FileLocationPresentation.Resolve(path, Home);

        Assert.Equal(kind, result.Kind);
        Assert.Equal(icon, result.Icon);
        Assert.False(string.IsNullOrWhiteSpace(result.ColorResourceKey));
    }

    [Fact]
    public void ResolveMarksRemovableVolumesExplicitly()
    {
        var result = FileLocationPresentation.Resolve("/run/media/tester/Drive", Home, removable: true);

        Assert.Equal(FileLocationKind.Removable, result.Kind);
        Assert.Equal("hard-drives", result.Icon);
    }
}

public sealed class ClockTimingContractTests
{
    private static string RepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void SystemBarClockPollsWallClockEverySecond()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "DaisyOS.Shell", "Views", "Components", "SystemBar", "SystemBarView.axaml.cs"));

        Assert.Contains("_clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }", source);
        Assert.DoesNotContain("_clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) }", source);
    }

    [Fact]
    public void AnalogClockRendersFractionalSecondProgressSmoothly()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "DaisyOS.Shell", "DesktopWidgets", "ClockWidget.axaml.cs"));

        Assert.Contains("now.Second + now.Millisecond / 1000.0", source);
        Assert.Contains("DispatcherPriority.Render", source);
        Assert.Contains("Interval = TimeSpan.FromMilliseconds(16)", source);
    }
}

public sealed class FilesUiContractTests
{
    private static string RepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void FilesChromeUsesStableReusableStructure()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "DaisyOS.Shell", "Apps", "Files", "FilesWindow.axaml"));

        var viewModel = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "DaisyOS.Shell", "Apps", "Files", "FilesViewModel.cs"));

        Assert.Contains("controls:ToolbarSearchPopover", source);
        Assert.Contains("Classes=\"FilesBreadcrumb\"", source);
        Assert.Contains("CornerRadius\" Value=\"{DynamicResource RadiusControlInner}\"", source);
        Assert.Contains("x:Name=\"SidebarScrollViewer\"", source);
        Assert.Contains("Height=\"40\"", source);
        Assert.Contains("ItemsSource=\"{Binding StandardLocations}\"", source);
        Assert.Contains("Text=\"{DynamicResource " + DaisyOS.Shell.Services.Localization.Loc.Key("PINNED") + "}\"", source);
        Assert.Contains("ItemsSource=\"{Binding PinnedLocations}\"", source);
        Assert.Contains("Icon=\"pin-02\"", source);
        Assert.Contains("Click=\"OnUnpinLocationClicked\"", source);
        Assert.Contains("theming:ThemeResourceBrush.ForegroundKey=\"{Binding ColorResourceKey}\"", source);
        Assert.Contains("\"OrangeSwatchBrush\"", viewModel);
        Assert.Contains("Classes=\"PinnedLocationRow\"", source);
        Assert.Contains("Classes=\"Large SettingsNavItem PinnedLocationBody\"", source);
        Assert.Contains("<ContextMenu>", source);
        Assert.Contains("Border.PinnedLocationRow", source);
        Assert.Contains("Border.PinnedLocationRow:pointerover", source);
        Assert.Contains("Property=\"Background\" Value=\"Transparent\"", source);
        Assert.Contains("Background\" Value=\"{DynamicResource StateHoverBrush}\"", source);
        Assert.Contains("MinWidth=\"220\" Background=\"{DynamicResource SurfaceRaisedSubtleBrush}\"", source);
        Assert.Contains("Classes=\"ShellButton FilesHiddenToggle\"", source);
        Assert.Contains("ToggleButton.FilesHiddenToggle:checked", source);
        Assert.Contains("ToggleButton.FilesHiddenToggle:not(:checked):pointerover", source);
        Assert.Contains("Border.FilesToolbar Button /template/ ContentPresenter#PART_ContentPresenter", source);
        Assert.DoesNotContain("<Border Width=\"20\" Height=\"20\" CornerRadius=\"6\"", source);
        Assert.DoesNotContain("ACTION COMMAND STRIP", source);
        Assert.DoesNotContain("Classes=\"SidebarFrame\"", source);
        Assert.DoesNotContain("SearchTools.Width", source);
    }

    [Fact]
    public void SearchPopoverIsAnchoredBelowAFixedToolbarTrigger()
    {
        var xaml = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "DaisyOS.Shell", "Controls", "ToolbarSearchPopover.axaml"));
        var code = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "DaisyOS.Shell", "Controls", "ToolbarSearchPopover.axaml.cs"));

        Assert.Contains("Width=\"32\" Height=\"32\"", xaml);
        Assert.DoesNotContain("DoubleTransition Property=\"Width\"", xaml);
        Assert.Contains("<Popup x:Name=\"SearchPopup\"", xaml);
        Assert.Contains("Placement=\"AnchorAndGravity\"", xaml);
        Assert.Contains("Margin=\"16\"", xaml);
        Assert.Contains("controls:CartoucheBorder Width=\"280\" Height=\"50\"", xaml);
        Assert.Contains("TailHeight=\"6\" TailOnTop=\"True\" CornerRadius=\"{DynamicResource RadiusSurfaceInnerValue}\"", xaml);
        Assert.Contains("ShouldUseOverlayLayer=\"True\"", xaml);
        Assert.Contains("Background=\"{DynamicResource SurfaceOverlayBrush}\"", xaml);
        Assert.DoesNotContain("ContextMenuBackgroundBrush", xaml);
        Assert.DoesNotContain("ToolbarSearchPopupWindow", code);
        Assert.DoesNotContain("SearchTools", code);
    }
}
