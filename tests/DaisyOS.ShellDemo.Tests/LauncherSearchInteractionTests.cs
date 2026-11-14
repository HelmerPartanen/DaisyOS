using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Search;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class LauncherSearchInteractionTests
{
    [AvaloniaFact]
    public async Task FailedInlineReadOffersSettingsActionAndRecoversWhenAvailable()
    {
        var available = false;
        var opened = false;
        var settings = new SearchSettingsRegistry();
        settings.Register(new("contrast", "High contrast", "Accessibility", [],
            _ => available ? Task.FromResult(false) : Task.FromException<bool>(new IOException("unavailable")),
            (_, _) => Task.CompletedTask));
        var result = new SearchResult("contrast", "settings", "High contrast", SearchResultType.Setting, 100,
            new SettingSearchPayload("contrast")) { Actions = [new("open", "settings-pages", "contrast-page", "Open in Settings")] };
        var setting = new SettingSearchResultViewModel(result, settings);
        var row = new UniversalSearchResultViewModel(result, setting, _ => { opened = true; return Task.CompletedTask; });
        await setting.RefreshAsync();
        Assert.False(row.HasSetting);
        Assert.True(row.CanActivate);
        await row.ActivateAsync();
        Assert.True(opened);
        available = true;
        await setting.RefreshAsync();
        Assert.True(row.HasSetting);
        Assert.False(row.CanActivate);
        Assert.Null(setting.Error);
    }

    [AvaloniaFact]
    public async Task ChoiceOpensWithPointerAndCommitsWithKeyboardWithoutChangingQuery()
    {
        var theme = "Dark";
        var settings = new SearchSettingsRegistry();
        settings.RegisterChoice(new("theme", "Theme", "Appearance", [], ["Light", "Dark", "Automatic"],
            _ => Task.FromResult(theme), (value, _) => { theme = value; return Task.CompletedTask; }));
        var model = new LauncherViewModel(null, [], new DaisySearchService(new ApplicationSearchProvider(), settings));
        var launcher = new LauncherView(model);
        var window = new Window { Width = 900, Height = 700, Content = launcher };
        window.Show();
        try
        {
            model.SearchText = "theme";
            await model.SearchReady;
            await Assert.Single(model.SettingResults).RefreshAsync();
            window.UpdateLayout();
            var choice = Assert.Single(launcher.GetVisualDescendants().OfType<ComboBox>());
            var point = choice.TranslatePoint(new Point(choice.Bounds.Width / 2, choice.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(choice.IsDropDownOpen);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.False(choice.IsDropDownOpen);
            Assert.Equal("Automatic", theme);
            Assert.Equal("Automatic", choice.SelectedItem);
            Assert.Equal("theme", model.SearchText);
            launcher.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "SearchInput").Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.True(choice.IsDropDownOpen);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(choice.IsDropDownOpen);
            Assert.Equal("theme", model.SearchText);
            Assert.Empty(launcher.GetVisualDescendants().OfType<Slider>());
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task LongResultsScrollWithWheelAndKeyboardWithoutPagingApplications()
    {
        var actions = new RegisteredActionSearchProvider(Enumerable.Range(0, 30).Select(index =>
            new RegisteredSearchAction($"entry-{index:00}", $"Entry {index:00}", "Test", [], _ => Task.CompletedTask)));
        var model = new LauncherViewModel(null, [], new DaisySearchService(new ApplicationSearchProvider(), new SearchSettingsRegistry(), [actions]));
        var launcher = new LauncherView(model);
        var window = new Window { Width = 640, Height = 480, Content = launcher };
        window.Show();
        try
        {
            model.SearchText = "entry";
            await model.SearchReady;
            window.UpdateLayout();
            var list = launcher.FindControl<ListBox>("UniversalSearchList")!;
            var scroll = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            var point = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(point, new Vector(0, -4));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(scroll.Offset.Y > 0);
            Assert.Equal(0, model.PageIndex);
            launcher.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "SearchInput").Focus();
            for (var index = 0; index < 6; index++)
            {
                window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
                window.UpdateLayout();
            }
            Assert.Same(model.UniversalResults.Last(), model.SelectedUniversalResult);
            Assert.True(scroll.Offset.Y > 0);
            Assert.Equal(0, model.PageIndex);
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }
}
