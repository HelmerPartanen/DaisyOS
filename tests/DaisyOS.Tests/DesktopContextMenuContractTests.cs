using System.IO;
using DaisyOS.Core.Desktop;
using DaisyOS.Shell.ViewModels;
using Xunit;

namespace DaisyOS.Tests;

public sealed class DesktopContextMenuContractTests
{
    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "DaisyOS.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return Directory.GetCurrentDirectory();
    }

    [Fact]
    public void WidgetSizeMenu_UsesSuppliedSemanticIconsAndPopupScopedStyles()
    {
        var root = FindRepoRoot();
        var item = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Controls", "DesktopItemView.axaml"));
        var overlay = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Views", "Components", "Desktop", "DesktopView.axaml"));
        var styles = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Styles", "DropdownMenuStyles.axaml"));
        var aliases = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "IconBundle.aliases.json"));

        Assert.Contains("Icon=\"widget_small\"", item);
        Assert.Contains("Icon=\"widget_medium\"", item);
        Assert.Contains("Icon=\"widget_large\"", item);
        Assert.Contains("Icon=\"{Binding ExtraLargeSizeIcon}\"", item);
        Assert.Contains("Classes=\"WidgetSizeIcon\"", item);
        Assert.Contains("Icon=\"widget_small\" Classes=\"WidgetSizeIcon\" Width=\"18\" Height=\"18\"", item);
        Assert.Contains("Icon=\"widget_medium\" Classes=\"WidgetSizeIcon\" Width=\"18\" Height=\"18\"", item);
        Assert.Contains("Icon=\"widget_large\" Classes=\"WidgetSizeIcon\" Width=\"26\" Height=\"18\"", item);
        Assert.Contains("Icon=\"{Binding ExtraLargeSizeIcon}\" Classes=\"WidgetSizeIcon\" Width=\"18\" Height=\"18\"", item);
        Assert.Contains("ContextMenu controls|DaisyIcon", styles);
        Assert.Contains("Button.WidgetSizeButton controls|DaisyIcon.WidgetSizeIcon", styles);
        Assert.DoesNotContain("Icon=\"widget_small\" Classes=\"WidgetSizeIcon\" Size=", item);
        Assert.Contains("Icon=\"widget_small\" Classes=\"WidgetSizeIcon\" Width=\"18\" Height=\"18\"", overlay);
        Assert.Contains("Icon=\"widget_medium\" Classes=\"WidgetSizeIcon\" Width=\"18\" Height=\"18\"", overlay);
        Assert.Contains("Icon=\"widget_large\" Classes=\"WidgetSizeIcon\" Width=\"26\" Height=\"18\"", overlay);
        Assert.Contains("Icon=\"{Binding ExtraLargeSizeIcon}\" Classes=\"WidgetSizeIcon\" Width=\"18\" Height=\"18\"", overlay);
        Assert.Contains("\"widget_small\": \"widget_small\"", aliases);
        Assert.Contains("\"widget_medium\": \"widget_medium\"", aliases);
        Assert.Contains("\"widget_large\": \"widget_large\"", aliases);
        Assert.Contains("\"widget_extralarge\": \"widget_extralarge\"", aliases);
    }

    [Fact]
    public void GrowingWidgetAtDesktopEdgeMovesItInwardWithoutOverflow()
    {
        var metrics = new DesktopGridMetrics(
            "test", new DaisyOS.Core.Desktop.Rect(0, 0, 1000, 600), 96, 100, 100);

        var result = DesktopViewModel.ClampWidgetOrigin(
            new GridCell(metrics.ColumnCount - 1, metrics.RowCount - 1), 4, 2, metrics);

        Assert.Equal(metrics.ColumnCount - 4, result.Column);
        Assert.Equal(metrics.RowCount - 2, result.Row);
        Assert.True(result.Column >= 0 && result.Column + 4 <= metrics.ColumnCount);
        Assert.True(result.Row >= 0 && result.Row + 2 <= metrics.RowCount);
    }

    [Fact]
    public void EveryDesktopWidgetUsesTheSharedBlurMaterialContainer()
    {
        var root = FindRepoRoot();
        var widgetTheme = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Themes", "WidgetContainerTheme.axaml"));
        var startup = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "App.axaml.cs"));

        Assert.Contains("TargetType=\"controls:WidgetContainer\"", widgetTheme);
        Assert.Contains("Background=\"{TemplateBinding Background}\"", widgetTheme);
        Assert.Contains("border.Name is \"OuterFrame\"", startup);

        foreach (var file in new[] { "ClockWidget.axaml", "DigitalClockWidget.axaml", "CalendarWidget.axaml", "MediaWidget.axaml", "ImageWidget.axaml" })
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "DesktopWidgets", file));
            Assert.Contains("<controls:WidgetContainer", source);
            Assert.DoesNotContain("Background=\"{DynamicResource ClockDialBrush}\"", source);
            Assert.DoesNotContain("Background=\"{DynamicResource CalendarBackgroundBrush}\"", source);
        }
    }

    [Fact]
    public void ImageWidgetOffersMediumLargeAndExtraLargePresentations()
    {
        var root = FindRepoRoot();
        var library = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Views", "Components", "Widgets", "WidgetLibraryOverlay.axaml"));
        var libraryCode = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Views", "Components", "Widgets", "WidgetLibraryOverlay.axaml.cs"));
        var desktop = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "ViewModels", "DesktopViewModel.cs"));

        Assert.Contains("PhotosPreviewsPanel", library);
        Assert.Contains("OnPhotosMediumPressed", libraryCode);
        Assert.Contains("OnPhotosLargePressed", libraryCode);
        Assert.Contains("OnPhotosExtraLargePressed", libraryCode);
        Assert.Contains("StartDragForPreview(\"image\", 3, 2", libraryCode);
        Assert.Contains("StartDragForPreview(\"image\", 4, 2", libraryCode);
        Assert.Contains("StartDragForPreview(\"image\", 5, 4", libraryCode);
        Assert.Contains("SupportsExtraLargeSize = true", desktop);
        Assert.Contains("IsSizeExtraLarge => IsMediaWidget ? RowSpan > 2 : ColumnSpan == 5 && RowSpan == 4", desktop);
    }

    [Fact]
    public void CalendarOffersOnlyMediumAndLargePresentations()
    {
        var root = FindRepoRoot();
        var library = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Views", "Components", "Widgets", "WidgetLibraryOverlay.axaml"));
        var libraryCode = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Views", "Components", "Widgets", "WidgetLibraryOverlay.axaml.cs"));
        var calendar = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "DesktopWidgets", "CalendarWidget.axaml.cs"));
        var desktop = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "ViewModels", "DesktopViewModel.cs"));

        Assert.DoesNotContain("SmallCalendarPreview", library);
        Assert.DoesNotContain("OnCalendarSmallPressed", libraryCode);
        Assert.DoesNotContain("SetGridSize(1, 1)", libraryCode);
        Assert.DoesNotContain("Small (1x1)", calendar);
        Assert.Contains("columnSpan = Math.Max(3, columnSpan)", calendar);
        Assert.Contains("SupportsSmallSize = false", desktop);
    }

    [Fact]
    public void MediaArtworkUsesOneClippedSurfaceWithoutDarkCornerShadow()
    {
        var root = FindRepoRoot();
        var media = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "DesktopWidgets", "MediaWidget.axaml"));

        Assert.Contains("Border#LargeArtworkFrame", media);
        Assert.Contains("CornerRadius\" Value=\"{DynamicResource RoundedLG}\"", media);
        Assert.DoesNotContain("BoxShadow=\"0 0 18 0 #50000000", media);
        Assert.DoesNotContain("CornerRadius=\"16\"", media);
    }

    [Fact]
    public void DesktopMenus_RequestSameFrameBlurRefresh()
    {
        var root = FindRepoRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "App.axaml.cs"));
        var desktop = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Views", "Components", "Desktop", "DesktopView.axaml.cs"));
        var item = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Controls", "DesktopItemView.axaml.cs"));

        Assert.Contains("ContextMenuVisualChanged += (_, _) => desktopBlur.RefreshImmediately()", app);
        Assert.Contains("desktopMenu.Opened", desktop);
        Assert.Contains("desktopMenu.Closed", desktop);
        Assert.Contains("contextMenu.Opened", item);
        Assert.Contains("contextMenu.Closed", item);
    }

    [Fact]
    public void DesktopPinUpdatesLiveTaskbarAndTaskbarMenuDoesNotOfferUninstall()
    {
        var root = FindRepoRoot();
        var desktopItem = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Controls", "DesktopItemView.axaml.cs"));
        var taskbar = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Views", "Components", "Taskbar", "TaskbarView.axaml.cs"));

        Assert.Contains("app.PinApplicationToTaskbar(appId)", desktopItem);
        Assert.DoesNotContain("UninstallAppRequested", taskbar);
        Assert.DoesNotContain("CreateMenuItem(\"Uninstall\"", taskbar);
    }

    [Fact]
    public void DesktopWidgetMenu_IsHostedOnNativeBlurSurface()
    {
        var root = FindRepoRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "App.axaml.cs"));
        var desktop = File.ReadAllText(Path.Combine(root, "src", "DaisyOS.Shell", "Views", "Components", "Desktop", "DesktopView.axaml"));

        Assert.Contains("DesktopWidgetContextMenuHost", desktop);
        Assert.Contains("DetachWidgetContextMenuForNativeOverlay", app);
        Assert.Contains("DesktopWidgetContextMenuHost\") }.OfType<Control>()", app);
    }
}
