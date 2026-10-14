using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.Services.Localization;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class FilesOperationLocalizationTests
{
    [AvaloniaFact]
    public async Task FileTransferAndRecoveryMessagesUseTheSelectedLanguage()
    {
        var originalLanguage = Loc.Culture.Name;
        var resources = new ResourceDictionary();
        var root = Path.Combine(Path.GetTempPath(), "daisy-transfer-language-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        using var tab = new FilesTabViewModel(root, navigateImmediately: false, watchFiles: false);
        try
        {
            foreach (var language in new[] { "fi-FI", "en-US" })
            {
                Loc.Apply(resources, language);
                var source = Path.Combine(root, language + ".txt");
                var destination = Path.Combine(root, language);
                Directory.CreateDirectory(destination);
                await File.WriteAllTextAsync(source, "test content");
                Assert.Equal(1, await tab.TransferFilesAsync([source], destination, move: false));
                Assert.Equal("test content", await File.ReadAllTextAsync(Path.Combine(destination, Path.GetFileName(source))));
                Assert.Equal(language == "fi-FI" ? "Kopioitu 1 kohde." : "Copied 1 item.", tab.OperationStatus);
                var error = FilesErrorMessages.Describe("Couldn’t open this folder", new UnauthorizedAccessException());
                Assert.Equal(language == "fi-FI"
                    ? "Kansiota ei voitu avata. Tarkista, että sinulla on oikeus käyttää näitä tiedostoja."
                    : "Couldn’t open this folder. Check that you have permission to access these files.", error);
            }
        }
        finally
        {
            Loc.Apply(resources, originalLanguage);
            Directory.Delete(root, recursive: true);
        }
    }
}
