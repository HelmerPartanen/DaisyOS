using System.Reflection;
using System.Text;
using Avalonia.Input;
using Avalonia;
using Avalonia.Skia;
using Avalonia.Platform.Storage;
using Avalonia.Wayland;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Files.Services;
using Xunit;

namespace DaisyOS.Tests;

public sealed class FileClipboardTests
{
    [Theory]
    [InlineData(ClipboardOperation.Copy, "copy", "0")]
    [InlineData(ClipboardOperation.Cut, "cut", "1")]
    public async Task MultipleFilesArePublishedAsFilesWithCompatibleOperationMetadata(
        ClipboardOperation operation, string action, string kdeFlag)
    {
        IStorageItem[] files = [new LocalItem("/tmp/a file.txt"), new LocalItem("/tmp/folder #one")];
        var transfer = ClipboardService.CreateTransfer(files, operation);
        Assert.Contains(DataFormat.File, transfer.Formats);
        Assert.DoesNotContain(DataFormat.Text, transfer.Formats);
        Assert.Equal(files, transfer.TryGetFiles());
        var (paths, readOperation) = await ClipboardService.ReadTransferAsync(transfer);
        Assert.Equal(new[] { "/tmp/a file.txt", "/tmp/folder #one" }, paths);
        Assert.Equal(operation, readOperation);

        var uris = Encoding.UTF8.GetString(await SerializeAsync(transfer, "text/uri-list"));
        Assert.Contains("file:///tmp/a%20file.txt", uris);
        Assert.Contains("file:///tmp/folder%20%23one", uris);
        Assert.Equal(kdeFlag, Encoding.UTF8.GetString(await SerializeAsync(transfer, "application/x-kde-cutselection")));
        Assert.StartsWith(action + "\n", Encoding.UTF8.GetString(await SerializeAsync(transfer, "x-special/gnome-copied-files")));
    }

    [Fact]
    public async Task TextPathsAndStaleInternalStateAreNotTreatedAsFiles()
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText("/tmp/a file.txt"));
        var (files, operation) = await ClipboardService.ReadTransferAsync(transfer);
        Assert.Empty(files);
        Assert.Equal(ClipboardOperation.None, operation);
    }

    [Fact]
    public async Task ExternalGnomeCutClipboardIsParsedWithUriEscapingAndRemoteFilesRejected()
    {
        var transfer = new DataTransfer();
        var item = new DataTransferItem();
        item.Set(ClipboardService.GnomeFilesFormat, Encoding.UTF8.GetBytes(
            "cut\nfile:///tmp/a%20file.txt\nfile://remote-host/tmp/private\nhttps://example.com/file\n"));
        transfer.Add(item);
        var (files, operation) = await ClipboardService.ReadTransferAsync(transfer);
        Assert.Equal(new[] { "/tmp/a file.txt" }, files);
        Assert.Equal(ClipboardOperation.Cut, operation);
    }

    [Fact]
    public async Task ExternalFileListDefaultsToCopy()
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateFile(new LocalItem("/tmp/file")));
        var (files, operation) = await ClipboardService.ReadTransferAsync(transfer);
        Assert.Single(files);
        Assert.Equal(ClipboardOperation.Copy, operation);
    }

    [Fact]
    public async Task BitmapClipboardSerializesAnActualPngNotAPath()
    {
        using var scope = AvaloniaLocator.EnterScope();
        SkiaPlatform.Initialize();
        using var source = new SkiaSharp.SKBitmap(16, 12);
        source.Erase(SkiaSharp.SKColors.CornflowerBlue);
        using var image = SkiaSharp.SKImage.FromBitmap(source);
        using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(png.ToArray());
        using var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DataFormat.Bitmap, bitmap));
        Assert.DoesNotContain(DataFormat.Text, transfer.Formats);
        var bytes = await SerializeAsync(transfer, "image/png");
        using var decoded = SkiaSharp.SKBitmap.Decode(bytes);
        Assert.Equal(16, decoded.Width);
        Assert.Equal(12, decoded.Height);
    }

    // Exercise the pinned backend's actual wire serializer, not only the shell's data object.
    private static async Task<byte[]> SerializeAsync(IAsyncDataTransfer transfer, string mime)
    {
        var implementation = typeof(LayerShellWindow).Assembly.GetType("Avalonia.Wayland.Clipboard.WaylandClipboardImpl")!;
        var method = implementation.GetMethod("SerializeForMimeAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var stream = new MemoryStream();
        await (Task)method.Invoke(null, [transfer, mime, stream])!;
        return stream.ToArray();
    }

    private sealed class LocalItem(string path) : IStorageItem
    {
        public string Name => global::System.IO.Path.GetFileName(path);
        public Uri Path { get; } = new(path);
        public bool CanBookmark => false;
        public void Dispose() { }
        public Task<StorageItemProperties> GetBasicPropertiesAsync() => Task.FromResult(new StorageItemProperties());
        public Task<string?> SaveBookmarkAsync() => Task.FromResult<string?>(null);
        public Task<IStorageFolder?> GetParentAsync() => Task.FromResult<IStorageFolder?>(null);
        public Task DeleteAsync() => throw new NotSupportedException();
        public Task<IStorageItem?> MoveAsync(IStorageFolder destination) => throw new NotSupportedException();
    }
}

public sealed class FilesMultiMoveTests
{
    [Fact]
    public async Task TransferMovesEveryFileAndFolderInTheSelectionAndPreservesConflicts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"daisyos-move-test-{Guid.NewGuid():N}");
        var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
        try
        {
            File.WriteAllText(Path.Combine(source, "one.txt"), "one");
            File.WriteAllText(Path.Combine(source, "two.txt"), "two");
            Directory.CreateDirectory(Path.Combine(source, "folder"));
            File.WriteAllText(Path.Combine(source, "folder", "nested.txt"), "nested");
            File.WriteAllText(Path.Combine(source, "conflict.txt"), "source");
            File.WriteAllText(Path.Combine(destination, "conflict.txt"), "destination");
            using var tab = new FilesTabViewModel(destination, navigateImmediately: false, uiDispatcher: new ImmediateFilesDispatcher(), watchFiles: false);
            await tab.NavigateToAsync(destination);
            var paths = new[] { "one.txt", "two.txt", "folder", "conflict.txt" }.Select(name => Path.Combine(source, name));
            var count = await tab.TransferFilesAsync(paths, destination, move: true);
            Assert.Equal(3, count);
            Assert.Equal("one", File.ReadAllText(Path.Combine(destination, "one.txt")));
            Assert.Equal("two", File.ReadAllText(Path.Combine(destination, "two.txt")));
            Assert.Equal("nested", File.ReadAllText(Path.Combine(destination, "folder", "nested.txt")));
            Assert.False(File.Exists(Path.Combine(source, "one.txt")));
            Assert.False(File.Exists(Path.Combine(source, "two.txt")));
            Assert.False(Directory.Exists(Path.Combine(source, "folder")));
            Assert.Equal("source", File.ReadAllText(Path.Combine(source, "conflict.txt")));
            Assert.Equal("destination", File.ReadAllText(Path.Combine(destination, "conflict.txt")));
        }
        finally { Directory.Delete(root, true); }
    }
}
