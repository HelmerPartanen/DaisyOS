using Avalonia;
using Avalonia.Headless.XUnit;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Services.Localization;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class SidebarLocalizationTests
{
    [AvaloniaFact]
    public void LanguageChangesUpdateStandardLabelsWithoutChangingPathsOrPinnedNames()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daisy-sidebar-language-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var window = new FilesWindow(directory, false);
        try
        {
            window.Show();
            var viewModel = (FilesViewModel)window.DataContext!;
            viewModel.PinnedLocations.Add(new SidebarLocation("My actual folder", directory, "folder", "ContentPrimaryBrush"));
            var paths = viewModel.StandardLocations.Select(location => location.Path).ToArray();
            Loc.Apply(Application.Current!.Resources, "fi-FI");
            Assert.Equal("Koti", viewModel.StandardLocations[0].Name);
            Assert.Equal("Lataukset", viewModel.StandardLocations.Single(location => location.TranslationSource == "Downloads").Name);
            Assert.Equal(paths, viewModel.StandardLocations.Select(location => location.Path));
            Assert.Equal("My actual folder", viewModel.PinnedLocations.Last().Name);
            Loc.Apply(Application.Current.Resources, "en-US");
            Assert.Equal("Home", viewModel.StandardLocations[0].Name);
            Assert.Equal(paths, viewModel.StandardLocations.Select(location => location.Path));
        }
        finally
        {
            Loc.Apply(Application.Current!.Resources, "en-US");
            window.Close();
            Directory.Delete(directory, true);
        }
    }
}
