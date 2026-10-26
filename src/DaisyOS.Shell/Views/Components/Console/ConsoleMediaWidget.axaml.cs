using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Wallpaper;
using DaisyOS.Shell.ViewModels;
using DaisyOS.System.Audio;
using DaisyOS.System.Media;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Views.Components.Console;

/// <summary>Bottom-row media control with MPRIS updates and a real output-spectrum visualizer.</summary>
public partial class ConsoleMediaWidget : UserControl
{
    private const int SpectrumColumns = 6;
    private const int MaxInlineArtworkBytes = 4 * 1024 * 1024;
    private static readonly HttpClient ArtworkHttpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly IMediaSessionService _mediaService = LinuxMediaSessionService.Shared;
    private readonly DispatcherTimer _spectrumTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly List<Border> _spectrumBars = [];
    private readonly double[] _spectrumSnapshot = new double[SpectrumColumns];
    private CancellationTokenSource? _refreshCancellation;
    private IAudioSpectrumService? _audioSpectrumService;
    private Bitmap? _artwork;
    private string? _artworkPath;
    private Color? _artworkTint;
    private bool _isPlaying;
    private bool _spectrumIsIdle = true;
    private string? _trackKey;
    private string? _resolvedArtworkPath;

    public ConsoleMediaWidget()
    {
        InitializeComponent();
        Loc.RefreshOnChange(this, () => _ = RefreshMediaAsync());
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _ = new DaisyOS.Shell.Services.Diagnostics.VisualActivityGate(this, active =>
        {
            if (active && _isPlaying) _spectrumTimer.Start(); else _spectrumTimer.Stop();
        });
        _spectrumTimer.Tick += (_, _) => UpdateSpectrum();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        BuildSpectrum();
        ActualThemeVariantChanged += OnThemeVariantChanged;
        _mediaService.MediaChanged += OnMediaChanged;
        _ = RefreshMediaAsync();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        ActualThemeVariantChanged -= OnThemeVariantChanged;
        _mediaService.MediaChanged -= OnMediaChanged;
        _spectrumTimer.Stop();
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = null;
        this.FindControl<Image>("Artwork")!.Source = null;
        _artwork?.Dispose();
        _artwork = null;
        _audioSpectrumService?.Dispose();
        _audioSpectrumService = null;
    }

    private void OnMediaChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => _ = RefreshMediaAsync());

    private async Task RefreshMediaAsync()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        var cancellation = _refreshCancellation = new CancellationTokenSource();

        try
        {
            var session = await _mediaService.GetCurrentSessionAsync(cancellation.Token);

            var trackKey = session is null ? null : $"{session.Title}\x1f{session.Artist}";
            var trackChanged = !string.Equals(_trackKey, trackKey, StringComparison.Ordinal);

            string? artworkPath;
            if (!string.IsNullOrWhiteSpace(session?.AlbumArtPath))
            {
                artworkPath = session!.AlbumArtPath;
            }
            else if (!trackChanged && !string.IsNullOrWhiteSpace(_resolvedArtworkPath))
            {
                artworkPath = _resolvedArtworkPath;
            }
            else
            {
                artworkPath = session?.AlbumArtPath;
            }

            var replaceArtwork = !string.Equals(_artworkPath, artworkPath, StringComparison.Ordinal)
                               || trackChanged;

            (Bitmap Bitmap, byte[] Bytes)? artwork = null;
            Color? tint = null;

            if (replaceArtwork && !string.IsNullOrWhiteSpace(artworkPath))
            {
                artwork = await LoadArtworkAsync(artworkPath, cancellation.Token);
            }

            if (cancellation.IsCancellationRequested)
            {
                artwork?.Bitmap.Dispose();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
                ApplySession(session, trackKey, artworkPath, artwork?.Bitmap, tint, replaceArtwork));
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // An unavailable player or malformed artwork must leave the shell responsive.
        }
    }

    private void ApplySession(
        MediaSession? session,
        string? trackKey,
        string? artworkPath,
        Bitmap? artwork,
        Color? tint,
        bool replaceArtwork)
    {
        var trackChanged = !string.Equals(_trackKey, trackKey, StringComparison.Ordinal);
        _trackKey = trackKey;

        if (replaceArtwork)
        {
            if (artwork is not null || trackChanged)
            {
                var previousArtwork = _artwork;
                _artwork = artwork;
                // Release the old bitmap only after the Image stops referencing it.
                this.FindControl<Image>("Artwork")!.Source = artwork;
                previousArtwork?.Dispose();
                _artworkTint = tint;
                _artworkPath = artwork is null && !string.IsNullOrWhiteSpace(artworkPath) ? null : artworkPath;
            }

            if (artwork is not null)
            {
                _resolvedArtworkPath = artworkPath;
            }
            else if (trackChanged)
            {
                _resolvedArtworkPath = null;
            }
        }

        _isPlaying = session?.IsPlaying == true;
        UpdateSpectrumCaptureState();

        this.FindControl<TextBlock>("MediaTitle")!.Text = session?.Title ?? Loc.Get("Not playing");
        this.FindControl<TextBlock>("MediaArtist")!.Text = session is null
            ? ""
            : string.IsNullOrWhiteSpace(session.Artist) ? SourceLabel(session.SourceIdentity) : session.Artist;
        this.FindControl<DaisyIcon>("PlayPauseGlyph")!.Icon = _isPlaying ? "pause" : "play";
        ToolTip.SetTip(this.FindControl<Button>("PlayPauseButton")!, _isPlaying ? Loc.Get("Pause") : Loc.Get("Play"));
        var sourceGlyph = this.FindControl<DaisyIcon>("SourceGlyph")!;
        sourceGlyph.Icon = SourceGlyphFor(session?.SourceIdentity);
        sourceGlyph.Weight = sourceGlyph.Icon == "music-note" ? DaisyIconWeight.Fill : DaisyIconWeight.Regular;

        var sourceIcon = ResolveSourceIcon(session?.SourceIdentity);
        var sourceIconControl = this.FindControl<Image>("SourceIcon")!;
        sourceIconControl.Source = sourceIcon;
        sourceIconControl.IsVisible = sourceIcon is not null;
        this.FindControl<DaisyIcon>("SourceGlyph")!.IsVisible = sourceIcon is null;

        var image = this.FindControl<Image>("Artwork")!;
        var fallback = this.FindControl<Border>("SourceFallback")!;
        image.Source = _artwork;
        image.IsVisible = _artwork is not null;
        fallback.IsVisible = _artwork is null;

        UpdateTintLayer();
    }

    private void UpdateSpectrumCaptureState()
    {
        if (_isPlaying && IsEffectivelyVisible && _audioSpectrumService is null)
        {
            _audioSpectrumService = new LinuxAudioSpectrumService();
        }

        if (_isPlaying && IsEffectivelyVisible)
        {
            _spectrumTimer.Start();
            return;
        }

        _spectrumTimer.Stop();
        if (_audioSpectrumService is not null)
        {
            _audioSpectrumService.Dispose();
            _audioSpectrumService = null;
        }

        ResetSpectrum();
    }

    private async void OnPreviousClicked(object? sender, RoutedEventArgs e) => await RunMediaCommandAsync(_mediaService.PreviousAsync);
    private async void OnPlayPauseClicked(object? sender, RoutedEventArgs e) => await RunMediaCommandAsync(_mediaService.PlayPauseAsync);
    private async void OnNextClicked(object? sender, RoutedEventArgs e) => await RunMediaCommandAsync(_mediaService.NextAsync);

    private async Task RunMediaCommandAsync(Func<CancellationToken, Task> command)
    {
        try
        {
            await command(CancellationToken.None);
            await Task.Delay(100);
            await RefreshMediaAsync();
        }
        catch
        {
            // MPRIS controls are best-effort; a player can exit between a click and dispatch.
        }
    }

    private void BuildSpectrum()
    {
        if (_spectrumBars.Count != 0 || this.FindControl<Canvas>("SpectrumCanvas") is not { } canvas)
        {
            return;
        }

        const double idleScale = 0.12;
        for (var index = 0; index < SpectrumColumns; index++)
        {
            var bar = CreateSpectrumBar(idleScale);
            Canvas.SetLeft(bar, 3 + index * 4);
            canvas.Children.Add(bar);
            _spectrumBars.Add(bar);
        }
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        UpdateTintLayer();
    }

    private void UpdateTintLayer()
    {
        var tintLayer = this.FindControl<Border>("ArtworkTint");
        if (tintLayer is null) return;
        
        if (_artworkTint is { } color)
        {
            var isLightMode = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;
            byte alpha = isLightMode ? (byte)76 : (byte)128;
            tintLayer.Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }
        else
        {
            tintLayer.Background = Brushes.Transparent;
        }
    }

    private Border CreateSpectrumBar(double idleScale)
    {
        var bar = new Border
        {
            Width = 2,
            Height = Math.Max(2, 24 * idleScale),
            CornerRadius = new CornerRadius(1),
        };
        Canvas.SetTop(bar, (24 - bar.Height) / 2);

        bar.Classes.Add("spectrumBar");
        return bar;
    }

    private void UpdateSpectrum()
    {
        var spectrumService = _audioSpectrumService;
        if (!_isPlaying || spectrumService is null)
        {
            return;
        }

        try
        {
            spectrumService.CopySpectrum(_spectrumSnapshot);
            _spectrumIsIdle = false;
            for (var index = 0; index < _spectrumBars.Count; index++)
            {
                // The analyser returns six logarithmic ranges: bass on the left, treble on the right.
                var amplitude = _spectrumSnapshot[index];
                // Analysis remains linear; this display curve gives normal music enough
                // headroom to use the available height while preserving real band balance.
                var visualAmplitude = Math.Pow(Math.Clamp(amplitude * 1.35, 0, 1), 0.58);
                var scale = 0.12 + (visualAmplitude * 0.88);
                SetSpectrumBarScale(_spectrumBars[index], scale);
            }
        }
        catch
        {
            ResetSpectrum();
        }
    }

    private void ResetSpectrum()
    {
        if (_spectrumIsIdle)
        {
            return;
        }

        foreach (var bar in _spectrumBars)
        {
            SetSpectrumBarScale(bar, 0.12);
        }

        _spectrumIsIdle = true;
    }

    private static void SetSpectrumBarScale(Border bar, double scale)
    {
        bar.Height = Math.Max(bar.Width, 24 * scale);
        Canvas.SetTop(bar, (24 - bar.Height) / 2);
    }

    private Color GetResourceColor(string key, Color fallback) =>
        this.FindResource(key) is ISolidColorBrush brush ? brush.Color : fallback;

    private static async Task<(Bitmap Bitmap, byte[] Bytes)?> LoadArtworkAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            byte[] bytes;
            if (TryDecodeDataImageUri(path, out var inlineImage))
            {
                bytes = inlineImage;
            }
            else if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                bytes = await ArtworkHttpClient.GetByteArrayAsync(uri, cancellationToken);
            }
            else
            {
                bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            }

            using var stream = new MemoryStream(bytes, writable: false);
            return (new Bitmap(stream), bytes);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryDecodeDataImageUri(string value, out byte[] bytes)
    {
        bytes = [];
        const string prefix = "data:image/";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separator = value.IndexOf(',');
        if (separator < 0 || !value[..separator].Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = value[(separator + 1)..];
        if (payload.Length > (MaxInlineArtworkBytes * 4 / 3) + 4)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(payload);
            return bytes is { Length: > 0 and <= MaxInlineArtworkBytes };
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string SourceGlyphFor(string? source) => source?.ToLowerInvariant() switch
    {
        var value when value?.Contains("spotify") == true => "music-note",
        var value when value?.Contains("firefox") == true || value?.Contains("chrom") == true => "globe",
        _ => "music-note"
    };

    private static string SourceLabel(string? source) => string.IsNullOrWhiteSpace(source) ? Loc.Get("Media player") : source;

    private static IImage? ResolveSourceIcon(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var iconName = source.Contains("spotify", StringComparison.OrdinalIgnoreCase) ? "spotify"
            : source.Contains("firefox", StringComparison.OrdinalIgnoreCase) ? "firefox"
            : source.Contains("chrom", StringComparison.OrdinalIgnoreCase) ? "google-chrome"
            : source.Split('.', StringSplitOptions.RemoveEmptyEntries)[0];
        var iconPath = DesktopItemLoader.ResolveIconPath(iconName);
        return DesktopItemLoader.LoadBitmapSafe(iconPath);
    }
}
