using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.Wayland;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.Services.Media;
using DaisyOS.Shell.Services.Shell;
using DaisyOS.Shell.Services.Theming;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Wallpaper;
using DaisyOS.System.Audio;
using DaisyOS.System.Media;
using DaisyOS.System.Power;
using DaisyOS.System.Processes;
using DaisyOS.System.Security;

namespace DaisyOS.Shell.Views.Windows;

public partial class LockScreenWindow : Window
{
    private const int SpectrumColumns = 6;
    private const int MaxInlineArtworkBytes = 4 * 1024 * 1024;
    private static readonly HttpClient ArtworkHttpClient = new() { Timeout = TimeSpan.FromSeconds(3) };

    private readonly ILockScreenService _lockService;
    private readonly IPowerService _powerService;
    private readonly IBatteryStatusService _batteryService;
    private readonly IMediaSessionService _mediaService;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _batteryTimer;
    private readonly bool _isSecondary;
    private bool _isAuthenticating;
    private bool _isShaking;
    private Task? _dismissal;
    private bool _dismissing;
    private bool _closed;
    private bool _mediaRefreshInFlight;
    private Rect _collapsedArtworkRect;
    private Rect _expandedArtworkRect;

    private readonly DispatcherTimer _presenceTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly global::System.Diagnostics.Stopwatch _presenceTime = new();
    private readonly TranslateTransform _clockPresence = new();
    private readonly TranslateTransform _credentialsPresence = new();
    private readonly TranslateTransform _cornerControlsTransform = new();
    private TaskCompletionSource? _presenceCompletion;
    private double _presenceProgress;
    private double _presenceTarget, _presenceStart;
    private TimeSpan _presenceDuration;

    private readonly DispatcherTimer _mediaPresenceTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly global::System.Diagnostics.Stopwatch _mediaPresenceTime = new();
    private double _mediaPresenceProgress;
    private double _mediaPresenceTarget, _mediaPresenceStart;
    private TimeSpan _mediaPresenceDuration;

    private readonly DispatcherTimer _mediaExpandTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly global::System.Diagnostics.Stopwatch _mediaExpandTime = new();
    private TaskCompletionSource? _mediaExpandCompletion;
    private double _mediaExpandProgress;
    private double _mediaExpandTarget, _mediaExpandStart;
    private TimeSpan _mediaExpandDuration;

    private readonly TranslateTransform _collapsedCardTranslate = new();
    private readonly ScaleTransform _collapsedCardScale = new(1, 1);
    private readonly TransformGroup _collapsedCardTransform = new();

    private readonly TranslateTransform _expandedCardTranslate = new();
    private readonly ScaleTransform _expandedCardScale = new(1, 1);
    private readonly TransformGroup _expandedCardTransform = new();

    private readonly TranslateTransform _morphingArtworkTranslate = new();

    private readonly Panel _lockCanvasLayer;
    private readonly Panel _lockCanvasFallbackPanel;
    private readonly Image _lockCanvasFallbackImage;
    private readonly WallpaperLayer _lockCanvasWallpaper;

    private readonly Grid _collapsedMediaHost;
    private readonly ShellFrameBorder _collapsedMediaBorder;
    private readonly Border _collapsedArtworkTint;
    private readonly Border _collapsedArtworkButton;
    private readonly Image _collapsedArtworkImage;
    private readonly Border _collapsedArtworkFallback;
    private readonly Image _collapsedSourceIcon;
    private readonly DaisyIcon _collapsedSourceGlyph;
    private readonly Canvas _collapsedSpectrumCanvas;
    private readonly DaisyOS.Shell.Controls.MarqueeText _collapsedTitleText;
    private readonly TextBlock _collapsedArtistText;
    private readonly Button _collapsedPlayPauseButton;
    private readonly DaisyIcon _collapsedPlayPauseGlyph;

    private readonly Grid _expandedMediaHost;
    private readonly StackPanel _expandedMediaStack;
    private readonly ShellFrameBorder _expandedArtworkContainer;
    private readonly Image _expandedArtworkImage;
    private readonly Border _expandedArtworkFallback;
    private readonly Image _expandedSourceIcon;
    private readonly DaisyIcon _expandedSourceGlyph;
    private readonly ShellFrameBorder _expandedMediaBorder;
    private readonly DaisyOS.Shell.Controls.MarqueeText _expandedTitleText;
    private readonly TextBlock _expandedArtistText;
    private readonly Canvas _expandedSpectrumCanvas;
    private readonly TextBlock _expandedElapsedText;
    private readonly Border _expandedProgressTrack;
    private readonly Border _expandedProgressFill;
    private readonly TextBlock _expandedRemainingText;
    private readonly Button _expandedPlayPauseButton;
    private readonly DaisyIcon _expandedPlayPauseGlyph;

    private readonly Border _morphingArtworkBorder;
    private readonly Border _morphingArtworkInner;
    private readonly Image _morphingArtworkImage;
    private readonly Border _morphingArtworkFallback;
    private readonly Image _morphingSourceIcon;
    private readonly DaisyIcon _morphingSourceGlyph;
    private readonly Border _morphingArtworkHighlight;

    private readonly DispatcherTimer _clockAnimation = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly global::System.Diagnostics.Stopwatch _clockAnimationTime = new();
    private bool _clockInitialized;
    private KWinBlur? _nativeBlur;
    private readonly Dictionary<(string, Typeface, double), Rect[]> _clockGlyphBands = new();

    private readonly DispatcherTimer _spectrumTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _mediaPositionTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<Border> _collapsedSpectrumBars = [];
    private readonly List<Border> _expandedSpectrumBars = [];
    private readonly List<Border> _spectrumTintLayers = [];
    private readonly double[] _spectrumSnapshot = new double[SpectrumColumns];
    private LinuxAudioSpectrumService? _audioSpectrumService;
    private bool _spectrumIsIdle = true;

    private CancellationTokenSource? _mediaRefreshCancellation;
    private CancellationTokenSource? _canvasResolveCancellation;
    private Bitmap? _artwork;
    private string? _artworkPath;
    private string? _resolvedArtworkPath;
    private Color? _artworkTint;
    private string? _trackKey;
    private string? _sourceIdentity;
    private IImage? _sourceIcon;
    private string? _canvasTrackKey;
    private string? _resolvedCanvasVideoPath;
    private bool _canvasVideoAttached;
    private readonly DispatcherTimer _canvasRevealTimer = new(DispatcherPriority.Render)
        { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly global::System.Diagnostics.Stopwatch _canvasRevealTime = new();
    private double _canvasReveal;
    private double _canvasRevealStart;
    private double _canvasRevealTarget;

    private bool _hasMediaSession;
    private bool _isPlaying;
    private double _mediaPositionSeconds;
    private double _mediaDurationSeconds;
    private DateTimeOffset _lastPositionSyncAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastPeriodicSessionSyncAt = DateTimeOffset.UtcNow;

    public event Action? Dismissing;

    public bool HasActiveMediaSession => _hasMediaSession;
    public bool IsMediaExpanded => _mediaExpandTarget > 0.5;
    public double MediaExpandProgress => _mediaExpandProgress;
    public string? ActiveCanvasVideoPath => _resolvedCanvasVideoPath;

    // KWin needs the sharp wallpaper beneath a transparent foreground surface.
    // Restrict blur to material containers and clock glyphs, never the fullscreen root.
    internal static LockScreenWindow CreateNative(ILayerShellWindowBackend backend,
        LayerShellSurfaceOptions options, string? wallpaperSource, bool isSecondary = false,
        ILockScreenService? lockService = null, WallpaperLayer? initialWallpaper = null,
        IMediaSessionService? mediaService = null)
    {
        var wallpaper = backend.CreateWindow(options with
        {
            Namespace = "daisyos.overlay.lockscreen.wallpaper",
            KeyboardInteractivity = LayerShellKeyboardInteractivity.None,
            InputPassthrough = true
        }, () => Loc.Bind(new Window
        {
            Title = "DaisyOS Lock Wallpaper",
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            CanResize = false,
            Background = Brushes.Black
        }, Window.TitleProperty, "DaisyOS Lock Wallpaper"));
        var window = backend.CreateWindow(options,
            () => new LockScreenWindow(wallpaperSource, lockService, isSecondary, mediaService));
        ThemeTransitionPolicy.SetEnabled(window, false);
        window.Background = Brushes.Transparent;
        if (initialWallpaper is not null) window.LockWallpaper.SeedFrom(initialWallpaper);
        window.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        window.RootGrid.Children.Remove(window.LockBackground);
        wallpaper.Content = window.LockBackground;
        window.Closed += (_, _) => wallpaper.Close();
        window._nativeBlur = new KWinBlur(window, window.RootGrid,
            () => new Control[]
            {
                window.AvatarBorder,
                window.PasswordPillBorder,
                window._collapsedMediaBorder,
                window._expandedArtworkContainer,
                window._expandedMediaBorder
            })
        {
            AdditionalRegions = window.GetClockBlurRegions
        };
        window.ClockText.PropertyChanged += (_, _) => window._nativeBlur.RefreshImmediately();
        window.PreviousClockText.PropertyChanged += (_, _) => window._nativeBlur.RefreshImmediately();
        wallpaper.Show();
        return window;
    }

    internal IEnumerable<WaylandBlurRect> GetClockBlurRegions()
    {
        var regions = new List<WaylandBlurRect>();
        if (!ClockArea.IsEffectivelyVisible || ClockArea.Opacity <= 0.001 || RootGrid.Opacity <= 0.001)
            return regions;
        foreach (var text in new[] { ClockText, PreviousClockText })
        {
            if (!text.IsEffectivelyVisible || text.Opacity <= 0.001 || text.Bounds.Width <= 0) continue;
            var transform = text.TransformToVisual(this);
            if (transform is null) continue;
            var value = text.Text ?? string.Empty;
            var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
            var key = (value, typeface, text.FontSize);
            if (!_clockGlyphBands.TryGetValue(key, out var bands))
            {
                bands = RasterizeClockGlyphBands(value, typeface, text.FontSize);
                if (_clockGlyphBands.Count >= 4) _clockGlyphBands.Clear();
                _clockGlyphBands[key] = bands;
            }
            foreach (var band in bands)
            {
                var moved = band.TransformToAABB(transform.Value);
                var viewportOrigin = LockContentScroll.TranslatePoint(default, this);
                if (viewportOrigin is { } origin)
                    moved = moved.Intersect(new Rect(origin, LockContentScroll.Bounds.Size));
                if (moved.Width <= 0 || moved.Height <= 0) continue;
                KWinBlur.AddRoundedRegion(regions, moved.Position, moved.Size, 0);
            }
        }
        return regions;
    }

    internal static Rect[] RasterizeClockGlyphBands(string value, Typeface typeface, double fontSize)
    {
        var formatted = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, fontSize, Brushes.White);
        formatted.SetFontFeatures(DaisyOS.Shell.Services.Theming.TypographyPolicy.ProportionalNumerals);
        var geometry = formatted.BuildGeometry(default);
        if (geometry is null || geometry.Bounds.Width <= 0 || geometry.Bounds.Height <= 0) return [];
        var bounds = geometry.Bounds;
        var left = (int)Math.Floor(bounds.Left);
        var top = (int)Math.Floor(bounds.Top);
        var width = (int)Math.Ceiling(bounds.Right) - left;
        var height = (int)Math.Ceiling(bounds.Bottom) - top;
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var drawing = bitmap.CreateDrawingContext())
        using (drawing.PushTransform(Matrix.CreateTranslation(-left, -top)))
            drawing.DrawGeometry(Brushes.White, null, geometry);
        // Rasterize once, then scan alpha bytes. Calling FillContains for each
        // pixel repeatedly walked every glyph path and stalled native entry.
        var pixels = new byte[checked(width * height * 4)];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, width, height), pinned.AddrOfPinnedObject(), pixels.Length, width * 4); }
        finally { pinned.Free(); }
        var bands = new List<Rect>();
        for (var y = 0; y < height; y++)
        {
            var start = -1;
            for (var x = 0; x <= width; x++)
            {
                // A binary KWin mask cannot preserve glyph antialiasing. Use the
                // solid interior so its integer edge stays beneath the rendered text.
                var inside = x < width && pixels[(y * width + x) * 4 + 3] >= 128;
                if (inside && start < 0) start = x;
                if (!inside && start >= 0) { bands.Add(new Rect(left + start, top + y, x - start, 1)); start = -1; }
            }
        }
        return bands.ToArray();
    }

    public LockScreenWindow() : this(null, null, false, null) { }

    public LockScreenWindow(ILockScreenService lockService) : this(null, lockService, false, null) { }

    public LockScreenWindow(
        string? wallpaperSource = null,
        ILockScreenService? lockService = null,
        bool isSecondary = false,
        IMediaSessionService? mediaService = null)
    {
        InitializeComponent();

        _lockCanvasLayer = this.FindControl<Panel>("LockCanvasLayer")!;
        _lockCanvasFallbackPanel = this.FindControl<Panel>("LockCanvasFallbackPanel")!;
        _lockCanvasFallbackImage = this.FindControl<Image>("LockCanvasFallbackImage")!;
        _lockCanvasWallpaper = this.FindControl<WallpaperLayer>("LockCanvasWallpaper")!;

        _collapsedMediaHost = this.FindControl<Grid>("CollapsedMediaHost")!;
        _collapsedMediaBorder = this.FindControl<ShellFrameBorder>("CollapsedMediaBorder")!;
        _collapsedArtworkTint = this.FindControl<Border>("CollapsedArtworkTint")!;
        _collapsedArtworkButton = this.FindControl<Border>("CollapsedArtworkButton")!;
        _collapsedArtworkImage = this.FindControl<Image>("CollapsedArtworkImage")!;
        _collapsedArtworkFallback = this.FindControl<Border>("CollapsedArtworkFallback")!;
        _collapsedSourceIcon = this.FindControl<Image>("CollapsedSourceIcon")!;
        _collapsedSourceGlyph = this.FindControl<DaisyIcon>("CollapsedSourceGlyph")!;
        _collapsedSpectrumCanvas = this.FindControl<Canvas>("CollapsedSpectrumCanvas")!;
        _collapsedTitleText = this.FindControl<DaisyOS.Shell.Controls.MarqueeText>("CollapsedTitleText")!;
        _collapsedArtistText = this.FindControl<TextBlock>("CollapsedArtistText")!;
        _collapsedPlayPauseButton = this.FindControl<Button>("CollapsedPlayPauseButton")!;
        _collapsedPlayPauseGlyph = this.FindControl<DaisyIcon>("CollapsedPlayPauseGlyph")!;

        _expandedMediaHost = this.FindControl<Grid>("ExpandedMediaHost")!;
        _expandedMediaStack = this.FindControl<StackPanel>("ExpandedMediaStack")!;
        _expandedArtworkContainer = this.FindControl<ShellFrameBorder>("ExpandedArtworkContainer")!;
        _expandedArtworkImage = this.FindControl<Image>("ExpandedArtworkImage")!;
        _expandedArtworkFallback = this.FindControl<Border>("ExpandedArtworkFallback")!;
        _expandedSourceIcon = this.FindControl<Image>("ExpandedSourceIcon")!;
        _expandedSourceGlyph = this.FindControl<DaisyIcon>("ExpandedSourceGlyph")!;
        _expandedMediaBorder = this.FindControl<ShellFrameBorder>("ExpandedMediaBorder")!;
        _expandedTitleText = this.FindControl<DaisyOS.Shell.Controls.MarqueeText>("ExpandedTitleText")!;
        _expandedArtistText = this.FindControl<TextBlock>("ExpandedArtistText")!;
        _expandedSpectrumCanvas = this.FindControl<Canvas>("ExpandedSpectrumCanvas")!;
        _expandedElapsedText = this.FindControl<TextBlock>("ExpandedElapsedText")!;
        _expandedProgressTrack = this.FindControl<Border>("ExpandedProgressTrack")!;
        _expandedProgressFill = this.FindControl<Border>("ExpandedProgressFill")!;
        _expandedRemainingText = this.FindControl<TextBlock>("ExpandedRemainingText")!;
        _expandedPlayPauseButton = this.FindControl<Button>("ExpandedPlayPauseButton")!;
        _expandedPlayPauseGlyph = this.FindControl<DaisyIcon>("ExpandedPlayPauseGlyph")!;

        _morphingArtworkBorder = this.FindControl<Border>("MorphingArtworkBorder")!;
        _morphingArtworkInner = this.FindControl<Border>("MorphingArtworkInner")!;
        _morphingArtworkImage = this.FindControl<Image>("MorphingArtworkImage")!;
        _morphingArtworkFallback = this.FindControl<Border>("MorphingArtworkFallback")!;
        _morphingSourceIcon = this.FindControl<Image>("MorphingSourceIcon")!;
        _morphingSourceGlyph = this.FindControl<DaisyIcon>("MorphingSourceGlyph")!;
        _morphingArtworkHighlight = this.FindControl<Border>("MorphingArtworkHighlight")!;

        _collapsedCardTransform.Children.Add(_collapsedCardScale);
        _collapsedCardTransform.Children.Add(_collapsedCardTranslate);
        _collapsedMediaBorder.RenderTransform = _collapsedCardTransform;

        _expandedCardTransform.Children.Add(_expandedCardScale);
        _expandedCardTransform.Children.Add(_expandedCardTranslate);
        _expandedMediaBorder.RenderTransform = _expandedCardTransform;

        _morphingArtworkBorder.RenderTransform = _morphingArtworkTranslate;

        ClockArea.RenderTransform = _clockPresence;
        CredentialsPanel.RenderTransform = _credentialsPresence;
        CornerControlsGrid.RenderTransform = _cornerControlsTransform;

        _lockCanvasWallpaper.SuppressFailureFeedback = true;
        _lockCanvasWallpaper.FollowDesktopWallpaper = false;
        _canvasRevealTimer.Tick += (_, _) => ApplyVisualState();
        _lockCanvasWallpaper.PresentationCompleted += (_, _) => Dispatcher.UIThread.Post(ApplyVisualState);

        _presenceTimer.Tick += (_, _) => AdvancePresence();
        _mediaPresenceTimer.Tick += (_, _) => AdvanceMediaPresence();
        _mediaExpandTimer.Tick += (_, _) => AdvanceMediaExpansion();
        _spectrumTimer.Tick += (_, _) => UpdateSpectrum();
        _mediaPositionTimer.Tick += (_, _) => UpdateLiveMediaProgress();

        _isSecondary = isSecondary;
        _lockService = lockService ?? new LinuxLockScreenService();
        _powerService = new LinuxPowerService(new SafeCommandRunner());
        _batteryService = new LinuxBatteryStatusService();
        _mediaService = mediaService ?? LinuxMediaSessionService.Shared;

        ApplyPresence(0);

        UserNameText.Text = GetFormattedUserName();
        SizeChanged += (_, _) =>
        {
            UpdateLockLayout();
            UpdateProgressFillWidth();
            if (_mediaExpandProgress > 0.001 && _mediaExpandProgress < 0.999)
            {
                _collapsedArtworkRect = GetRestingCollapsedArtworkRect();
                _expandedArtworkRect = GetRestingExpandedArtworkRect();
                ApplyVisualState();
            }
        };
        _expandedProgressTrack.SizeChanged += (_, _) => UpdateProgressFillWidth();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == FontSizeProperty || args.Property == FontFamilyProperty) UpdateLockLayout();
        };
        ClockText.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBlock.FontFamilyProperty) UpdateLockLayout();
        };
        LockContentScroll.ScrollChanged += (_, _) => _nativeBlur?.RefreshImmediately();
        Loc.RefreshOnChange(this, () =>
        {
            UpdateClock();
            if (!_isSecondary) _ = RefreshMediaAsync();
        });
        if (Application.Current is App app)
        {
            EventHandler refreshLayout = (_, _) => UpdateLockLayout();
            app.AppearanceChanged += refreshLayout;
            Closed += (_, _) => app.AppearanceChanged -= refreshLayout;
        }

        var wallpaperImage = LockWallpaper.FindControl<Image>("WallpaperImage")!;
        DeptForeground.Configure(wallpaperImage, ClockText, ClockArea,
            () => RasterizeClockGlyphBands(ClockText.Text ?? "", new Typeface(ClockText.FontFamily,
                ClockText.FontStyle, ClockText.FontWeight, ClockText.FontStretch), ClockText.FontSize),
            () => !IsMediaExpanded && LockContentScroll.Offset.Y < 1 && wallpaperImage.Opacity > 0.999 && LockWallpaper.Opacity > 0.99 && _lockCanvasLayer.Opacity < 0.01, LockWallpaperScrim.Background);
        void RefreshDeptEffect()
        {
            var settings = new AppearanceSettingsStore().Load();
            DeptForeground.SetPreferences(settings.DeptEffectEnabled && !settings.HighContrast && !DaisyOS.Shell.Services.Wallpaper.WallpaperFileTypes.IsVideo(LockWallpaper.WallpaperUri ?? wallpaperSource ?? ""),
                settings.DeptEffectAutomatic, settings.DeptEffectStrength);
        }
        RefreshDeptEffect();
        if (Application.Current is App deptApp)
        {
            EventHandler refreshDept = (_, _) =>
            {
                LoadWallpaper(deptApp.UsesSolidWallpaper ? deptApp.CurrentSolidWallpaperColor.ToString() : deptApp.CurrentWallpaperUri);
                RefreshDeptEffect();
            };
            deptApp.AppearanceChanged += refreshDept;
            Closed += (_, _) => deptApp.AppearanceChanged -= refreshDept;
        }
        LoadWallpaper(wallpaperSource);
        RefreshDeptEffect();

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _batteryTimer.Tick += (_, _) => UpdateBatteryAsync();
        if (!_isSecondary) _batteryTimer.Start();
        UpdateClock();
        _clockAnimation.Tick += (_, _) =>
        {
            var progress = Math.Clamp(_clockAnimationTime.Elapsed.TotalMilliseconds / 320, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            ClockText.Opacity = eased;
            PreviousClockText.Opacity = 1 - eased;
            if (progress >= 1) _clockAnimation.Stop();
        };

        if (_isSecondary)
        {
            CredentialsPanel.IsVisible = false;
            PowerControlsPanel.IsVisible = false;
            BatteryInfo.IsVisible = false;
            _collapsedMediaHost.IsVisible = false;
            _expandedMediaHost.IsVisible = false;
        }
        else
        {
            BuildSpectrumBars();
            UpdateBatteryAsync();

            KeyDown += OnWindowKeyDown;
            PasswordBox.KeyDown += (s, e) =>
            {
                if (_isAuthenticating) return;
                if (e.Key == Key.Enter)
                {
                    OnUnlockClicked(s, e);
                }
            };

            _lockService.Lock();
        }

        Opened += (_, _) =>
        {
            UpdateLockLayout();
            RootGrid.Opacity = 1;
            _ = AnimatePresenceAsync(true);
            if (!_isSecondary)
            {
                _mediaService.MediaChanged += OnMediaChanged;
                _ = RefreshMediaAsync();
                PasswordBox.Focus();
            }
        };

        Closed += (_, _) =>
        {
            _closed = true;
            _clockTimer.Stop();
            _batteryTimer.Stop();
            _clockAnimation.Stop();
            _presenceTimer.Stop();
            _mediaPresenceTimer.Stop();
            _mediaExpandTimer.Stop();
            _canvasRevealTimer.Stop();
            _spectrumTimer.Stop();
            _mediaPositionTimer.Stop();
            _presenceCompletion?.TrySetResult();
            _mediaExpandCompletion?.TrySetResult();

            if (!_isSecondary)
            {
                _mediaService.MediaChanged -= OnMediaChanged;
            }

            _mediaRefreshCancellation?.Cancel();
            _mediaRefreshCancellation?.Dispose();
            _mediaRefreshCancellation = null;

            _canvasResolveCancellation?.Cancel();
            _canvasResolveCancellation?.Dispose();
            _canvasResolveCancellation = null;

            _audioSpectrumService?.Dispose();
            _audioSpectrumService = null;

            _collapsedArtworkImage.Source = null;
            _expandedArtworkImage.Source = null;
            _morphingArtworkImage.Source = null;
            _lockCanvasFallbackImage.Source = null;
            _ = RetireArtworkAsync(_artwork);
            _artwork = null;
            _collapsedSourceIcon.Source = null;
            _expandedSourceIcon.Source = null;
            _morphingSourceIcon.Source = null;
            _sourceIcon = null;
            _lockCanvasWallpaper.ClearWallpaper();
        };
        Closing += (_, e) =>
        {
            if (!_isSecondary && _lockService.IsLocked)
            {
                e.Cancel = true;
            }
        };
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsMediaExpanded)
        {
            e.Handled = true;
            _ = SetMediaExpandedAsync(false);
        }
    }

    private void UpdateLockLayout()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        var width = Math.Max(0, ClientSize.Width - 48);
        var height = ClientSize.Height;
        double Resource(string key, double fallback) =>
            this.TryFindResource(key, out var value) && value is double number ? number : fallback;
        var controlHeight = Resource("ControlHeightStandard", 32);
        CredentialsPanel.Width = Math.Min(360, width);
        CredentialsPanel.Margin = new Thickness(0, 24, 0, Math.Clamp(height * 0.12, 48, 160));
        PasswordPillBorder.Width = Math.Min(CredentialsPanel.Width, Math.Round(272 * Math.Max(1, controlHeight / 32)));
        PasswordPillBorder.Height = controlHeight + 8;
        PasswordPillBorder.CornerRadius = new CornerRadius((controlHeight + 8) / 2);
        MainContentGrid.MinHeight = Math.Max(0, height - controlHeight - 40);
        ClockArea.Margin = new Thickness(0, Math.Clamp(height * 0.1, 24, 96), 0, 0);
        DateText.MaxWidth = width;
        var size = Math.Min(Resource("TypeSizeDisplayHero", 104) * Math.Clamp(height / 720, 0.75, 1.25),
            Math.Clamp(height * 0.2, 64, 160));
        using var measured = new TextLayout("88:88", new Typeface(ClockText.FontFamily,
            ClockText.FontStyle, ClockText.FontWeight, ClockText.FontStretch), size, fontFeatures: ClockText.FontFeatures);
        if (measured.Width > width && measured.Width > 0) size *= width / measured.Width;
        ClockText.FontSize = PreviousClockText.FontSize = Math.Max(1, Math.Floor(size));
        DateText.FontSize = Math.Min(Resource("TypeSizeHeading", 24), Math.Max(18, Math.Round(size * 0.24)));

        // Responsive sizing for Collapsed & Expanded Media widgets
        var cardWidth = Math.Clamp(Math.Min(354, width), 240, 354);
        var collapsedHeight = Math.Clamp(Math.Round(cardWidth * (160.0 / 354.0)), 120, 160);
        var collapsedArtSize = Math.Max(84, collapsedHeight - 34);
        _collapsedMediaBorder.Width = cardWidth;
        _collapsedMediaBorder.Height = collapsedHeight;
        _collapsedArtworkButton.Width = _collapsedArtworkButton.Height = collapsedArtSize;

        _expandedMediaBorder.Width = cardWidth;
        var clockBottomEstimate = Math.Clamp(height * 0.1, 24, 96) + size + 44;
        var availableForExpanded = Math.Max(220, height - clockBottomEstimate - 36);
        var expandedArtSize = Math.Clamp(Math.Min(width * 0.5, availableForExpanded - 172), 148, 312);
        _expandedArtworkContainer.Width = _expandedArtworkContainer.Height = Math.Round(expandedArtSize);
        _expandedMediaStack.Spacing = Math.Clamp(height * 0.032, 14, 28);
        _expandedMediaStack.Margin = new Thickness(24, 0, 24, Math.Clamp(height * 0.045, 20, 40));
    }

    private void LoadWallpaper(string? wallpaperSource)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(wallpaperSource))
            {
                var appearance = new AppearanceSettingsStore().Load();
                wallpaperSource = appearance.WallpaperUri;
                if (appearance.UseSolidWallpaper && !string.IsNullOrWhiteSpace(appearance.SolidWallpaperColor))
                {
                    wallpaperSource = appearance.SolidWallpaperColor;
                }
            }

            LockWallpaper.WallpaperUri = wallpaperSource;
        }
        catch (Exception ex)
        {
            global::System.Console.Error.WriteLine($"[LockScreen] Failed to load wallpaper: {ex.Message}");
        }
    }

    private static string GetFormattedUserName()
    {
        try
        {
            var user = Environment.UserName;
            if (string.IsNullOrWhiteSpace(user)) return "DaisyOS User";

            if (char.IsLower(user[0]))
            {
                return char.ToUpper(user[0]) + user[1..];
            }

            return user;
        }
        catch
        {
            return "DaisyOS User";
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var useTwelveHour = string.Equals(
            (Application.Current as App)?.GetPreference("time.clock-format", "24-hour"),
            "12-hour",
            StringComparison.OrdinalIgnoreCase);
        var time = now.ToString(useTwelveHour ? "h:mm" : "H:mm", CultureInfo.InvariantCulture);
        if (ClockText.Text != time)
        {
            PreviousClockText.Text = ClockText.Text;
            ClockText.Text = time;
            if (_clockInitialized)
            {
                ClockText.Opacity = 0;
                PreviousClockText.Opacity = 1;
                ClockText.RenderTransform = null;
                PreviousClockText.RenderTransform = null;
                _clockAnimationTime.Restart();
                _clockAnimation.Start();
            }
        }
        _clockInitialized = true;
        DateText.Text = now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
    }

    private async void UpdateBatteryAsync()
    {
        try
        {
            var status = await _batteryService.GetStatusAsync();
            if (status.IsPresent && status.ChargePercent.HasValue)
            {
                BatteryInfo.ChargePercent = status.ChargePercent.Value;
                BatteryInfo.IsCharging = BatteryStatusView.IsChargingState(status.State);
                BatteryInfo.StateText = Loc.Get(status.State);
                BatteryInfo.IsVisible = true;
                ToolTip.SetTip(BatteryInfo, status.Detail);
            }
            else BatteryInfo.IsVisible = false;
        }
        catch
        {
            // Ignore battery query failures
        }
    }

    private void OnMediaChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => _ = RefreshMediaAsync());

    public async Task RefreshMediaAsync()
    {
        if (_isSecondary || _dismissing || _closed) return;

        _mediaRefreshCancellation?.Cancel();
        _mediaRefreshCancellation?.Dispose();
        var cancellation = _mediaRefreshCancellation = new CancellationTokenSource();
        _mediaRefreshInFlight = true;

        try
        {
            var session = await _mediaService.GetCurrentSessionAsync(cancellation.Token);
            if (session is not null && string.IsNullOrWhiteSpace(session.Title))
            {
                session = null;
            }

            if (cancellation.IsCancellationRequested || _closed) return;

            var trackKey = session is null ? null : $"{session.SourceIdentity}\x1f{session.SourceUrl}\x1f{session.Title}\x1f{session.Artist}";
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

            // Publish metadata and start Canvas resolution immediately; artwork I/O
            // must not delay transport state or leave the previous track visible.
            if (trackChanged)
                ApplyMediaSession(session, trackKey, null, null, null, replaceArtwork: true);

            if (replaceArtwork && !string.IsNullOrWhiteSpace(artworkPath))
            {
                var token = cancellation.Token;
                (artwork, tint) = await Task.Run(async () =>
                {
                    var loaded = await LoadArtworkAsync(artworkPath, token);
                    Color? color = loaded is null ? null : ArtworkColorExtractor.GetOrExtract(artworkPath, loaded.Value.Bytes);
                    return (loaded, color);
                }, token);
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
            {
                if (cancellation.IsCancellationRequested || _closed || _dismissing)
                {
                    artwork?.Bitmap.Dispose();
                    return;
                }
                ApplyMediaSession(session, trackKey, artworkPath, artwork?.Bitmap, tint, replaceArtwork);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Keep lock screen responsive even if media service fails
        }
        finally
        {
            if (ReferenceEquals(_mediaRefreshCancellation, cancellation))
                _mediaRefreshInFlight = false;
        }
    }

    private void ApplyMediaSession(
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
                _collapsedArtworkImage.Source = artwork;
                _expandedArtworkImage.Source = artwork;
                _morphingArtworkImage.Source = artwork;
                _lockCanvasFallbackImage.Source = artwork;
                _ = RetireArtworkAsync(previousArtwork);
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

        var hadMedia = _hasMediaSession;
        _hasMediaSession = session is not null;
        _isPlaying = session?.IsPlaying == true;

        if (session is not null)
        {
            _mediaDurationSeconds = Math.Max(0, session.DurationSeconds);
            _mediaPositionSeconds = _mediaDurationSeconds > 0
                ? Math.Clamp(session.PositionSeconds, 0, _mediaDurationSeconds)
                : Math.Max(0, session.PositionSeconds);
            _lastPositionSyncAt = DateTimeOffset.UtcNow;
            _lastPeriodicSessionSyncAt = DateTimeOffset.UtcNow;
        }
        else
        {
            _mediaDurationSeconds = 0;
            _mediaPositionSeconds = 0;
        }

        var titleText = session?.Title ?? Loc.Get("Not playing");
        var artistText = session is null
            ? string.Empty
            : string.IsNullOrWhiteSpace(session.Artist) ? SourceLabel(session.SourceIdentity) : session.Artist;
        var playIcon = _isPlaying ? "pause" : "play";
        var playTip = _isPlaying ? Loc.Get("Pause") : Loc.Get("Play");

        _collapsedTitleText.Text = titleText;
        _collapsedArtistText.Text = artistText;
        _collapsedPlayPauseGlyph.Icon = playIcon;
        ToolTip.SetTip(_collapsedPlayPauseButton, playTip);

        _expandedTitleText.Text = titleText;
        _expandedArtistText.Text = artistText;
        _expandedPlayPauseGlyph.Icon = playIcon;
        ToolTip.SetTip(_expandedPlayPauseButton, playTip);

        var sourceGlyph = SourceGlyphFor(session?.SourceIdentity);
        if (!string.Equals(_sourceIdentity, session?.SourceIdentity, StringComparison.Ordinal))
        {
            _sourceIdentity = session?.SourceIdentity;
            _sourceIcon = ResolveSourceIcon(_sourceIdentity);
        }
        var sourceIcon = _sourceIcon;

        ApplyArtworkControlState(
            _collapsedArtworkImage, _collapsedArtworkFallback, _collapsedSourceIcon, _collapsedSourceGlyph,
            _artwork, sourceIcon, sourceGlyph);
        ApplyArtworkControlState(
            _expandedArtworkImage, _expandedArtworkFallback, _expandedSourceIcon, _expandedSourceGlyph,
            _artwork, sourceIcon, sourceGlyph);
        ApplyArtworkControlState(
            _morphingArtworkImage, _morphingArtworkFallback, _morphingSourceIcon, _morphingSourceGlyph,
            _artwork, sourceIcon, sourceGlyph);
        _lockCanvasFallbackImage.IsVisible = _artwork is not null;

        UpdateMediaTintLayers();
        UpdateSpectrumBarTint();
        UpdateProgressDisplay(_mediaPositionSeconds, _mediaDurationSeconds);
        UpdateSpectrumAndPositionTimers();

        if (_hasMediaSession != hadMedia)
        {
            if (!_hasMediaSession && IsMediaExpanded)
            {
                _ = SetMediaExpandedAsync(false);
            }
            AnimateMediaPresence(_hasMediaSession);
        }
        else
        {
            ApplyVisualState();
        }

        if (session is not null)
        {
            var canvasKey = $"{session.Title}\x1f{session.Artist}\x1f{session.SourceUrl}";
            if (!string.Equals(_canvasTrackKey, canvasKey, StringComparison.Ordinal))
            {
                _canvasTrackKey = canvasKey;
                // Never show the previous track's Canvas while resolving this one.
                _resolvedCanvasVideoPath = null;
                _canvasVideoAttached = false;
                _lockCanvasWallpaper.ClearWallpaper();
                ApplyVisualState();
                _ = ResolveTrackCanvasAsync(session, canvasKey);
            }
        }
        else
        {
            _canvasResolveCancellation?.Cancel();
            _canvasTrackKey = null;
            _resolvedCanvasVideoPath = null;
            _canvasVideoAttached = false;
            _lockCanvasWallpaper.ClearWallpaper();
        }
    }

    private async Task ResolveTrackCanvasAsync(MediaSession session, string expectedCanvasKey)
    {
        _canvasResolveCancellation?.Cancel();
        _canvasResolveCancellation?.Dispose();
        var cancellation = _canvasResolveCancellation = new CancellationTokenSource();

        try
        {
            var videoPath = await SpotifyCanvasResolver.ResolveAsync(session, cancellation.Token);
            if (cancellation.IsCancellationRequested || !string.Equals(_canvasTrackKey, expectedCanvasKey, StringComparison.Ordinal))
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_closed || _dismissing || cancellation.IsCancellationRequested
                    || !string.Equals(_canvasTrackKey, expectedCanvasKey, StringComparison.Ordinal)) return;

                _resolvedCanvasVideoPath = videoPath;
                if (!string.IsNullOrWhiteSpace(videoPath) && IsMediaExpanded)
                {
                    _canvasVideoAttached = true;
                    _lockCanvasWallpaper.SetMediaVideo(videoPath);
                }
                else
                {
                    _canvasVideoAttached = false;
                    _lockCanvasWallpaper.ClearWallpaper();
                }

                ApplyVisualState();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private static void ApplyArtworkControlState(
        Image artworkImage,
        Border fallbackBorder,
        Image sourceIconImage,
        DaisyIcon sourceGlyphIcon,
        Bitmap? artwork,
        IImage? sourceIcon,
        string sourceGlyph)
    {
        artworkImage.Source = artwork;
        artworkImage.IsVisible = artwork is not null;
        fallbackBorder.IsVisible = artwork is null;
        sourceIconImage.Source = sourceIcon;
        sourceIconImage.IsVisible = sourceIcon is not null;
        sourceGlyphIcon.Icon = sourceGlyph;
        sourceGlyphIcon.Weight = sourceGlyph == "music-note" ? DaisyIconWeight.Fill : DaisyIconWeight.Regular;
        sourceGlyphIcon.IsVisible = sourceIcon is null;
    }

    private void OnCollapsedArtworkPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        _ = SetMediaExpandedAsync(true);
    }

    private void OnExpandedArtworkPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        _ = SetMediaExpandedAsync(false);
    }

    private void OnProgressTrackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mediaDurationSeconds <= 0) return;
        var point = e.GetPosition(_expandedProgressTrack);
        var trackWidth = _expandedProgressTrack.Bounds.Width;
        if (trackWidth <= 0) return;

        e.Handled = true;
        var ratio = Math.Clamp(point.X / trackWidth, 0, 1);
        var targetSeconds = ratio * _mediaDurationSeconds;
        _mediaPositionSeconds = targetSeconds;
        _lastPositionSyncAt = DateTimeOffset.UtcNow;
        UpdateProgressDisplay(_mediaPositionSeconds, _mediaDurationSeconds);
        _ = _mediaService.SeekAsync(targetSeconds, CancellationToken.None);
    }

    public Task SetMediaExpandedAsync(bool expanded)
    {
        if (_isSecondary || (expanded && !_hasMediaSession))
        {
            return Task.CompletedTask;
        }

        if (_closed || _dismissing) return Task.CompletedTask;
        if (_mediaExpandTarget == (expanded ? 1 : 0))
            return _mediaExpandCompletion?.Task ?? Task.CompletedTask;

        _mediaExpandTimer.Stop();
        _mediaExpandCompletion?.TrySetResult();
        _mediaExpandCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mediaExpandTarget = expanded ? 1 : 0;
        UpdateMediaPositionInterval();
        if (expanded && !_canvasVideoAttached && _resolvedCanvasVideoPath is { } videoPath)
        {
            _canvasVideoAttached = true;
            _lockCanvasWallpaper.SetMediaVideo(videoPath);
        }
        _mediaExpandDuration = Application.Current is App { ReduceMotionEnabled: true }
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(expanded ? 420 : 360);
        _mediaExpandStart = _mediaExpandProgress;
        _mediaExpandTime.Restart();

        _collapsedMediaHost.IsVisible = true;
        _expandedMediaHost.IsVisible = true;
        UpdateLockLayout();
        UpdateLayout();
        _collapsedArtworkRect = GetRestingCollapsedArtworkRect();
        _expandedArtworkRect = GetRestingExpandedArtworkRect();

        AdvanceMediaExpansion();
        if (!_mediaExpandCompletion.Task.IsCompleted)
        {
            _mediaExpandTimer.Start();
        }

        if (!expanded && !_isSecondary)
        {
            PasswordBox.Focus();
        }

        return _mediaExpandCompletion.Task;
    }

    private void AdvanceMediaExpansion()
    {
        var elapsed = _mediaExpandTime.Elapsed.TotalSeconds;
        var progress = _mediaExpandDuration <= TimeSpan.Zero
            ? 1
            : Math.Clamp(elapsed / _mediaExpandDuration.TotalSeconds, 0, 1);

        // Smooth quintic ease-out for silky FLIP artwork and card transitions
        var eased = 1 - Math.Pow(1 - progress, 4);
        var value = _mediaExpandStart + (_mediaExpandTarget - _mediaExpandStart) * eased;

        if (progress >= 1 || Application.Current is App { ReduceMotionEnabled: true })
        {
            _mediaExpandProgress = _mediaExpandTarget;
            ApplyVisualState();
            _mediaExpandTimer.Stop();
            _mediaExpandCompletion?.TrySetResult();
        }
        else
        {
            _mediaExpandProgress = Math.Clamp(value, 0, 1);
            ApplyVisualState();
        }
    }

    private void AnimateMediaPresence(bool visible)
    {
        _mediaPresenceTimer.Stop();
        _mediaPresenceTarget = visible ? 1 : 0;
        _mediaPresenceDuration = Application.Current is App { ReduceMotionEnabled: true }
            ? TimeSpan.Zero
            : DurationFromResource(visible ? "MotionDurationEnter" : "MotionDurationExit", visible ? 280 : 160);
        _mediaPresenceStart = _mediaPresenceProgress;
        _mediaPresenceTime.Restart();
        AdvanceMediaPresence();
        if (Math.Abs(_mediaPresenceProgress - _mediaPresenceTarget) > 0.001)
        {
            _mediaPresenceTimer.Start();
        }
    }

    private void AdvanceMediaPresence()
    {
        var elapsed = _mediaPresenceTime.Elapsed.TotalSeconds;
        var progress = _mediaPresenceDuration <= TimeSpan.Zero
            ? 1
            : Math.Clamp(elapsed / _mediaPresenceDuration.TotalSeconds, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        var value = _mediaPresenceStart + (_mediaPresenceTarget - _mediaPresenceStart) * eased;
        if (progress >= 1 || Application.Current is App { ReduceMotionEnabled: true })
        {
            _mediaPresenceProgress = _mediaPresenceTarget;
            _mediaPresenceTimer.Stop();
        }
        else
        {
            _mediaPresenceProgress = Math.Clamp(value, 0, 1);
        }

        ApplyVisualState();
    }

    private TimeSpan DurationFromResource(string key, int fallbackMs) =>
        this.TryFindResource(key, out var resource) && resource is TimeSpan duration
            ? duration
            : TimeSpan.FromMilliseconds(fallbackMs);

    private void UpdateLiveMediaProgress()
    {
        if (!_hasMediaSession) return;

        var now = DateTimeOffset.UtcNow;
        var livePosition = _mediaPositionSeconds;
        if (_isPlaying)
        {
            livePosition += (now - _lastPositionSyncAt).TotalSeconds;
            if (_mediaDurationSeconds > 0)
            {
                livePosition = Math.Clamp(livePosition, 0, _mediaDurationSeconds);
            }
        }

        if (!_mediaRefreshInFlight && (now - _lastPeriodicSessionSyncAt).TotalSeconds >= 2)
        {
            _lastPeriodicSessionSyncAt = now;
            _ = RefreshMediaAsync();
        }

        UpdateProgressDisplay(livePosition, _mediaDurationSeconds);
    }

    private void UpdateProgressDisplay(double positionSeconds, double durationSeconds)
    {
        var clampedPos = Math.Max(0, positionSeconds);
        var clampedDur = Math.Max(0, durationSeconds);
        if (clampedDur > 0)
        {
            clampedPos = Math.Min(clampedPos, clampedDur);
        }

        var remaining = clampedDur > 0 ? Math.Max(0, clampedDur - clampedPos) : 0;

        _expandedElapsedText.Text = FormatMediaTime(clampedPos, negative: false);
        _expandedRemainingText.Text = FormatMediaTime(remaining, negative: true);
        UpdateProgressFillWidth(clampedPos, clampedDur);
    }

    private void UpdateProgressFillWidth()
    {
        var livePosition = _mediaPositionSeconds;
        if (_isPlaying)
        {
            livePosition += (DateTimeOffset.UtcNow - _lastPositionSyncAt).TotalSeconds;
        }
        UpdateProgressFillWidth(livePosition, _mediaDurationSeconds);
    }

    private void UpdateProgressFillWidth(double positionSeconds, double durationSeconds)
    {
        var trackWidth = _expandedProgressTrack.Bounds.Width;
        if (trackWidth <= 0)
        {
            trackWidth = Math.Max(0, _expandedMediaBorder.Width - 124);
        }

        var ratio = durationSeconds > 0
            ? Math.Clamp(positionSeconds / durationSeconds, 0, 1)
            : 0;
        // Preserve fractional pixels: rounding makes a slow timeline jump one
        // whole pixel at a time even when its position is sampled every frame.
        _expandedProgressFill.Width = trackWidth * ratio;
    }

    internal static string FormatMediaTime(double seconds, bool negative = false) =>
        MediaPlaybackTime.Format(seconds, negative);

    private async void OnMediaPreviousClicked(object? sender, RoutedEventArgs e) =>
        await RunMediaCommandAsync(_mediaService.PreviousAsync);

    private async void OnMediaPlayPauseClicked(object? sender, RoutedEventArgs e) =>
        await RunMediaCommandAsync(_mediaService.PlayPauseAsync);

    private async void OnMediaNextClicked(object? sender, RoutedEventArgs e) =>
        await RunMediaCommandAsync(_mediaService.NextAsync);

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

    private void BuildSpectrumBars()
    {
        if (_collapsedSpectrumBars.Count == 0 && _collapsedSpectrumCanvas is { } cCanvas)
        {
            const double idleScale = 0.12;
            for (var index = 0; index < SpectrumColumns; index++)
            {
                var bar = CreateSpectrumBar(idleScale);
                Canvas.SetLeft(bar, 3 + index * 4);
                cCanvas.Children.Add(bar);
                _collapsedSpectrumBars.Add(bar);
            }
        }

        if (_expandedSpectrumBars.Count == 0 && _expandedSpectrumCanvas is { } eCanvas)
        {
            const double idleScale = 0.12;
            for (var index = 0; index < SpectrumColumns; index++)
            {
                var bar = CreateSpectrumBar(idleScale);
                Canvas.SetLeft(bar, 3 + index * 4);
                eCanvas.Children.Add(bar);
                _expandedSpectrumBars.Add(bar);
            }
        }

        UpdateSpectrumBarTint();
    }

    private Border CreateSpectrumBar(double idleScale)
    {
        var bar = new Border
        {
            Width = 3,
            Height = Math.Max(3, 24 * idleScale),
            CornerRadius = new CornerRadius(999),
            ClipToBounds = true,
        };
        Canvas.SetTop(bar, (28 - bar.Height) / 2);

        var tintLayer = new Border
        {
            CornerRadius = new CornerRadius(999),
            IsHitTestVisible = false,
            Opacity = 0
        };
        bar.Child = tintLayer;
        _spectrumTintLayers.Add(tintLayer);
        bar.Classes.Add("spectrumBar");
        return bar;
    }

    private void UpdateMediaTintLayers()
    {
        var brush = MediaWidgetMaterial.CreateTint(this, _artworkTint);
        _collapsedArtworkTint.Background = brush;
        this.FindControl<Border>("ExpandedArtworkTint")!.Background = brush;
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

    private void UpdateMediaPositionInterval()
    {
        // Only the visible, playing timeline needs frame-rate updates. Session
        // synchronization stays on its independent two-second cadence.
        _mediaPositionTimer.Interval = TimeSpan.FromMilliseconds(
            IsMediaExpanded && _isPlaying ? 16 : 250);
    }

    private void UpdateSpectrumAndPositionTimers()
    {
        UpdateMediaPositionInterval();
        if (_hasMediaSession && !_isSecondary)
        {
            _mediaPositionTimer.Start();
        }
        else
        {
            _mediaPositionTimer.Stop();
        }

        if (_hasMediaSession && _isPlaying && !_isSecondary)
        {
            if (_audioSpectrumService is null)
            {
                try
                {
                    _audioSpectrumService = new LinuxAudioSpectrumService();
                }
                catch
                {
                    _audioSpectrumService = null;
                }
            }
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

                if (index < _collapsedSpectrumBars.Count)
                {
                    SetSpectrumBarScale(_collapsedSpectrumBars[index], scale);
                }
                if (index < _expandedSpectrumBars.Count)
                {
                    SetSpectrumBarScale(_expandedSpectrumBars[index], scale);
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
        if (_spectrumIsIdle) return;

        foreach (var bar in _collapsedSpectrumBars)
        {
            SetSpectrumBarScale(bar, 0.12);
        }
        foreach (var bar in _expandedSpectrumBars)
        {
            SetSpectrumBarScale(bar, 0.12);
        }

        _spectrumIsIdle = true;
    }

    private static void SetSpectrumBarScale(Border bar, double scale)
    {
        bar.Height = Math.Max(bar.Width, 24 * scale);
        Canvas.SetTop(bar, (28 - bar.Height) / 2);
    }

    private static async Task RetireArtworkAsync(Bitmap? bitmap)
    {
        if (bitmap is null) return;
        // Image source transitions and queued layout/render work can still refer
        // to the outgoing bitmap after all four base sources have changed.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        bitmap.Dispose();
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

    private void OnHintClicked(object? sender, RoutedEventArgs e)
    {
        SubtitleText.IsVisible = true;
        SubtitleText.Text = Loc.Format("Hint: Enter your password for '{0}'.", UserNameText.Text);
    }

    private async Task ShakePasswordBoxAsync()
    {
        if (_isShaking) return;
        _isShaking = true;
        try
        {
            var transform = new TranslateTransform();
            PasswordPillBorder.RenderTransform = transform;
            int[] offsets = [-12, 12, -9, 9, -5, 5, 0];
            foreach (var offset in offsets)
            {
                transform.X = offset;
                _nativeBlur?.RefreshImmediately();
                await Task.Delay(25);
            }
            PasswordPillBorder.RenderTransform = null;
            _nativeBlur?.RefreshImmediately();
        }
        finally
        {
            _isShaking = false;
        }
    }

    private async void OnUnlockClicked(object? sender, RoutedEventArgs e)
    {
        if (_isAuthenticating || _dismissing) return;

        var password = PasswordBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(password))
        {
            ErrorMessageText.Text = Loc.Get("Please enter your password.");
            ErrorMessageText.IsVisible = true;
            PasswordBox.Focus();
            return;
        }

        _isAuthenticating = true;
        ErrorMessageText.IsVisible = false;
        PasswordBox.IsReadOnly = true;

        try
        {
            var success = await _lockService.AuthenticateAsync(password);
            if (success)
            {
                await DismissAsync();
            }
            else
            {
                ErrorMessageText.Text = Loc.Get("Incorrect password. Please try again.");
                ErrorMessageText.IsVisible = true;
                PasswordBox.Text = string.Empty;
                PasswordBox.IsReadOnly = false;
                _isAuthenticating = false;

                await ShakePasswordBoxAsync();
                PasswordBox.Focus();
            }
        }
        catch (Exception ex)
        {
            ErrorMessageText.Text = Loc.Format("Authentication error: {0}", ex.Message);
            ErrorMessageText.IsVisible = true;
            PasswordBox.IsReadOnly = false;
            _isAuthenticating = false;
            PasswordBox.Focus();
        }
    }

    public Task DismissAsync() => _dismissal ??= DismissCoreAsync();

    private async Task DismissCoreAsync()
    {
        _dismissing = true;
        if (!_isSecondary) Dismissing?.Invoke();
        _clockAnimation.Stop();
        _clockTimer.Stop();
        _batteryTimer.Stop();
        _spectrumTimer.Stop();
        _mediaPositionTimer.Stop();
        await AnimatePresenceAsync(false);
        if (!_isSecondary) _lockService.Unlock();
        Close();
    }

    private Task AnimatePresenceAsync(bool entering)
    {
        _presenceTimer.Stop();
        _presenceCompletion?.TrySetResult();
        _presenceCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _presenceTarget = entering ? 1 : 0;
        _presenceDuration = Application.Current is App { ReduceMotionEnabled: true } ? TimeSpan.Zero :
            this.TryFindResource(entering ? "MotionDurationEnter" : "MotionDurationExit", out var resource) && resource is TimeSpan duration
                ? duration : TimeSpan.FromMilliseconds(entering ? 280 : 140);
        _presenceStart = _presenceProgress;
        _presenceTime.Restart();
        AdvancePresence();
        if (!_presenceCompletion.Task.IsCompleted) _presenceTimer.Start();
        return _presenceCompletion.Task;
    }

    private void AdvancePresence()
    {
        var now = _presenceTime.Elapsed.TotalSeconds;
        var progress = _presenceDuration <= TimeSpan.Zero ? 1 :
            Math.Clamp(now / _presenceDuration.TotalSeconds, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        var position = _presenceStart + (_presenceTarget - _presenceStart) * eased;
        if (progress >= 1 || Application.Current is App { ReduceMotionEnabled: true })
        {
            ApplyPresence(_presenceTarget);
            _presenceTimer.Stop();
            _presenceCompletion?.TrySetResult();
        }
        else ApplyPresence(Math.Clamp(position, 0, 1));
    }

    private void ApplyPresence(double progress)
    {
        _presenceProgress = progress;
        ApplyVisualState();
    }

    private void ApplyVisualState()
    {
        if (_closed) return;
        void Update()
        {
            var presence = _presenceProgress;
            var mediaPresence = _mediaPresenceProgress;
            var expand = _mediaExpandProgress;
            var showMedia = !_isSecondary && (_hasMediaSession || mediaPresence > 0.001);

            DeptForeground.RefreshLayout();
            // 1. Clock Area stays anchored at top
            ClockArea.Opacity = presence;
            _clockPresence.Y = -14 * (1 - presence);

            // 2. Credentials & Corner Controls smoothly fade/slide out when media expands to fullscreen
            var chromeFactor = Math.Clamp(1 - expand * 1.45, 0, 1);
            CredentialsPanel.Opacity = presence * chromeFactor;
            CornerControlsGrid.Opacity = presence * chromeFactor;
            _credentialsPresence.Y = 14 * (1 - presence) + 18 * expand;
            _cornerControlsTransform.Y = 14 * (1 - presence) + 18 * expand;
            CredentialsPanel.IsHitTestVisible = expand < 0.1;
            CornerControlsGrid.IsHitTestVisible = expand < 0.1;

            // Hide bottom-left battery text when bottom-left media widget is shown so they never collide
            BatteryInfo.Opacity = Math.Clamp(1 - mediaPresence * 1.6, 0, 1);

            // 3. Collapsed Bottom-Left Media Widget
            var collapsedCardOpacity = presence * mediaPresence * Math.Clamp(1 - expand * 1.65, 0, 1);
            _collapsedMediaHost.IsVisible = showMedia && expand < 0.999;
            _collapsedMediaHost.IsHitTestVisible = showMedia && expand < 0.1;
            _collapsedMediaBorder.Opacity = collapsedCardOpacity;
            _collapsedCardTranslate.X = 22 * expand;
            _collapsedCardTranslate.Y = 14 * (1 - presence) + 12 * (1 - mediaPresence) - 16 * expand;
            var collapsedScale = (0.96 + 0.04 * mediaPresence) * (1 - 0.05 * expand);
            _collapsedCardScale.ScaleX = _collapsedCardScale.ScaleY = collapsedScale;

            // 4. Expanded Fullscreen Media View
            var expandedCardOpacity = presence * mediaPresence * Math.Clamp((expand - 0.18) / 0.82, 0, 1);
            _expandedMediaHost.IsVisible = showMedia && expand > 0.001;
            _expandedMediaHost.IsHitTestVisible = showMedia && expand > 0.9;
            _expandedMediaBorder.Opacity = expandedCardOpacity;
            _expandedCardTranslate.Y = 24 * (1 - expand);
            var expandedScale = 0.94 + 0.06 * expand;
            _expandedCardScale.ScaleX = _expandedCardScale.ScaleY = expandedScale;

            // 5. FLIP Hero Artwork Bridge between Collapsed and Expanded states
            if (!showMedia)
            {
                _morphingArtworkBorder.IsVisible = false;
                _collapsedArtworkButton.Opacity = 1;
                _expandedArtworkContainer.Opacity = 0;
            }
            else if (expand <= 0.001)
            {
                _morphingArtworkBorder.IsVisible = false;
                _collapsedArtworkButton.Opacity = 1;
                _expandedArtworkContainer.Opacity = 0;
            }
            else if (expand >= 0.999)
            {
                _morphingArtworkBorder.IsVisible = false;
                _collapsedArtworkButton.Opacity = 0;
                _expandedArtworkContainer.Opacity = presence * mediaPresence;
            }
            else
            {
                _collapsedArtworkButton.Opacity = 0;
                _expandedArtworkContainer.Opacity = 0;

                var startRect = _collapsedArtworkRect;
                var endRect = _expandedArtworkRect;
                var arcLift = -Math.Sin(expand * Math.PI) * 18;

                var currentX = startRect.X + (endRect.X - startRect.X) * expand;
                var currentY = startRect.Y + (endRect.Y - startRect.Y) * expand + arcLift;
                var currentW = Math.Max(1, startRect.Width + (endRect.Width - startRect.Width) * expand);
                var currentH = Math.Max(1, startRect.Height + (endRect.Height - startRect.Height) * expand);
                var currentRadius = 14 + (32 - 14) * expand;
                var innerRadius = 14 + (30 - 14) * expand;
                var frameInset = expand;
                var shadowAlpha = (byte)Math.Round(0x66 * expand);

                _morphingArtworkBorder.IsVisible = true;
                _morphingArtworkBorder.Opacity = presence * mediaPresence;
                _morphingArtworkBorder.Width = currentW;
                _morphingArtworkBorder.Height = currentH;
                // The expanded artwork has a two-stage 1 px frame, while the
                // compact artwork is edge-to-edge. Interpolate those insets so
                // collapse ends with the exact same crop as the resting widget.
                _morphingArtworkBorder.Padding = new Thickness(frameInset);
                _morphingArtworkInner.Margin = new Thickness(frameInset);
                _morphingArtworkBorder.CornerRadius = new CornerRadius(currentRadius);
                _morphingArtworkHighlight.CornerRadius = new CornerRadius(Math.Max(0, currentRadius - 1));
                _morphingArtworkHighlight.Opacity = expand;
                _morphingArtworkInner.CornerRadius = new CornerRadius(innerRadius);
                // Match the large artwork shadow at the expanded endpoint and
                // remove it continuously before handing off to the flat compact
                // artwork. This prevents the final-frame elevation pop.
                _morphingArtworkBorder.BoxShadow = new BoxShadows(new BoxShadow
                {
                    OffsetY = 18 * expand,
                    Blur = 44 * expand,
                    Color = Color.FromArgb(shadowAlpha, 0, 0, 0)
                });
                _morphingArtworkTranslate.X = currentX;
                _morphingArtworkTranslate.Y = currentY;
            }

            // 6. Spotify Canvas Video / Fallback Blurred Artwork Wallpaper Crossfade
            var canvasAlpha = showMedia ? presence * mediaPresence * expand : 0;
            if (canvasAlpha <= 0.001)
            {
                UpdateCanvasReveal(false, immediate: true);
                _lockCanvasLayer.IsVisible = false;
                _lockCanvasLayer.Opacity = 0;
                _lockCanvasWallpaper.IsVisible = false;
                if (_canvasVideoAttached && !IsMediaExpanded)
                {
                    // Native video surfaces outlive visual-tree visibility. Release
                    // the surface after the collapse finishes, then reload on expand.
                    _canvasVideoAttached = false;
                    _lockCanvasWallpaper.ClearWallpaper();
                }
                LockWallpaper.Opacity = 1;
            }
            else
            {
                _lockCanvasLayer.IsVisible = true;
                var hasVideoPath = !string.IsNullOrWhiteSpace(_resolvedCanvasVideoPath);
                _lockCanvasWallpaper.IsVisible = hasVideoPath;

                _lockCanvasLayer.Opacity = canvasAlpha;
                var videoPresented = hasVideoPath
                    && _lockCanvasWallpaper.PresentationState == WallpaperLayer.WallpaperPresentationState.Presented;
                // A native subsurface sits beneath the Avalonia scene. Reveal it
                // through the desktop wallpaper; visual-tree video fades normally.
                var nativeVideo = videoPresented && _lockCanvasWallpaper.UsesNativeSubsurface;
                UpdateCanvasReveal(videoPresented, immediate: !hasVideoPath);
                // Native video is below the entire scene. Fade both opaque covers
                // with the same clock; independent transitions expose the desktop
                // wallpaper between artwork and video, producing a flash.
                LockWallpaper.Opacity = nativeVideo ? 1 - canvasAlpha * _canvasReveal : 1;
                _lockCanvasWallpaper.Opacity = nativeVideo ? 1 : _canvasReveal;
                _lockCanvasFallbackPanel.Opacity = 1 - _canvasReveal;
            }
        }

        // Publish one coherent mask after all values and transform caches have
        // advanced, rather than several partially updated masks per frame.
        if (_nativeBlur is { } blur) blur.UpdateGeometry(Update);
        else Update();
    }

    private void UpdateCanvasReveal(bool ready, bool immediate = false)
    {
        var target = ready ? 1d : 0d;
        if (immediate || Application.Current is App { ReduceMotionEnabled: true })
        {
            _canvasReveal = _canvasRevealTarget = target;
            _canvasRevealTimer.Stop();
            return;
        }
        if (_canvasRevealTarget != target)
        {
            _canvasRevealStart = _canvasReveal;
            _canvasRevealTarget = target;
            _canvasRevealTime.Restart();
            _canvasRevealTimer.Start();
        }
        if (!_canvasRevealTimer.IsEnabled) return;
        var progress = Math.Clamp(_canvasRevealTime.Elapsed.TotalMilliseconds / 240, 0, 1);
        var eased = progress * progress * (3 - 2 * progress);
        _canvasReveal = _canvasRevealStart + (target - _canvasRevealStart) * eased;
        if (progress >= 1) _canvasRevealTimer.Stop();
    }

    private Rect GetRestingCollapsedArtworkRect()
    {
        var w = _collapsedArtworkButton.Width > 0 ? _collapsedArtworkButton.Width : 126;
        var h = _collapsedArtworkButton.Height > 0 ? _collapsedArtworkButton.Height : 126;
        var hostOrigin = _collapsedMediaHost.TranslatePoint(default, RootGrid);
        var artInHost = _collapsedArtworkButton.TranslatePoint(default, _collapsedMediaBorder);
        if (hostOrigin is { } ho && artInHost is { } ai && _collapsedMediaHost.Bounds.Width > 0)
        {
            return new Rect(ho.X + ai.X, ho.Y + ai.Y, w, h);
        }

        var rootH = RootGrid.Bounds.Height > 0 ? RootGrid.Bounds.Height : ClientSize.Height;
        var cardH = _collapsedMediaBorder.Height > 0 ? _collapsedMediaBorder.Height : 160;
        var x = 24 + 2 + 16;
        var y = Math.Max(0, rootH - 24 - cardH + (cardH - h) / 2);
        return new Rect(x, y, w, h);
    }

    private Rect GetRestingExpandedArtworkRect()
    {
        var w = _expandedArtworkContainer.Width > 0 ? _expandedArtworkContainer.Width : 312;
        var h = _expandedArtworkContainer.Height > 0 ? _expandedArtworkContainer.Height : 312;
        var origin = _expandedArtworkContainer.TranslatePoint(default, RootGrid);
        if (origin is { } o && _expandedArtworkContainer.Bounds.Width > 0)
        {
            return new Rect(o.X, o.Y, w, h);
        }

        var rootW = RootGrid.Bounds.Width > 0 ? RootGrid.Bounds.Width : ClientSize.Width;
        var rootH = RootGrid.Bounds.Height > 0 ? RootGrid.Bounds.Height : ClientSize.Height;
        var x = (rootW - w) / 2;
        var bottomMargin = _expandedMediaStack.Margin.Bottom;
        var cardH = _expandedMediaBorder.Bounds.Height > 0 ? _expandedMediaBorder.Bounds.Height : 136;
        var spacing = _expandedMediaStack.Spacing;
        var y = Math.Max(0, rootH - bottomMargin - cardH - spacing - h);
        return new Rect(x, y, w, h);
    }

    private async void OnSleepClicked(object? sender, RoutedEventArgs e)
    {
        await _powerService.SuspendAsync();
    }

    private async void OnRestartClicked(object? sender, RoutedEventArgs e)
    {
        await _powerService.RebootAsync();
    }

    private async void OnShutdownClicked(object? sender, RoutedEventArgs e)
    {
        await _powerService.ShutdownAsync();
    }
}
