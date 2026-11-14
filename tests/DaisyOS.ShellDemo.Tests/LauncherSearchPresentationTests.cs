using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DaisyOS.Core.Search;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class LauncherSearchPresentationTests
{
    [AvaloniaTheory]
    [InlineData(1280, 800, false)]
    [InlineData(1280, 800, true)]
    [InlineData(640, 480, false)]
    [InlineData(640, 480, true)]
    public async Task SearchUsesOneBoundedRoundedGroupWithInlineControlsAndVisibleActions(int width, int height, bool dark)
    {
        var registry = new SearchSettingsRegistry();
        var theme = "Light";
        registry.RegisterChoice(new("appearance.theme", "Theme", "Appearance", [], ["Light", "Dark", "Automatic"],
            _ => Task.FromResult(theme), (value, _) => { theme = value; return Task.CompletedTask; }));
        var provider = new FilesFixture();
        var model = new LauncherViewModel(null, [], new DaisySearchService(new ApplicationSearchProvider(), registry, [provider]));
        var launcher = new LauncherView(model);
        var window = new Window { Width = width, Height = height, Content = launcher,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            model.SearchText = "Theme";
            await model.SearchReady;
            window.UpdateLayout();
            Assert.Empty(launcher.GetVisualDescendants().OfType<ProgressBar>());
            var surface = launcher.FindControl<Border>("SearchResultsSurface")!;
            var rows = surface.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("SearchResultRow")).ToArray();
            Assert.Equal(3, rows.Length);
            Assert.Equal(new Avalonia.CornerRadius(14), surface.CornerRadius);
            Assert.True(surface.Bounds.Height <= 300, $"Grouped surface stretched to {surface.Bounds.Height}.");
            Assert.True(surface.Bounds.Width > 0);
            Assert.Equal(2, surface.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("SearchResultAction") && button.IsEffectivelyVisible));
            var selector = Assert.Single(surface.GetVisualDescendants().OfType<ComboBox>(), box => box.IsEffectivelyVisible);
            Assert.Equal("Light", selector.SelectedItem);
            Assert.True(selector.Bounds.Width >= 104 && selector.Bounds.Width <= 180);
            Assert.All(rows, row => Assert.True(row.Bounds.Height >= 60));
            Assert.All(rows, row => Assert.True(row.Bounds.Height <= 72, $"Unexpected blank row space: {row.Bounds.Height}."));
            var panel = launcher.FindControl<Border>("LauncherRootBorder")!;
            Assert.True(surface.Bounds.Width <= panel.Bounds.Width - 40);
            var artifactDirectory = Environment.GetEnvironmentVariable("DAISYOS_UX_ARTIFACTS");
            if (!string.IsNullOrWhiteSpace(artifactDirectory))
            {
                Directory.CreateDirectory(artifactDirectory);
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(artifactDirectory, $"search-{width}x{height}-{(dark ? "dark" : "light")}.png"),
                    Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            var firstFile = model.UniversalResults.Single(result => result.Result.Id == "documents/theme.svg");
            surface.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("SearchResultAction") && ReferenceEquals(button.DataContext, firstFile)).Focus();
            model.SelectedUniversalResult = firstFile;
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal("pictures/theme.svg", provider.OpenedId);
            // Reset navigation feedback for the neutral visual reference frame.
            foreach (var result in model.UniversalResults) result.IsKeyboardSelected = false;
            window.UpdateLayout();
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }
    private sealed class FilesFixture : ISearchProvider
    {
        public string? OpenedId;
        public string Id => "fixture-files";
        public SearchProviderCapabilities Capabilities => new("Fixture files", [SearchResultType.File], SearchStage.Extended, SupportsActions: true);
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SearchResult>>([
                Result("documents/theme.svg", "Documents · Design assets"), Result("pictures/theme.svg", "Pictures · Icons")]);
        private SearchResult Result(string id, string subtitle) => new(id, Id, "theme.svg", SearchResultType.File, 400, new FileSearchPayload(id, false))
        { Subtitle = subtitle, Actions = [new("open", Id, id, "Open")] };
        public Task ExecuteAsync(SearchAction action, CancellationToken cancellationToken) { OpenedId = action.EntityId; return Task.CompletedTask; }
    }
}
