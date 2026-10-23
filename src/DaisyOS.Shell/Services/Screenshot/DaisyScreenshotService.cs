using System.Diagnostics;
using System.Buffers.Binary;
using SkiaSharp;

namespace DaisyOS.Shell.Services.Screenshot;

public enum ScreenshotCaptureMode { Screen, Window, Area }
public readonly record struct ScreenshotArea(int X, int Y, int Width, int Height);
public sealed record ScreenshotRequest(ScreenshotCaptureMode Mode, bool IncludeCursor = false,
    int X = 0, int Y = 0, int Width = 0, int Height = 0, ScreenshotArea? LogicalArea = null);

/// <summary>Uses the trusted native helper when updated, otherwise Spectacle's authorized KWin backend.</summary>
public sealed class DaisyScreenshotService
{
    private readonly string? _executable;
    private readonly bool _discoverBackend;
    private string? Executable => _discoverBackend ? FindCaptureBackend() : _executable;
    private readonly string _destination;
    private readonly TimeSpan _timeout;
    public DaisyScreenshotService() : this(null, DefaultSaveDirectory(), TimeSpan.FromSeconds(15)) => _discoverBackend = true;
    internal DaisyScreenshotService(string? executable, string destination, TimeSpan timeout)
    {
        _executable = executable;
        _destination = destination;
        _timeout = timeout;
    }

    internal static string DefaultSaveDirectory()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
            pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
        return Path.Combine(pictures, "Screenshots");
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, ScreenshotRequest request, string outputPath)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.Environment["XDG_MENU_PREFIX"] = "plasma-";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            info.Environment["QT_QPA_PLATFORM"] = "wayland";
        if (IsNativeHelper(executable))
        {
            foreach (var argument in new[] { "--mode", request.Mode.ToString().ToLowerInvariant(), "--output", outputPath })
                info.ArgumentList.Add(argument);
            if (Path.GetExtension(outputPath).Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                info.ArgumentList.Add("--format");
                info.ArgumentList.Add("bmp");
            }
            if (request.IncludeCursor) info.ArgumentList.Add("--include-cursor");
            if (request.Mode == ScreenshotCaptureMode.Area)
            {
                var area = request.LogicalArea ?? new ScreenshotArea(request.X, request.Y, request.Width, request.Height);
                foreach (var pair in new[] { ("--x", area.X), ("--y", area.Y), ("--width", area.Width), ("--height", area.Height) })
                {
                    info.ArgumentList.Add(pair.Item1);
                    info.ArgumentList.Add(pair.Item2.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            return info;
        }
        foreach (var argument in new[] { "-b", "-n", "-i", "-d", "0", "-o", outputPath })
            info.ArgumentList.Add(argument);
        info.ArgumentList.Add(request.Mode == ScreenshotCaptureMode.Window ? "-a" : "-m");
        if (request.IncludeCursor) info.ArgumentList.Add("-p");
        return info;
    }

    public async Task<ScreenshotCaptureResult> CaptureAsync(ScreenshotRequest request, CancellationToken cancellationToken = default)
    {
        var executable = Executable;
        var result = await CaptureCoreAsync(executable, request, cancellationToken);
        if (!result.Succeeded && executable is not null && IsNativeHelper(executable)
            && !cancellationToken.IsCancellationRequested && FindExecutable("spectacle") is { } fallback)
            return await CaptureCoreAsync(fallback, request, cancellationToken);
        return result;
    }

    internal Task<ScreenshotCaptureResult> CaptureWithoutFallbackAsync(ScreenshotRequest request, CancellationToken cancellationToken)
        => CaptureCoreAsync(Executable, request, cancellationToken);

    private async Task<ScreenshotCaptureResult> CaptureCoreAsync(string? executable, ScreenshotRequest request, CancellationToken cancellationToken)
    {
        if (executable is null)
            return ScreenshotCaptureResult.Failed("Screenshot capture is unavailable. Install or repair the DaisyOS screenshot helper.");
        if (request.Mode == ScreenshotCaptureMode.Area && (request.Width < 10 || request.Height < 10))
            return ScreenshotCaptureResult.Failed("Select an area at least 10 × 10 pixels.");
        string? outputPath = null;
        string? temporaryPath = null;
        var succeeded = false;
        try
        {
            Directory.CreateDirectory(_destination);
            outputPath = Path.Combine(_destination, $"Screenshot {DateTime.Now:yyyy-MM-dd HH.mm.ss.fff}-{Guid.NewGuid():N}.png");
            var useBitmapTransfer = !IsNativeHelper(executable) && request.Mode != ScreenshotCaptureMode.Window;
            temporaryPath = Path.Combine(Path.GetTempPath(), $"daisyos-capture-{Guid.NewGuid():N}.{(useBitmapTransfer ? "bmp" : "png")}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(IsNativeHelper(executable) && _timeout > TimeSpan.FromSeconds(3)
                ? TimeSpan.FromSeconds(3) : _timeout);
            var ct = timeout.Token;
            using var process = new Process { StartInfo = CreateStartInfo(executable, request, temporaryPath) };
            if (!process.Start()) return ScreenshotCaptureResult.Failed("Could not start the KWin capture backend.");
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            try
            {
                // Avoid Spectacle's expensive PNG encoding for opaque screen/area transfers.
                while (!IsCompleteImage(temporaryPath))
                {
                    ct.ThrowIfCancellationRequested();
                    if (process.HasExited)
                    {
                        if (IsCompleteImage(temporaryPath)) break;
                        var diagnostic = string.Join(" ", new[] { (await stderr).Trim(), (await stdout).Trim() }
                            .Where(value => value.Length > 0));
                        return ScreenshotCaptureResult.Failed($"KWin capture exited {process.ExitCode} without a complete image. {diagnostic}".Trim());
                    }
                    await Task.Delay(16, ct);
                }
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(stderr, stdout);
            }
            ct.ThrowIfCancellationRequested();
            if (request.Mode == ScreenshotCaptureMode.Area && !IsNativeHelper(executable))
                await Task.Run(() => CropImage(temporaryPath, outputPath, request), ct);
            else if (useBitmapTransfer)
                await Task.Run(() => ConvertImage(temporaryPath, outputPath), ct);
            else File.Move(temporaryPath, outputPath);
            succeeded = true;
            return ScreenshotCaptureResult.Success(outputPath);
        }
        catch (OperationCanceledException)
        {
            return ScreenshotCaptureResult.Failed(cancellationToken.IsCancellationRequested
                ? "Screenshot cancelled." : "KWin capture timed out without producing an image.");
        }
        catch (Exception ex) { return ScreenshotCaptureResult.Failed($"Screenshot failed: {ex.Message}"); }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
            if (!succeeded) DeleteTemporaryFile(outputPath);
        }
    }

    internal static bool IsCompletePng(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 45) return false;
            Span<byte> signature = stackalloc byte[8];
            stream.ReadExactly(signature);
            if (!signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
            stream.Seek(-12, SeekOrigin.End);
            Span<byte> end = stackalloc byte[12];
            stream.ReadExactly(end);
            return end.SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 });
        }
        catch (IOException) { return false; }
    }

    internal static bool IsCompleteImage(string path)
    {
        if (IsCompletePng(path)) return true;
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 54) return false;
            Span<byte> header = stackalloc byte[14];
            stream.ReadExactly(header);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header[2..6]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(header[10..14]);
            return header[0] == 'B' && header[1] == 'M' && length > offset && offset >= 54
                && length == stream.Length;
        }
        catch (IOException) { return false; }
    }

    public string GenerateDestinationPath()
    {
        Directory.CreateDirectory(_destination);
        return Path.Combine(_destination, $"Screenshot {DateTime.Now:yyyy-MM-dd HH.mm.ss.fff}-{Guid.NewGuid():N}.png");
    }

    public async Task<string?> CaptureScreenSnapshotAsync(bool includeCursor = false, CancellationToken cancellationToken = default)
    {
        var executable = Executable;
        var path = await CaptureScreenSnapshotCoreAsync(executable, includeCursor, cancellationToken);
        if (path is null && executable is not null && IsNativeHelper(executable) &&
            !cancellationToken.IsCancellationRequested && FindExecutable("spectacle") is { } fallback)
            return await CaptureScreenSnapshotCoreAsync(fallback, includeCursor, cancellationToken);
        return path;
    }

    private async Task<string?> CaptureScreenSnapshotCoreAsync(string? executable, bool includeCursor, CancellationToken cancellationToken)
    {
        if (executable is null) return null;

        var useBitmapTransfer = true; // Raw BMP avoids a PNG encode before the toolbar can open.
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"daisyos-freeze-{Guid.NewGuid():N}.{(useBitmapTransfer ? "bmp" : "png")}");

        try
        {
            var request = new ScreenshotRequest(ScreenshotCaptureMode.Screen, IncludeCursor: includeCursor);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(IsNativeHelper(executable) && _timeout > TimeSpan.FromSeconds(3)
                ? TimeSpan.FromSeconds(3) : _timeout);
            var ct = timeout.Token;

            using var process = new Process { StartInfo = CreateStartInfo(executable, request, temporaryPath) };
            if (!process.Start()) return null;

            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            try
            {
                while (!IsCompleteImage(temporaryPath))
                {
                    ct.ThrowIfCancellationRequested();
                    if (process.HasExited)
                    {
                        if (IsCompleteImage(temporaryPath)) break;
                        return null;
                    }
                    await Task.Delay(16, ct);
                }
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(stderr, stdout);
            }

            ct.ThrowIfCancellationRequested();
            return temporaryPath;
        }
        catch
        {
            DeleteTemporaryFile(temporaryPath);
            return null;
        }
    }

    private static void ConvertImage(string source, string destination)
    {
        using var bitmap = SKBitmap.Decode(source) ?? throw new InvalidOperationException("The captured image could not be decoded.");
        SavePng(bitmap, destination);
    }

    public static void SavePng(SKBitmap bitmap, string destination)
    {
        using var pixels = bitmap.PeekPixels();
        using var file = new FileStream(destination, FileMode.CreateNew);
        if (!pixels.Encode(file, new SKPngEncoderOptions(SKPngEncoderFilterFlags.Sub, 1)))
            throw new InvalidOperationException("The screenshot could not be encoded as PNG.");
    }

    public static void SaveCroppedPng(SKBitmap source, SKRectI bounds, string destination)
    {
        if (bounds.Left < 0 || bounds.Top < 0 || bounds.Right > source.Width || bounds.Bottom > source.Height)
            throw new InvalidOperationException("The selected area is outside the captured desktop.");
        using var cropped = new SKBitmap();
        if (!source.ExtractSubset(cropped, bounds))
            throw new InvalidOperationException("The selected area could not be cropped.");
        SavePng(cropped, destination);
    }

    internal static void CropImage(string source, string destination, ScreenshotRequest request)
    {
        using var bitmap = SKBitmap.Decode(source) ?? throw new InvalidOperationException("The captured PNG could not be decoded.");
        var bounds = new SKRectI(request.X, request.Y, checked(request.X + request.Width), checked(request.Y + request.Height));
        SaveCroppedPng(bitmap, bounds, destination);
    }

    public static void DeleteFile(string? path) => DeleteTemporaryFile(path);

    private static void DeleteTemporaryFile(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private static string? FindExecutable(string name)
        => (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
    private static bool IsNativeHelper(string executable) => Path.GetFileName(executable) == "daisy-screenshot-helper";
    private static string? FindCaptureBackend()
    {
        var localHelper = Path.Combine(AppContext.BaseDirectory, "daisy-screenshot-helper");
        if (File.Exists(localHelper)) return localHelper;

        // AppContext is src/DaisyOS.Shell/bin/<Configuration>/<TFM>. The native
        // helper is a sibling of DaisyOS.Shell under src/native, so only three
        // parent traversals are needed. The old path added a second `src` and
        // could never find a locally built helper.
        var sourceHelper = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../native/daisy_screenshot/build/daisy-screenshot-helper"));
        if (File.Exists(sourceHelper)) return sourceHelper;

        const string systemHelper = "/usr/local/libexec/daisyos/daisy-screenshot-helper";
        if (File.Exists(systemHelper)) return systemHelper;

        const string packagedSystemHelper = "/usr/libexec/daisyos/daisy-screenshot-helper";
        if (File.Exists(packagedSystemHelper)) return packagedSystemHelper;

        var pathHelper = FindExecutable("daisy-screenshot-helper");
        if (pathHelper is not null) return pathHelper;

        return FindExecutable("spectacle");
    }
    public Task<ScreenshotCaptureResult> CaptureActiveScreenAsync(bool includeCursor = false, CancellationToken cancellationToken = default)
        => CaptureAsync(new(ScreenshotCaptureMode.Screen, includeCursor), cancellationToken);
    public Task<ScreenshotCaptureResult> CaptureActiveWindowAsync(bool includeCursor = false, CancellationToken cancellationToken = default)
        => CaptureAsync(new(ScreenshotCaptureMode.Window, includeCursor), cancellationToken);
    public Task<ScreenshotCaptureResult> CaptureAreaAsync(int x, int y, int width, int height, bool includeCursor = false, CancellationToken cancellationToken = default)
        => CaptureAsync(new(ScreenshotCaptureMode.Area, includeCursor, x, y, width, height), cancellationToken);
}
public sealed record ScreenshotCaptureResult(bool Succeeded, string? Path, string? Error)
{
    public static ScreenshotCaptureResult Success(string path) => new(true, path, null);
    public static ScreenshotCaptureResult Failed(string error) => new(false, null, error);
}
