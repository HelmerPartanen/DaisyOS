using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Services.Localization;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class FilesTabTitleTests
{
    [AvaloniaFact]
    public void TrashTitleIsLocalizedAndOrdinaryFilesFolderKeepsItsName()
    {
        var originalLanguage = Loc.Culture.Name;
        var resources = new ResourceDictionary();
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome))
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        using var tab = new FilesTabViewModel("/", navigateImmediately: false);
        var currentPath = typeof(FilesTabViewModel).GetProperty(nameof(FilesTabViewModel.CurrentPath))!;
        try
        {
            Loc.Apply(resources, "en-US");
            currentPath.SetValue(tab, Path.Combine(dataHome, "Trash", "files"));
            Assert.Equal("Trash", tab.Title);
            Loc.Apply(resources, "fi-FI");
            Assert.Equal("Roskakori", tab.Title);
            currentPath.SetValue(tab, Path.Combine(dataHome, "Trash", "files", "files"));
            Assert.Equal("files", tab.Title);
        }
        finally
        {
            Loc.Apply(resources, originalLanguage);
        }
    }
}
