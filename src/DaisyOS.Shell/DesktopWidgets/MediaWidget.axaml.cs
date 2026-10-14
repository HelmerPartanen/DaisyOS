using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.ViewModels;
using DaisyOS.System.Audio;
using DaisyOS.System.Media;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.DesktopWidgets;

public partial class MediaWidget : UserControl
{
    private const int SpectrumColumns = 6;
    private const int MaxInlineArtworkBytes = 4 * 1024 * 1024;
    private static readonly HttpClient ArtworkHttpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly IMediaSessionService _mediaService = LinuxMediaSessionService.Shared;
    private readonly DispatcherTimer _spectrumTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };

    private readonly List<Border> _largeSpectrumBars = [];
    private readonly List<Border> _spectrumTintLayers = [];
    private readonly double[] _spectrumSnapshot = new double[SpectrumColumns];

    private CancellationTokenSource? _refreshCancellation;
    private LinuxAudioSpectrumService? _audioSpectrumService;
    private Bitmap? _artwork;
    private string? _artworkPath;
    private Color? _artworkTint;
    private bool _isPlaying;
    private bool _isVisuallyActive;
    private bool _spectrumIsIdle = true;
    private string? _trackKey;
    private string? _resolvedArtworkPath;
    private int _columnSpan = 5;
    private int _rowSpan = 2;

    public MediaWidget()
    {
        InitializeComponent();
        Loc.RefreshOnChange(this, () => _ = RefreshMediaAsync());
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _ = new DaisyOS.Shell.Services.Diagnostics.VisualActivityGate(this, active =>
        {
            _isVisuallyActive = active;
            UpdateSpectrumCaptureState();
        });
        _spectrumTimer.Tick += (_, _) => UpdateSpectrum();
    }

    public void SetGridSize(int columnSpan, int rowSpan)
    {
        _columnSpan = Math.Max(1, columnSpan);
        _rowSpan = Math.Max(1, rowSpan);

        var container = this.FindControl<WidgetContainer>("Container");
        container?.SetGridSize(_columnSpan, _rowSpan);

        var medium = this.FindControl<Grid>("MediumLayout");
        var large = this.FindControl<Grid>("LargeLayout");

        if (_columnSpan <= 3)
        {
            if (medium != null) medium.IsVisible = true;
            if (large != null) large.IsVisible = false;
        }
        else
        {
            if (medium != null) medium.IsVisible = false;
            if (large != null) large.IsVisible = true;
        }

        UpdateSpectrumCaptureState();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Opacity = 0;
        BuildSpectrum();
        ActualThemeVariantChanged += OnThemeVariantChanged;
        _mediaService.MediaChanged += OnMediaChanged;
        SetGridSize(_columnSpan, _rowSpan);
        _ = RefreshMediaAsync();

        Dispatcher.UIThread.Post(() =>
        {
            Opacity = 1;
        }, DispatcherPriority.Render);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        ActualThemeVariantChanged -= OnThemeVariantChanged;
        _mediaService.MediaChanged -= OnMediaChanged;
        _spectrumTimer.Stop();
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = null;
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

            var replaceArtwork = !string.Equals(_artworkPath, artworkPath, StringComparison.Ordinal) || trackChanged;

            (Bitmap Bitmap, byte[] Bytes)? artwork = null;
            Color? tint = null;

            if (replaceArtwork && !string.IsNullOrWhiteSpace(artworkPath))
            {
                artwork = await LoadArtworkAsync(artworkPath, cancellation.Token);
                if (artwork is not null)
                {
                    tint = DaisyOS.Shell.Services.Media.ArtworkColorExtractor.GetOrExtract(artworkPath, artwork.Value.Bytes);
                }
            }
            else if (!replaceArtwork)
            {
                tint = _artworkTint;
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
            // Best effort
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
                _artwork?.Dispose();
                _artwork = artwork;
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

        var titleText = session?.Title ?? Loc.Get("Not playing");
        var artistText = session is null ? "" : string.IsNullOrWhiteSpace(session.Artist) ? SourceLabel(session.SourceIdentity) : session.Artist;
        var playIcon = _isPlaying ? "pause" : "play";
        var playTip = _isPlaying ? "Pause" : "Play";

        // 1. Medium Layout Updates (Full bleed cover + bottom left text / hover controls)
        if (this.FindControl<TextBlock>("MediumTitle") is { } mTitle) mTitle.Text = titleText;
        if (this.FindControl<TextBlock>("MediumArtist") is { } mArtist) mArtist.Text = artistText;
        if (this.FindControl<DaisyIcon>("MediumPlayPauseGlyph") is { } mGlyph) mGlyph.Icon = playIcon;
        if (this.FindControl<Button>("MediumPlayPauseButton") is { } mBtn) ToolTip.SetTip(mBtn, playTip);

        // 2. Large Layout Updates
        if (this.FindControl<TextBlock>("LargeTitle") is { } lTitle) lTitle.Text = titleText;
        if (this.FindControl<TextBlock>("LargeArtist") is { } lArtist) lArtist.Text = artistText;
        if (this.FindControl<DaisyIcon>("LargePlayPauseGlyph") is { } lGlyph) lGlyph.Icon = playIcon;
        if (this.FindControl<Button>("LargePlayPauseButton") is { } lBtn) ToolTip.SetTip(lBtn, playTip);

        // Source icons & fallbacks
        var sourceGlyph = SourceGlyphFor(session?.SourceIdentity);
        var sourceIcon = ResolveSourceIcon(session?.SourceIdentity);

        ApplyArtworkViews(
            "MediumArtwork", "MediumFallback", "MediumSourceIcon", "MediumSourceGlyph",
            _artwork, sourceIcon, sourceGlyph);

        ApplyArtworkViews(
            "LargeArtwork", "LargeFallback", "LargeSourceIcon", "LargeSourceGlyph",
            _artwork, sourceIcon, sourceGlyph);

        UpdateTintLayer();
        UpdateSpectrumBarTint();
    }

    private void ApplyArtworkViews(
        string artworkName,
        string fallbackName,
        string iconName,
        string glyphName,
        Bitmap? artwork,
        IImage? sourceIcon,
        string sourceGlyph)
    {
        if (this.FindControl<Image>(artworkName) is { } img)
        {
            img.Source = artwork;
            img.IsVisible = artwork is not null;
        }

        if (this.FindControl<Border>(fallbackName) is { } fb)
        {
            fb.IsVisible = artwork is null;
        }

        if (this.FindControl<Image>(iconName) is { } ic)
        {
            ic.Source = sourceIcon;
            ic.IsVisible = sourceIcon is not null;
        }

        if (this.FindControl<DaisyIcon>(glyphName) is { } gl)
        {
            gl.Icon = sourceGlyph;
            gl.Weight = gl.Icon == "music-note" ? DaisyIconWeight.Fill : DaisyIconWeight.Regular;
            gl.IsVisible = sourceIcon is null;
        }
    }

    private void UpdateSpectrumCaptureState()
    {
        if (_isPlaying && _isVisuallyActive && _columnSpan > 3 && _audioSpectrumService is null)
        {
            _audioSpectrumService = new LinuxAudioSpectrumService();
        }

        if (_isPlaying && _isVisuallyActive && _columnSpan > 3)
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
        }
    }

    private void BuildSpectrum()
    {
        if (_largeSpectrumBars.Count == 0 && this.FindControl<Canvas>("LargeSpectrumCanvas") is { } lCanvas)
        {
            const double idleScale = 0.12;
            for (var index = 0; index < SpectrumColumns; index++)
            {
                var bar = CreateSpectrumBar(idleScale);
                Canvas.SetLeft(bar, 3 + index * 4);
                Canvas.SetTop(bar, 2);
                lCanvas.Children.Add(bar);
                _largeSpectrumBars.Add(bar);
            }
        }

        UpdateSpectrumBarTint();
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        UpdateTintLayer();
        UpdateSpectrumBarTint();
    }

    private void UpdateTintLayer()
    {
        var tintLayer = this.FindControl<Border>("ArtworkTint");
        if (tintLayer == null) return;

        if (_artworkTint is { } color)
        {
            tintLayer.Background = new SolidColorBrush(Color.FromArgb(
                DaisyOS.Shell.Services.Media.ArtworkColorExtractor.DefaultTintAlpha,
                color.R, color.G, color.B));
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
            Height = 24,
            CornerRadius = new CornerRadius(1),
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = new ScaleTransform(1, idleScale)
        };

        var tintLayer = new Border
        {
            CornerRadius = new CornerRadius(1),
            IsHitTestVisible = false,
            Opacity = 0
        };
        bar.Child = tintLayer;
        _spectrumTintLayers.Add(tintLayer);
        bar.Classes.Add("spectrumBar");
        return bar;
    }

    private void UpdateSpectrumBarTint()
    {
        foreach (var tintLayer in _spectrumTintLayers)
        {
            tintLayer.Background = _artworkTint is { } color
                ? new SolidColorBrush(color)
                : Brushes.Transparent;
            tintLayer.Opacity = _artworkTint is null ? 0 : 0.55;
        }
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

            for (var index = 0; index < SpectrumColumns; index++)
            {
                var amplitude = _spectrumSnapshot[index];
                var visualAmplitude = Math.Pow(Math.Clamp(amplitude * 1.35, 0, 1), 0.58);
                var scale = 0.12 + (visualAmplitude * 0.88);

                if (index < _largeSpectrumBars.Count)
                {
                    ((ScaleTransform)_largeSpectrumBars[index].RenderTransform!).ScaleY = scale;
                }
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

        foreach (var bar in _largeSpectrumBars)
        {
            ((ScaleTransform)bar.RenderTransform!).ScaleY = 0.12;
        }

        _spectrumIsIdle = true;
    }

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
