using DaisyOS.Core.Search;

namespace DaisyOS.Core.Tests.Search;

public class CalculatorAndQueryTests
{
    [Fact]
    public async Task EquivalentInlineSettingReplacesPageAndUnavailableSettingRestoresFallback()
    {
        var registry = new SearchSettingsRegistry();
        registry.RegisterChoice(new("theme", "Theme", "Appearance", [], ["Light", "Dark"],
            _ => Task.FromResult("Light"), (_, _) => Task.CompletedTask));
        var entries = new SearchableSettingsPage[]
        {
            new("theme-page", "Theme", "Appearance", "", "Appearance theme", []) { InlineSettingId = "theme" }
        };
        string? opened = null;
        var pages = new SettingsPageSearchProvider(entries, (page, _) => { opened = page.Id; return Task.CompletedTask; }, registry);
        var inline = new SettingsSearchProvider(registry, entries);
        var query = SearchQuery.Parse("theme");
        Assert.Empty(await pages.SearchAsync(query, default));
        var result = Assert.Single(await inline.SearchAsync(query, default));
        await pages.ExecuteAsync(Assert.Single(result.Actions), default);
        Assert.Equal("theme-page", opened);
        registry.Unregister("theme");
        Assert.Empty(await inline.SearchAsync(query, default));
        Assert.IsType<SettingsPageSearchPayload>(Assert.Single(await pages.SearchAsync(query, default)).Payload);
    }

    [Fact]
    public async Task NumericSettingsUsePagesAndDistinctNetworkTasksRemainAvailable()
    {
        var registry = new SearchSettingsRegistry();
        registry.RegisterNumber(new("size", "Taskbar size", "Taskbar", [], 0, 2, 1,
            _ => Task.FromResult(1d), (_, _) => Task.CompletedTask));
        registry.Register(new("wifi", "Wi-Fi", "Network", ["wifi"], _ => Task.FromResult(true), (_, _) => Task.CompletedTask));
        var entries = new SearchableSettingsPage[]
        {
            new("size-page", "Taskbar size", "Taskbar", "Layout", "Size", []) { InlineSettingId = "size" },
            new("wifi-page", "Wi-Fi", "Network", "Wi-Fi", "Networks", ["wifi"])
            { InlineSettingId = "wifi", SearchTitle = "Wi-Fi networks", HasAdditionalTasks = true }
        };
        var pages = new SettingsPageSearchProvider(entries, (_, _) => Task.CompletedTask, registry);
        var inline = new SettingsSearchProvider(registry, entries);
        Assert.Empty(await inline.SearchAsync(SearchQuery.Parse("size"), default));
        Assert.Single(await pages.SearchAsync(SearchQuery.Parse("size"), default));
        Assert.Single(await inline.SearchAsync(SearchQuery.Parse("wifi"), default));
        Assert.Equal("Wi-Fi networks", Assert.Single(await pages.SearchAsync(SearchQuery.Parse("wifi"), default)).Title);
    }

    [Theory]
    [InlineData("2 + 3 * 4", "14")]
    [InlineData("(2 + 3) * 4", "20")]
    [InlineData("2^3^2", "512")]
    [InlineData("-2^2", "-4")]
    [InlineData("0.1+0.2", "0.3")]
    [InlineData("7%3", "1")]
    public void CalculatesBoundedArithmetic(string expression, string expected)
    { Assert.True(CalculatorSearchProvider.TryCalculate(expression, out var actual)); Assert.Equal(expected, actual); }
    [Theory]
    [InlineData("1/0")]
    [InlineData("2^99999")]
    [InlineData("system('touch /tmp/bad')")]
    [InlineData("2..3+4")]
    [InlineData("1 2")]
    [InlineData("(1+2")]
    public void RejectsInvalidOrExecutableExpressions(string expression) => Assert.False(CalculatorSearchProvider.TryCalculate(expression, out _));
    [Theory]
    [InlineData("firefox", "fierfox", true)]
    [InlineData("settings", "setings", true)]
    [InlineData("calculator", "calclator", true)]
    [InlineData("firefox", "xyzabc", false)]
    [InlineData("files", "fli", false)]
    public void FuzzyMatchingHasABoundedOneEditThreshold(string title, string query, bool matches)
    { Assert.Equal(matches, SearchRanker.Score(new(title, ""), SearchQuery.Parse(query)) > 0); }
    [Fact]
    public async Task TypedSettingsValidateChoicesBoundsAndConfirmedValues()
    {
        var registry = new SearchSettingsRegistry();
        var theme = "Dark";
        var size = 1d;
        registry.RegisterChoice(new("theme", "Theme", "Appearance", [], ["Dark", "Light", "Automatic"],
            _ => Task.FromResult(theme), (value, _) => { theme = value; return Task.CompletedTask; }));
        registry.RegisterNumber(new("size", "Size", "Desktop", [], 0, 2, 1,
            _ => Task.FromResult(size), (value, _) => { size = value; return Task.CompletedTask; }));
        Assert.Null((await registry.SetValueAsync("theme", new ChoiceSettingValue("Automatic"))).Error);
        Assert.Equal("Automatic", theme);
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetValueAsync("theme", new ChoiceSettingValue("Execute command")));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetValueAsync("size", new NumberSettingValue(double.NaN)));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetValueAsync("size", new NumberSettingValue(3)));
        await registry.SetValueAsync("size", new NumberSettingValue(2));
        Assert.Equal(2, size);
        Assert.Throws<ArgumentException>(() => registry.Register(new("theme", "Duplicate", "", [], _ => Task.FromResult(false), (_, _) => Task.CompletedTask)));
    }
    [Fact]
    public void ParsesSafeFileFiltersWithoutIncludingOperatorsInTerms()
    {
        var query = SearchQuery.Parse("report type:pdf size:>100MB in:\"My Documents\" kind:file");
        Assert.Equal("REPORT", query.NormalizedText);
        Assert.Equal(".pdf", query.FileFilters.Extension);
        Assert.Equal(104857600, query.FileFilters.MinimumSize);
        Assert.Equal("My Documents", query.FileFilters.Location);
        Assert.False(query.FileFilters.IsDirectory);
    }
    [Fact]
    public async Task FilterOnlyQueryPublishesFinalSnapshotWithNoMatchingProviders()
    {
        using var session = new SearchCoordinator([new CalculatorSearchProvider()]).CreateSession();
        var snapshots = new List<SearchSnapshot>();
        await foreach (var snapshot in session.SearchAsync("kind:file")) snapshots.Add(snapshot);
        Assert.True(Assert.Single(snapshots).IsComplete);
    }
    [Fact]
    public async Task ActionsExecuteOnlyRegisteredIdsAndSettingsPagesNavigateExplicitTargets()
    {
        var count = 0;
        var provider = new RegisteredActionSearchProvider([new("lock", "Lock screen", "Session", ["privacy"], _ => { count++; return Task.CompletedTask; })]);
        var result = Assert.Single(await provider.SearchAsync(SearchQuery.Parse("privacy"), default));
        await provider.ExecuteAsync(result.Actions[0], default);
        Assert.Equal(1, count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ExecuteAsync(new("execute", "actions", "bad", "Bad"), default));
        string? target = null;
        var pages = new SettingsPageSearchProvider([new("display", "Resolution", "Displays", "Display", "Screen size", ["monitor"])],
            (page, _) => { target = page.Category; return Task.CompletedTask; });
        await pages.ExecuteAsync(Assert.Single(await pages.SearchAsync(SearchQuery.Parse("monitor"), default)).Actions[0], default);
        Assert.Equal("Displays", target);
    }
}
