using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DaisyOS.Shell.Controls;

/// <summary>Lazy image decoding with bounded downloads, concurrency and a reusable disk cache.</summary>
public sealed class AsyncImage : Image
{
    public static readonly StyledProperty<string?> LocationProperty = AvaloniaProperty.Register<AsyncImage, string?>(nameof(Location));
    public static readonly StyledProperty<int> DecodeWidthProperty = AvaloniaProperty.Register<AsyncImage, int>(nameof(DecodeWidth), 128);
    public static readonly DirectProperty<AsyncImage, bool> HasImageProperty = AvaloniaProperty.RegisterDirect<AsyncImage, bool>(nameof(HasImage), image => image.HasImage);
    private bool _hasImage;
    public bool HasImage { get => _hasImage; private set => SetAndRaise(HasImageProperty, ref _hasImage, value); }
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Workers = new(4, 4);
    private CancellationTokenSource? _load;
    private Bitmap? _bitmap;
    private bool _attached;
    public string? Location { get => GetValue(LocationProperty); set => SetValue(LocationProperty, value); }
    public int DecodeWidth { get => GetValue(DecodeWidthProperty); set => SetValue(DecodeWidthProperty, value); }
    public AsyncImage()
    {
        // This control owns and disposes decoded images. A theme animation must
        // not retain its old Source beyond Reload or visual-tree detachment.
        Services.Theming.ThemeTransitionPolicy.SetEnabled(this, false);
    }
    static AsyncImage()
    {
        LocationProperty.Changed.AddClassHandler<AsyncImage>((image, _) => image.Reload());
        SourceProperty.Changed.AddClassHandler<AsyncImage>((image, _) => image.HasImage = image.Source is not null);
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); _attached = true; Reload(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { _attached = false; _load?.Cancel(); Source = null; _bitmap?.Dispose(); _bitmap = null; base.OnDetachedFromVisualTree(e); }
    private void Reload()
    {
        _load?.Cancel();
        Source = null; _bitmap?.Dispose(); _bitmap = null;
        if (!_attached || string.IsNullOrWhiteSpace(Location)) return;
        var request = new CancellationTokenSource(); _load = request;
        _ = LoadAsync(Location, Math.Clamp(DecodeWidth, 32, 1600), request);
    }
    private async Task LoadAsync(string location, int width, CancellationTokenSource request)
    {
        Bitmap? bitmap = null;
        try
        {
            await Workers.WaitAsync(request.Token);
            try
            {
                bitmap = await Task.Run(async () =>
                {
                    var path = location;
                    if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                    {
                        // Metadata assets are only fetched from Flathub's media host.
                        if (uri.Host != "dl.flathub.org" || !(uri.AbsolutePath.StartsWith("/media/", StringComparison.Ordinal)
                            || uri.AbsolutePath.StartsWith("/repo/screenshots/", StringComparison.Ordinal)
                            || uri.AbsolutePath.StartsWith("/repo/appstream/", StringComparison.Ordinal))) return null;
                        var directory = Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
                            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "daisyos", "store-images");
                        Directory.CreateDirectory(directory);
                        path = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(location))) + ".image");
                        if (!File.Exists(path))
                        {
                            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, request.Token);
                            response.EnsureSuccessStatusCode();
                            await using var input = await response.Content.ReadAsStreamAsync(request.Token);
                            using var buffer = new MemoryStream();
                            var bytes = new byte[16384]; int count;
                            while ((count = await input.ReadAsync(bytes, request.Token)) > 0)
                            {
                                if (buffer.Length + count > 8_000_000) throw new IOException("Image exceeds download limit.");
                                buffer.Write(bytes, 0, count);
                            }
                            var temporary = path + "." + Guid.NewGuid().ToString("N");
                            try { await File.WriteAllBytesAsync(temporary, buffer.ToArray(), request.Token); File.Move(temporary, path, overwrite: true); }
                            finally { if (File.Exists(temporary)) File.Delete(temporary); }
                            TrimCache(directory);
                        }
                    }
                    if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) return null;
                    using var stream = File.OpenRead(path);
                    return Bitmap.DecodeToWidth(stream, width);
                }, request.Token);
            }
            finally { Workers.Release(); }
            if (!request.IsCancellationRequested && ReferenceEquals(_load, request) && _attached)
            { _bitmap = bitmap; Source = bitmap; bitmap = null; }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or HttpRequestException or ArgumentException or NotSupportedException or UnauthorizedAccessException) { }
        finally { bitmap?.Dispose(); if (ReferenceEquals(_load, request)) _load = null; request.Dispose(); }
    }
    private static void TrimCache(string directory)
    {
        try
        {
            var files = new DirectoryInfo(directory).GetFiles("*.image").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            long total = 0;
            for (var i = 0; i < files.Length; i++)
            { total += files[i].Length; if (i >= 256 || total > 100_000_000) files[i].Delete(); }
        }
        catch (IOException) { }
    }
}
