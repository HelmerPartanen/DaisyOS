using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Apps.AppStore;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Notes;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Services.Theming;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class SidebarGeometryTests
{
    [AvaloniaTheory]
    [InlineData("Inter", 100)]
    [InlineData("Atkinson Hyperlegible", 100)]
    [InlineData("Noto Sans", 150)]
    [InlineData("DejaVu Serif", 150)]
    public async Task FourSidebarsShareFilesGeometryAndCenterLabels(string font, int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"daisy-sidebar-geometry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var resources = Application.Current!.Resources;
        TypographyPolicy.Apply(resources, font, scale);
        using var store = new AppStoreViewModel(new EmptyStore());
        var notes = new NotesViewModel(directory);
        notes.AddNote();
        var windows = new (string Name, Window Window)[]
        {
            ("files", new FilesWindow(directory, false)),
            ("settings", new Window { Content = new SettingsView() }),
            ("notes", new Window { Content = new NotesView { DataContext = notes } }),
            ("app-store", new Window { Content = new AppStoreView { DataContext = store } })
        };
        try
        {
            foreach (var (name, window) in windows)
            {
                window.Width = 1120;
                window.Height = 800;
                window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
                window.Show();
                await Task.Delay(250);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var sidebar = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NavigationSidebar");
                var buttons = sidebar.GetVisualDescendants().OfType<Button>()
                    .Where(b => b.Classes.Contains("SettingsNavItem") && b.IsEffectivelyVisible && b.Bounds.Height > 0).ToArray();
                Assert.NotEmpty(buttons);
                foreach (var button in buttons)
                {
                    Assert.Equal((double)resources["InputHeightStandard"]!, button.Bounds.Height, 1);
                    Assert.True(button.TryFindResource("RoundedSM", out var radius));
                    Assert.Equal((CornerRadius)radius!, button.CornerRadius);
                    var labels = button.GetVisualDescendants().OfType<TextBlock>()
                        .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text) && t.Bounds.Height > 0).ToArray();
                    Assert.Single(labels);
                    if (button.DataContext is NoteItem) Assert.NotNull(ToolTip.GetTip(button));
                    var label = labels[0];
                    var origin = label.TranslatePoint(default, button)!.Value;
                    var layout = label.TextLayout;
                    var inkCenter = origin.Y + layout.Height + layout.OverhangAfter - layout.Extent / 2;
                    Assert.InRange(inkCenter - button.Bounds.Height / 2, -1.1, 1.1);
                }
                Directory.CreateDirectory("/tmp/daisy-sidebar-geometry");
                using var frame = window.CaptureRenderedFrame();
                frame!.Save($"/tmp/daisy-sidebar-geometry/{name}-{font.Replace(' ', '-')}-{scale}.png");
                window.Close();
            }
        }
        finally
        {
            foreach (var (_, window) in windows) window.Close();
            TypographyPolicy.Apply(resources, TypographyPolicy.DefaultFamilyName, 100);
            Directory.Delete(directory, true);
        }
    }

    private sealed class EmptyStore : IAppStoreService
    {
        public Task<IReadOnlyList<AppStoreItem>> GetFeaturedAppsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AppStoreItem>>([]);
        public Task<IReadOnlyList<AppStoreItem>> GetAppsByCategoryAsync(AppCategory category, CancellationToken cancellationToken = default) => GetFeaturedAppsAsync(cancellationToken);
        public Task<IReadOnlyList<AppStoreItem>> SearchAppsAsync(string query, CancellationToken cancellationToken = default) => GetFeaturedAppsAsync(cancellationToken);
        public Task<IReadOnlyList<AppStoreItem>> GetInstalledAppsAsync(CancellationToken cancellationToken = default) => GetFeaturedAppsAsync(cancellationToken);
        public Task<bool> InstallAppAsync(string packageName, Action<double>? progressCallback = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> UninstallAppAsync(string packageName, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
