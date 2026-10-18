using System.Diagnostics;
using DaisyOS.Shell.Services.Screenshot;
using SkiaSharp;
using Xunit;

namespace DaisyOS.Tests;

public sealed class ScreenshotCaptureTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"daisyos-capture-test-{Guid.NewGuid():N}");
    public ScreenshotCaptureTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(ScreenshotCaptureMode.Screen, "-m")]
    [InlineData(ScreenshotCaptureMode.Window, "-a")]
    [InlineData(ScreenshotCaptureMode.Area, "-m")]
    public void CaptureArgumentsAreNoninteractiveAndHonorCursor(ScreenshotCaptureMode mode, string flag)
    {
        var info = DaisyScreenshotService.CreateStartInfo("spectacle", new(mode, true), "/tmp/output with spaces.png");
        Assert.Contains(flag, info.ArgumentList);
        Assert.Contains("-p", info.ArgumentList);
        Assert.Contains("-b", info.ArgumentList);
        Assert.Contains("-n", info.ArgumentList);
        Assert.Contains("-i", info.ArgumentList);
        Assert.Equal("0", info.ArgumentList[info.ArgumentList.IndexOf("-d") + 1]);
        Assert.Contains("/tmp/output with spaces.png", info.ArgumentList);
        Assert.DoesNotContain("-p", DaisyScreenshotService.CreateStartInfo("spectacle", new(mode), "out").ArgumentList);
    }

    [Fact]
    public void DefaultDestinationIsPicturesNotDesktop() =>
        Assert.Equal("Screenshots", Path.GetFileName(DaisyScreenshotService.DefaultSaveDirectory()));

    [Fact]
    public void NativeAreaCapturesOnlyRequestedAreaWithoutInteractiveSelection()
    {
        var info = DaisyScreenshotService.CreateStartInfo("/usr/local/libexec/daisyos/daisy-screenshot-helper",
            new(ScreenshotCaptureMode.Area, true, -30, 12, 100, 80), "/tmp/capture.png");
        Assert.Equal(new[] { "--mode", "area", "--output", "/tmp/capture.png", "--include-cursor",
            "--x", "-30", "--y", "12", "--width", "100", "--height", "80" }, info.ArgumentList);
    }

    [Fact]
    public void NativeSnapshotUsesUncompressedBitmapTransfer()
    {
        var info = DaisyScreenshotService.CreateStartInfo("daisy-screenshot-helper", new(ScreenshotCaptureMode.Screen), "/tmp/freeze.bmp");
        Assert.Equal("bmp", info.ArgumentList[info.ArgumentList.IndexOf("--format") + 1]);
    }

    [Fact]
    public void NativeAreaUsesGlobalLogicalCoordinatesRatherThanScaledCropPixels()
    {
        var info = DaisyScreenshotService.CreateStartInfo("daisy-screenshot-helper",
            new(ScreenshotCaptureMode.Area, X: 200, Y: 100, Width: 600, Height: 400,
                LogicalArea: new(-1000, 50, 300, 200)), "out.png");
        Assert.Equal("-1000", info.ArgumentList[info.ArgumentList.IndexOf("--x") + 1]);
        Assert.Equal("300", info.ArgumentList[info.ArgumentList.IndexOf("--width") + 1]);
    }

    [Theory]
    [InlineData(ScreenshotCaptureMode.Screen)]
    [InlineData(ScreenshotCaptureMode.Window)]
    [InlineData(ScreenshotCaptureMode.Area)]
    public async Task CompletedImageDoesNotWaitForBackendExit(ScreenshotCaptureMode mode)
    {
        if (!OperatingSystem.IsLinux()) return;
        CreatePng(Path.Combine(_directory, "source.png"));
        var executable = CreateBackend("cp \"$(dirname \"$0\")/source.png\" \"$output\"\nexec sleep 30\n");
        var saveDirectory = Path.Combine(_directory, "saved");
        var service = new DaisyScreenshotService(executable, saveDirectory, TimeSpan.FromSeconds(5));
        var watch = Stopwatch.StartNew();
        var result = await service.CaptureAsync(new(mode, X: 4, Y: 6, Width: 12, Height: 10));
        Assert.True(result.Succeeded, result.Error);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(saveDirectory, Path.GetDirectoryName(result.Path));
        using var image = SKBitmap.Decode(result.Path);
        Assert.Equal(mode == ScreenshotCaptureMode.Area ? 12 : 64, image.Width);
        Assert.Equal(mode == ScreenshotCaptureMode.Area ? 10 : 32, image.Height);
        Assert.True(DaisyScreenshotService.IsCompletePng(result.Path!));
        var pid = int.Parse(File.ReadAllText(Path.Combine(_directory, "pid")));
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    [Fact]
    public async Task TimeoutKillsBackendAndDoesNotLeaveSavedFiles()
    {
        if (!OperatingSystem.IsLinux()) return;
        var backend = CreateBackend("exec sleep 30\n");
        var destination = Path.Combine(_directory, "saved");
        var result = await new DaisyScreenshotService(backend, destination, TimeSpan.FromMilliseconds(300))
            .CaptureActiveScreenAsync();
        Assert.False(result.Succeeded);
        Assert.Contains("timed out", result.Error);
        Assert.Empty(Directory.GetFiles(destination));
        var pid = int.Parse(File.ReadAllText(Path.Combine(_directory, "pid")));
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    [Fact]
    public async Task CancellationIsNotReportedAsTimeout()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var cancellation = new CancellationTokenSource(300);
        var result = await new DaisyScreenshotService(CreateBackend("exec sleep 30\n"), _directory, TimeSpan.FromSeconds(10))
            .CaptureActiveScreenAsync(cancellationToken: cancellation.Token);
        Assert.Equal("Screenshot cancelled.", result.Error);
    }

    [Fact]
    public void InvalidCropFailsInsteadOfSavingAnUncroppedImage()
    {
        var source = Path.Combine(_directory, "source.png");
        CreatePng(source);
        var destination = Path.Combine(_directory, "crop.png");
        Assert.Throws<InvalidOperationException>(() => DaisyScreenshotService.CropImage(source, destination,
            new(ScreenshotCaptureMode.Area, X: 60, Width: 10, Height: 10)));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void BitmapTransferWaitsForAllPixelBytes()
    {
        var path = Path.Combine(_directory, "transfer.bmp");
        var bytes = new byte[64];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        global::System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2), 64);
        global::System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), 54);
        File.WriteAllBytes(path, bytes[..54]);
        Assert.False(DaisyScreenshotService.IsCompleteImage(path));
        File.WriteAllBytes(path, bytes);
        Assert.True(DaisyScreenshotService.IsCompleteImage(path));
        Assert.False(DaisyScreenshotService.IsCompletePng(path));
    }

    private string CreateBackend(string body)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var path = Path.Combine(_directory, "backend.sh");
        File.WriteAllText(path, "#!/bin/sh\necho $$ > \"$(dirname \"$0\")/pid\"\noutput=''\nwhile [ $# -gt 0 ]; do\n" +
            " if [ \"$1\" = '-o' ]; then shift; output=\"$1\"; fi\n shift\ndone\n" + body);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static void CreatePng(string path)
    {
        using var bitmap = new SKBitmap(64, 32);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        png.SaveTo(file);
    }
    [Fact]
    public void SaveCroppedPngProducesCorrectCroppedDimensions()
    {
        using var bitmap = new SKBitmap(100, 80);
        bitmap.Erase(SKColors.Red);
        var dest = Path.Combine(_directory, "cropped.png");
        DaisyScreenshotService.SaveCroppedPng(bitmap, new SKRectI(10, 15, 60, 45), dest);
        Assert.True(File.Exists(dest));
        using var loaded = SKBitmap.Decode(dest);
        Assert.Equal(50, loaded.Width);
        Assert.Equal(30, loaded.Height);
    }

    [Fact]
    public void SaveCroppedPngThrowsWhenOutOfBounds()
    {
        using var bitmap = new SKBitmap(100, 80);
        var dest = Path.Combine(_directory, "invalid.png");
        Assert.Throws<InvalidOperationException>(() =>
            DaisyScreenshotService.SaveCroppedPng(bitmap, new SKRectI(0, 0, 120, 80), dest));
    }

    [Fact]
    public async Task CaptureScreenSnapshotAsyncReturnsTemporaryPath()
    {
        if (!OperatingSystem.IsLinux()) return;
        CreatePng(Path.Combine(_directory, "source.png"));
        var executable = CreateBackend("cp \"$(dirname \"$0\")/source.png\" \"$output\"\n");
        var service = new DaisyScreenshotService(executable, _directory, TimeSpan.FromSeconds(5));
        var tempPath = await service.CaptureScreenSnapshotAsync();
        Assert.NotNull(tempPath);
        Assert.True(File.Exists(tempPath));
        DaisyScreenshotService.DeleteFile(tempPath);
        Assert.False(File.Exists(tempPath));
    }

    public void Dispose() => Directory.Delete(_directory, true);
}

public sealed class ScreenshotLiveCaptureTests
{
    [LiveKWinFact]
    public async Task AllCaptureModesProduceActualKWinImages()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"daisyos-live-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var service = new DaisyScreenshotService(Environment.GetEnvironmentVariable("DAISYOS_TEST_SCREENSHOT_BACKEND") ?? "/usr/bin/spectacle", directory, TimeSpan.FromSeconds(15));
            foreach (var mode in Enum.GetValues<ScreenshotCaptureMode>())
            {
                var result = await service.CaptureWithoutFallbackAsync(new(mode, X: 10, Y: 10, Width: 100, Height: 100), CancellationToken.None);
                Assert.True(result.Succeeded, $"{mode}: {result.Error}");
                using var image = SKBitmap.Decode(result.Path);
                Assert.True(image.Width > 0 && image.Height > 0);
                if (mode == ScreenshotCaptureMode.Area)
                {
                    Assert.Equal(100, image.Width);
                    Assert.Equal(100, image.Height);
                }
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}

public sealed class LiveKWinFactAttribute : FactAttribute
{
    public LiveKWinFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DAISYOS_TEST_LIVE_CAPTURE") != "1")
            Skip = "Opt-in capture against a running KWin session.";
    }
}
