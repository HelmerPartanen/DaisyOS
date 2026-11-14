using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Core.Search;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LauncherSearchTests
{
    [AvaloniaFact]
    public async Task RepeatedSearchKeepsRowsAndTypingDoesNotBlankResults()
    {
        var apps = new AppEntry[] { new("notes", "Notes", "", "", false), new("files", "Files", "", "", false) };
        var provider = new ApplicationSearchProvider();
        provider.ReplaceCatalog(apps);
        var service = new DaisySearchService(provider, new SearchSettingsRegistry());
        var model = new LauncherViewModel(null, apps, service);
        model.SearchText = "notes";
        await model.SearchReady;
        var row = Assert.Single(model.UniversalResults);
        var resets = 0;
        model.UniversalResults.CollectionChanged += (_, change) =>
        {
            if (change.Action == global::System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;
        };
        model.SearchText = "notes ";
        Assert.Same(row, Assert.Single(model.UniversalResults));
        await model.SearchReady;
        Assert.Same(row, Assert.Single(model.UniversalResults));
        Assert.Same(row, model.SelectedUniversalResult);
        model.SearchText = "f";
        var superseded = model.SearchReady;
        model.SearchText = "files";
        Assert.Same(row, Assert.Single(model.UniversalResults));
        await model.SearchReady;
        await superseded;
        Assert.Equal("files", Assert.Single(model.UniversalResults).Result.Id);
        Assert.Equal(0, resets);
    }

    [AvaloniaFact]
    public async Task CoordinatorUpdatesLauncherAndClearingQueryRestoresBrowsing()
    {
        var apps = new AppEntry[] { new("notes", "Notes", "", "", false), new("files", "Files", "", "", false) };
        var provider = new ApplicationSearchProvider();
        provider.ReplaceCatalog(apps);
        var registry = new SearchSettingsRegistry();
        var value = false;
        registry.Register(new("contrast", "High contrast", "Accessibility", ["readability"],
            _ => Task.FromResult(value), (requested, _) => { value = requested; return Task.CompletedTask; }));
        var service = new DaisySearchService(provider, registry);
        var model = new LauncherViewModel(null, apps, service);
        model.SearchText = "notes";
        await model.SearchReady;
        Assert.Equal("notes", model.ResolveSearchResult()?.App.Id);
        model.SearchText = "notes ";
        var previous = model.SearchReady;
        model.SearchText = "files";
        await model.SearchReady;
        await previous;
        Assert.Equal("files", model.ResolveSearchResult()?.App.Id);
        model.SearchText = "readability";
        Assert.Null(model.ResolveSearchResult());
        await model.SearchReady;
        Assert.Empty(model.FilteredApps);
        var setting = Assert.Single(model.SettingResults);
        Assert.False(model.ShowNoResults);
        await setting.ToggleAsync();
        Assert.True(value);
        Assert.True(setting.Value);
        value = false;
        registry.NotifyExternalChange("contrast");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        await setting.RefreshAsync();
        Assert.False(setting.Value);
        model.SearchText = "";
        await model.SearchReady;
        Assert.Equal(2, model.FilteredApps.Count);
        Assert.Empty(model.SettingResults);
        Assert.False(model.IsSearching);
    }

    [AvaloniaFact]
    public async Task FocusedSettingUsesSpaceWithoutActivatingAnApplication()
    {
        var value = false;
        var reject = false;
        var registry = new SearchSettingsRegistry();
        registry.Register(new("motion", "Reduced motion", "Accessibility", ["animation"],
            _ => Task.FromResult(value), (requested, _) =>
            {
                if (reject) throw new IOException("write failed");
                value = requested;
                return Task.CompletedTask;
            }));
        var service = new DaisySearchService(new ApplicationSearchProvider(), registry);
        var model = new LauncherViewModel(null, [], service);
        var launcher = new LauncherView(model);
        var window = new Window { Width = 900, Height = 700, Content = launcher };
        window.Show();
        try
        {
            model.SearchText = "animation";
            await model.SearchReady;
            window.UpdateLayout();
            var toggle = Assert.Single(launcher.GetVisualDescendants().OfType<ToggleSwitch>());
            toggle.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(value);
            Assert.True(Assert.Single(model.SettingResults).Value);
            reject = true;
            toggle.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(value);
            Assert.True(toggle.IsChecked);
            Assert.NotNull(Assert.Single(model.SettingResults).Error);
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task UnifiedCalculatorAndActionsAreKeyboardAccessibleAndCopyThroughClipboardBoundary()
    {
        var service = new DaisySearchService(new ApplicationSearchProvider(), new SearchSettingsRegistry(), [new CalculatorSearchProvider()]);
        var model = new LauncherViewModel(null, [], service);
        var launcher = new LauncherView(model);
        var window = new Window { Width = 900, Height = 700, Content = launcher };
        window.Show();
        try
        {
            string? copied = null;
            model.ClipboardWriter = value => { copied = value; return Task.CompletedTask; };
            model.SearchText = "2+3*4";
            await model.SearchReady;
            window.UpdateLayout();
            Assert.Equal("14", Assert.Single(model.UniversalResults).Title);
            Assert.False(model.ShowNoResults);
            Assert.True(model.ShowUnifiedSearch);
            launcher.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "SearchInput").Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal("14", copied);
            Assert.True(launcher.FindControl<ListBox>("UniversalSearchList")!.IsVisible);
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task SelectorUsesConfirmedValuesAndNumericSettingsHaveNoInlineEditor()
    {
        var registry = new SearchSettingsRegistry();
        var theme = "Dark";
        var size = 1d;
        registry.RegisterChoice(new("theme", "Theme", "Appearance", [], ["Light", "Dark", "Automatic"],
            _ => Task.FromResult(theme), (value, _) => { theme = value; return Task.CompletedTask; }));
        registry.RegisterNumber(new("size", "Taskbar size", "Desktop", [], 0, 2, 1,
            _ => Task.FromResult(size), (value, _) => { size = value; return Task.CompletedTask; }));
        var service = new DaisySearchService(new ApplicationSearchProvider(), registry);
        var model = new LauncherViewModel(null, [], service);
        model.SearchText = "theme";
        await model.SearchReady;
        var choice = Assert.Single(model.SettingResults);
        await choice.RefreshAsync();
        Assert.True(choice.IsChoice);
        Assert.Equal("Dark", choice.Choice);
        choice.Choice = "Automatic";
        Assert.Equal("Automatic", theme);
        model.SearchText = "size";
        await model.SearchReady;
        Assert.Empty(model.SettingResults);
        Assert.Empty(model.UniversalResults);
        Assert.Equal(1, size);
    }

    [AvaloniaFact]
    public async Task EnterActivatesTheFocusedResultAndNumericSettingsOpenTheirPage()
    {
        string? executed = null;
        double size = 1;
        var registry = new SearchSettingsRegistry();
        registry.RegisterNumber(new("size", "Size", "Desktop", [], 0, 2, 1, _ => Task.FromResult(size),
            (value, _) => { size = value; return Task.CompletedTask; }));
        var actions = new RegisteredActionSearchProvider([
            new("first", "Entry one", "Test", [], _ => { executed = "first"; return Task.CompletedTask; }),
            new("second", "Entry two", "Test", [], _ => { executed = "second"; return Task.CompletedTask; })]);
        var model = new LauncherViewModel(null, [], new DaisySearchService(new ApplicationSearchProvider(), registry, [actions,
            new SettingsPageSearchProvider([new("size-page", "Size", "Desktop", "Layout", "Taskbar size", [])],
                (_, _) => { executed = "size-page"; return Task.CompletedTask; })]));
        var launcher = new LauncherView(model);
        var window = new Window { Width = 900, Height = 700, Content = launcher };
        window.Show();
        try
        {
            model.SearchText = "entry";
            await model.SearchReady;
            window.UpdateLayout();
            var second = model.UniversalResults.Single(result => result.Result.Id == "second");
            launcher.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("SearchResultAction") && ReferenceEquals(button.DataContext, second)).Focus();
            model.SelectedUniversalResult = model.UniversalResults.Single(result => result.Result.Id == "first");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal("second", executed);
            model.SearchText = "size";
            await model.SearchReady;
            window.UpdateLayout();
            Assert.Empty(model.SettingResults);
            Assert.Empty(launcher.GetVisualDescendants().OfType<Slider>());
            var page = Assert.Single(model.UniversalResults);
            Assert.IsType<SettingsPageSearchPayload>(page.Result.Payload);
            launcher.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("SearchResultAction") && ReferenceEquals(button.DataContext, page)).Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal("size-page", executed);
            Assert.Equal(1, size);
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }

}
