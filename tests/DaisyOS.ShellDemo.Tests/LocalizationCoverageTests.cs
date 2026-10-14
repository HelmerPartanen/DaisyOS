using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Calculator;
using DaisyOS.Shell.Apps.Notes.Editor.Rich;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.DesktopWidgets;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.Shell.Views.Components.Desktop;
using DaisyOS.Shell.Views.Components.SystemBar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LocalizationCoverageTests
{
    [AvaloniaFact]
    public async Task InstalledDaisyAppsUseLocalizedNamesAndManagementMessages()
    {
        var resources = Application.Current!.Resources;
        try
        {
            Loc.Apply(resources, "fi-FI");
            var service = new DaisyOS.Shell.Services.InstalledApplicationService();
            var store = service.GetInstalledApplications().Single(app => app.Id == "org.daisyos.appstore.desktop");
            Assert.Equal(Loc.Get("App Store"), store.Name);
            Assert.NotEqual("Discover and install applications for DaisyOS", store.Description);
            var management = await service.ResolveAsync(store);
            Assert.Equal(Loc.Get("DaisyOS system applications are managed with system updates."), management.UnavailableReason);
            Assert.Equal("org.daisyos.appstore.desktop", store.Id);
        }
        finally { Loc.Apply(resources, "en-US"); }
    }

    [AvaloniaFact]
    public void OpenDesktopMenuAndSubmenusChangeLanguageWithoutReopening()
    {
        var resources = Application.Current!.Resources;
        Loc.Apply(resources, "en-US");
        var desktop = new DesktopView();
        var window = new Window { Width = 640, Height = 480, Content = desktop,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        var menu = desktop.FindControl<Grid>("InteractionSurface")!.ContextMenu!;
        try
        {
            menu.Open(desktop.FindControl<Grid>("InteractionSurface")!);
            Dispatcher.UIThread.RunJobs();
            var items = Flatten(menu.Items).ToArray();
            var originals = items.Select(item => Assert.IsType<string>(item.Header)).ToArray();
            Assert.Contains("Change Wallpaper", originals);
            Assert.Contains("New folder", originals);
            foreach (var tag in new[] { "fi-FI", "en-US", "fi-FI" })
            {
                Loc.Apply(resources, tag);
                Dispatcher.UIThread.RunJobs();
                Assert.True(menu.IsOpen);
                for (var index = 0; index < items.Length; index++)
                {
                    Assert.Equal(Loc.Get(originals[index]), items[index].Header);
                    if (tag == "fi-FI") Assert.NotEqual(originals[index], items[index].Header);
                }
            }
            if (TopLevel.GetTopLevel(menu) is TopLevel popup)
                using (var frame = popup.CaptureRenderedFrame()) frame?.Save("/tmp/daisy-desktop-menu-fi.png");
        }
        finally { menu.Close(); window.Close(); Loc.Apply(resources, "en-US"); }
    }

    [AvaloniaFact]
    public void EditorMenuCalculatorAndCalendarWidgetRefreshExistingInstances()
    {
        var resources = Application.Current!.Resources;
        Loc.Apply(resources, "en-US");
        var editor = new RichTextBlockEditor();
        var calendar = new CalendarWidget();
        var tiles = new DaisyOS.Shell.Views.Components.QuickSettings.QuickSettings();
        using var calculator = new CalculatorViewModel();
        var window = new Window { Width = 400, Height = 600,
            Content = new StackPanel { Children = { editor, calendar, tiles } } };
        window.Show();
        try
        {
            editor.ContextMenu!.Open(editor);
            Dispatcher.UIThread.RunJobs();
            var editItems = Flatten(editor.ContextMenu.Items).ToArray();
            var sources = editItems.Select(item => Assert.IsType<string>(item.Header)).ToArray();
            foreach (var tag in new[] { "fi-FI", "en-US" })
            {
                Loc.Apply(resources, tag);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Loc.Get("Standard"), calculator.ModeTitle);
                Assert.Equal(Loc.Get("Toggle Standard / Scientific"), calculator.ModeToggleTip);
                Assert.Equal(DateTime.Today.ToString("MMMM", Loc.Culture).ToUpper(Loc.Culture), calendar.FindControl<TextBlock>("MonthHeaderText")!.Text);
                for (var i = 0; i < editItems.Length; i++) Assert.Equal(Loc.Get(sources[i]), editItems[i].Header);
                var theme = tiles.FindControl<QuickSettingTile>("ThemeTile")!;
                var tip = theme.IsChecked ? "Use light theme" : "Use dark theme";
                Assert.Equal(Loc.Get(tip), theme.ToolTipText);
                if (tag == "fi-FI") Assert.NotEqual(tip, theme.ToolTipText);
            }
        }
        finally { editor.ContextMenu!.Close(); window.Close(); Loc.Apply(resources, "en-US"); }
    }

    [AvaloniaFact]
    public void FrameworkTextEditingMenuUsesTheSameLiveTranslations()
    {
        var resources = Application.Current!.Resources;
        Loc.Apply(resources, "en-US");
        var input = new TextBox { Text = "example", SelectionEnd = 7 };
        var window = new Window { Width = 300, Height = 100, Content = input };
        window.Show();
        var flyout = Assert.IsType<MenuFlyout>(input.ContextFlyout);
        try
        {
            flyout.ShowAt(input);
            Dispatcher.UIThread.RunJobs();
            var items = flyout.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(3, items.Length);
            foreach (var tag in new[] { "fi-FI", "en-US" })
            {
                Loc.Apply(resources, tag);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Loc.Get("Cut"), items[0].Header);
                Assert.Equal(Loc.Get("Copy"), items[1].Header);
                Assert.Equal(Loc.Get("Paste"), items[2].Header);
                Assert.Equal(Loc.Get("month"), resources["StringDatePickerMonthText"]);
            }
        }
        finally { flyout.Hide(); window.Close(); Loc.Apply(resources, "en-US"); }
    }

    [AvaloniaFact]
    public void SystembarCalendarUsesTheSharedGhostButton()
    {
        var bar = new SystemBarView();
        var button = bar.FindControl<Button>("CalendarButton")!;
        Assert.Contains("Ghost", button.Classes);
        Assert.DoesNotContain("Subtle", button.Classes);
    }

    private static IEnumerable<MenuItem> Flatten(Avalonia.Controls.ItemCollection items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in Flatten(item.Items)) yield return child;
        }
    }
}
