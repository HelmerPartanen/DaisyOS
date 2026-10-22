using DaisyOS.Shell.Apps.Files;
using DaisyOS.System.Networking.LocalSend;
using Xunit;

namespace DaisyOS.Tests;

public class UserDirectoryPathsTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "daisy-user-dirs-" + Guid.NewGuid());
    public UserDirectoryPathsTests() => Directory.CreateDirectory(Path.Combine(_home, ".config"));
    public void Dispose() => Directory.Delete(_home, true);
    private string Config => Path.Combine(_home, ".config", "user-dirs.dirs");

    [Theory]
    [InlineData("Lataukset")]
    [InlineData("Téléchargements")]
    [InlineData("Descargas")]
    [InlineData("ダウンロード")]
    [InlineData("Загрузки")]
    public void DownloadsResolutionIsIndependentOfLanguage(string localizedName)
    {
        File.WriteAllText(Config, $"XDG_DOWNLOAD_DIR=\"$HOME/{localizedName}\"\n");

        var expected = Path.Combine(_home, localizedName);
        Assert.Equal(expected, DaisyOS.Core.Storage.UserDirectoryPaths.Load(_home, Config)["Downloads"]);
        Assert.Equal(expected, LocalSendService.ResolveDownloadsDirectory(_home, Config));
        Assert.Equal(expected, UserDirectoryPaths.Load(_home, Config)["Downloads"]);
    }

    [Fact]
    public void LocalizedAndAbsoluteFoldersKeepTheirActualPathsAndSemanticIcons()
    {
        var documents = Path.Combine(_home, "custom documents");
        File.WriteAllText(Config, $"XDG_DOWNLOAD_DIR=\"$HOME/Lataukset\"\nXDG_DESKTOP_DIR=\"${{HOME}}/Työpöytä\"\nXDG_DOCUMENTS_DIR=\"{documents}\"\n");
        var paths = UserDirectoryPaths.Load(_home);
        Assert.Equal(Path.Combine(_home, "Lataukset"), paths["Downloads"]);
        Assert.Equal(Path.Combine(_home, "Lataukset"), LocalSendService.ResolveDownloadsDirectory(_home, Config));
        Assert.Equal(Path.Combine(_home, "Työpöytä"), paths["Desktop"]);
        Assert.Equal(documents, paths["Documents"]);
        var presentation = FileLocationPresentation.Resolve(paths["Downloads"], _home);
        Assert.Equal(FileLocationKind.Downloads, presentation.Kind);
        Assert.Equal("download-simple", presentation.Icon);
    }

    [Fact]
    public void InvalidShellExpressionsAreNotEvaluatedAndAbsentFoldersHaveStableFallbacks()
    {
        File.WriteAllText(Config, "XDG_DOWNLOAD_DIR=\"$(touch /tmp/never-run)\"\nXDG_MUSIC_DIR=\"relative/folder\"\nXDG_PICTURES_DIR=\"$HOME\"\n");
        var paths = UserDirectoryPaths.Load(_home);
        Assert.Equal(Path.Combine(_home, "Downloads"), paths["Downloads"]);
        Assert.Equal(Path.Combine(_home, "Music"), paths["Music"]);
        Assert.Equal(_home, paths["Pictures"]);
        Assert.Equal(Path.Combine(_home, "Videos"), paths["Videos"]);
    }
}
