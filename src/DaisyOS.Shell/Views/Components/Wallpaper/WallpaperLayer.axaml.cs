using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Services.Wallpaper;
using DaisyOS.Shell.Services.Wallpaper.VideoWallpaper;

namespace DaisyOS.Shell.Views.Components.Wallpaper;

/// <summary>The wallpaper visual shared by the desktop and console shell experiences.</summary>
public partial class WallpaperLayer : UserControl
{
    public enum WallpaperPresentationState { Loading, Presented, Paused, Failed }

    private const double MaximumParallaxOffset = 0;
    private const double WallpaperScale = 1.0;
    private const double EdgeSafetyInset = 0;
    private const int WallpaperFadeHalfDurationMilliseconds = 160;
    private const int MaximumWallpaperPresentationPollRate = 60;
    private const int MaximumPresentationWidth = 7680;
    private const int MaximumPresentationHeight = 4320;
    private const long MaximumPresentationBytes = (long)MaximumPresentationWidth * MaximumPresentationHeight * 4;
    private readonly DispatcherTimer _videoFrameTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly TranslateTransform _parallaxTranslation = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private App? _app;
    private WallpaperImageService? _wallpaperImageService;
    private readonly List<Bitmap> _retiredWallpaperBitmaps = [];
    private VideoWallpaperService? _videoWallpaperService;
    private MpvWallpaperSession? _mpvSession;
    private WriteableBitmap? _mpvPoster;
    private CancellationTokenSource? _videoWork;
    private IDisposable? _activeVideoFile;
    private VideoWallpaperOptimization.PlaybackDemand? _videoDemand;
    private VideoMetadata? _videoMetadata;
    private string? _videoSourcePath;
    private bool _optimizationStarted;
    private bool _usingOptimizedVideo;
    private int _optimizedWidth, _optimizedHeight;
    private bool _nativeRecoveryAttempted;
    private DateTimeOffset _statisticsWindowStart;
    private long _windowPresented, _windowDiscarded, _windowEngineDropped;

    private GpuVideoFrameImage? _gpuVideoImage;
    private WriteableBitmap? _videoBitmapA;
    private WriteableBitmap? _videoBitmapB;
    private bool _useBitmapA;
    private WallpaperRenderMetrics _wallpaperMetrics;
    private double _targetOffsetX;
    private double _targetOffsetY;
    private double _offsetX;
    private double _offsetY;
    private double _horizontalTravelLimit;
    private double _verticalTravelLimit;
    private int _refreshRequestVersion;
    private bool _isLoaded;
    private int _wallpaperTransitionVersion;
    private WallpaperResources? _pendingVideoTransitionResources;
    private bool _reportedVideoFrameFailure;
    private DateTimeOffset _lastVideoFrameAt;
    // Native layer-shell windows can load before KWin has configured their
    // real output size. Keep the requested source until that size exists so
    // an accidental 1x1 pre-render can never become the desktop wallpaper.
    private string? _requestedWallpaperUri;
    private string? _presentedWallpaperUri;
    private string? _videoCandidateWallpaperUri;
    private bool _isVideoPrerolling;
    private TaskCompletionSource<bool>? _presentationCompletion;

    public WallpaperPresentationState PresentationState { get; private set; } = WallpaperPresentationState.Loading;

    public static readonly StyledProperty<bool> IgnoreDesktopOcclusionProperty =
        AvaloniaProperty.Register<WallpaperLayer, bool>(nameof(IgnoreDesktopOcclusion));

    public bool IgnoreDesktopOcclusion
    {
        get => GetValue(IgnoreDesktopOcclusionProperty);
        set => SetValue(IgnoreDesktopOcclusionProperty, value);
    }

    public static readonly StyledProperty<string?> WallpaperUriProperty =
        AvaloniaProperty.Register<WallpaperLayer, string?>(nameof(WallpaperUri));

    public string? WallpaperUri
    {
        get => GetValue(WallpaperUriProperty);
        set => SetValue(WallpaperUriProperty, value);
    }

    static WallpaperLayer()
    {
        WallpaperUriProperty.Changed.AddClassHandler<WallpaperLayer>((layer, e) =>
        {
            if (layer._isLoaded)
            {
                var uri = e.GetNewValue<string?>() ?? layer._app?.CurrentWallpaperUri;
                if (!string.IsNullOrEmpty(uri))
                {
                    layer.RequestWallpaperLoad(uri);
                }
            }
        });
    }

    public WallpaperLayer()
    {
        InitializeComponent();
        WallpaperImage.RenderTransform = _parallaxTranslation;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => OnSizeChanged();
        _videoFrameTimer.Tick += OnVideoFrameTick;
    }

    private Window? _attachedWindow;
    private bool _isConsoleMode;
    private bool _isGameRunning;

    public void SetConsoleNavigationParallax(double position)
    {
        _targetOffsetX = 0;
        _targetOffsetY = 0;
    }

    public void SetConsoleParallaxEnabled(bool enabled)
    {
        _targetOffsetX = 0;
        _targetOffsetY = 0;
        SetConsoleModeActive(enabled);
    }

    public void SetConsoleModeActive(bool active)
    {
        if (_isConsoleMode != active)
        {
            _isConsoleMode = active;
            UpdateVisibilityAndPauseState();
        }
    }

    public void SetGameRunningState(bool running)
    {
        if (_isGameRunning != running)
        {
            _isGameRunning = running;
            UpdateVisibilityAndPauseState();
        }
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Loaded may be raised again while the shell's presentation tree is being composed.
        // Reopening the decoder in that case races the first frame and can dispose a bitmap
        // that Avalonia still has queued for rendering.
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        ApplyWallpaperGeometry();
        _app = Application.Current as App;
        if (_app is not null)
        {
            _app.WallpaperChanged += OnWallpaperChanged;
            _app.WindowTracker.OcclusionStateChanged += OnOcclusionStateChanged;
            _app.DesktopOcclusionChanged += OnOcclusionStateChanged;
        }

        _attachedWindow = TopLevel.GetTopLevel(this) as Window;
        if (_attachedWindow is not null)
        {
            _attachedWindow.PropertyChanged += OnWindowPropertyChanged;
            _attachedWindow.ScalingChanged += OnWindowScalingChanged;
            if (_attachedWindow.GetType().Name == "LockScreenWindow")
            {
                IgnoreDesktopOcclusion = true;
            }
        }

        PropertyChanged += OnLayerPropertyChanged;
        UpdateVisibilityAndPauseState();

        if (_app is not null)
        {
            var uri = WallpaperUri ?? _app.CurrentWallpaperUri;
            if (!string.IsNullOrEmpty(uri))
            {
                RequestWallpaperLoad(uri);
            }
        }
    }

    private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _isLoaded = false;
        Interlocked.Increment(ref _refreshRequestVersion);
        _videoFrameTimer.Stop();
        if (_app is not null)
        {
            _app.WallpaperChanged -= OnWallpaperChanged;
            _app.WindowTracker.OcclusionStateChanged -= OnOcclusionStateChanged;
            _app.DesktopOcclusionChanged -= OnOcclusionStateChanged;
        }

        if (_attachedWindow is not null)
        {
            _attachedWindow.PropertyChanged -= OnWindowPropertyChanged;
            _attachedWindow.ScalingChanged -= OnWindowScalingChanged;
            _attachedWindow = null;
        }

        PropertyChanged -= OnLayerPropertyChanged;

        WallpaperImage.Source = null;
        WallpaperImage.Opacity = 1;
        DetachWallpaperResources().Dispose();
        _pendingVideoTransitionResources?.Dispose();
        _pendingVideoTransitionResources = null;
        foreach (var bitmap in _retiredWallpaperBitmaps) bitmap.Dispose();
        _retiredWallpaperBitmaps.Clear();
    }

    private void OnOcclusionStateChanged(object? sender, EventArgs e) => UpdateVisibilityAndPauseState();

    private void OnWindowScalingChanged(object? sender, EventArgs e)
    {
        ConfigureVideoOutput();
        RequestWallpaperRefresh();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Visual.IsVisibleProperty)
        {
            UpdateVisibilityAndPauseState();
        }
    }

    private void OnLayerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty)
        {
            UpdateVisibilityAndPauseState();
        }
    }

    private void UpdateVisibilityAndPauseState()
    {
        if (!_isLoaded || (_mpvSession is null && (_videoWallpaperService is null || !_videoWallpaperService.IsLoaded)))
        {
            return;
        }

        bool isVisible = IsEffectivelyVisible;
        bool isWindowMinimized = _attachedWindow?.WindowState == WindowState.Minimized;
        bool isOwnedWindowOccludingDesktop = !IgnoreDesktopOcclusion && (_app?.WindowTracker.IsAnyWindowMaximizedOrFullScreen ?? false);
        bool isExternalAppOccludingDesktop = !IgnoreDesktopOcclusion && (_app?.IsDesktopOccludedByExternalApp ?? false);
        bool isLockScreenOccludingDesktop = !IgnoreDesktopOcclusion && (_app?.IsLockScreenActive ?? false);

        bool shouldRender = isVisible
                            && !isWindowMinimized
                            && (_isVideoPrerolling ||
                                (!isOwnedWindowOccludingDesktop
                                 && !isExternalAppOccludingDesktop
                                 && !isLockScreenOccludingDesktop
                                 && !_isConsoleMode
                                 && !_isGameRunning));

        if (_mpvSession is not null)
        {
            _videoDemand?.SetVisible(shouldRender);
            _mpvSession.Pause(!shouldRender);
            if (shouldRender && !_videoFrameTimer.IsEnabled) _videoFrameTimer.Start();
            else if (!shouldRender) _videoFrameTimer.Stop();
            if (!_isVideoPrerolling) PresentationState = shouldRender ? WallpaperPresentationState.Presented : WallpaperPresentationState.Paused;
            return;
        }

        if (shouldRender)
        {
            _lastVideoFrameAt = DateTimeOffset.UtcNow;
            _reportedVideoFrameFailure = false;
            if (_videoWallpaperService!.IsPaused) _videoWallpaperService.Play();
            if (!_videoFrameTimer.IsEnabled)
            {
                _videoFrameTimer.Start();
            }
            if (!_isVideoPrerolling) PresentationState = WallpaperPresentationState.Presented;
        }
        else
        {
            _videoWallpaperService!.Pause();
            if (_videoFrameTimer.IsEnabled)
            {
                _videoFrameTimer.Stop();
            }
            PresentationState = WallpaperPresentationState.Paused;
        }
    }

    private void OnWallpaperChanged(object? sender, string wallpaperUri)
    {
        if (WallpaperUri is null)
        {
            RequestWallpaperLoad(wallpaperUri);
        }
    }

    private void OnSizeChanged()
    {
        ApplyWallpaperGeometry();
        ConfigureVideoOutput();

        if (_wallpaperImageService is null && _videoWallpaperService is null && _mpvSession is null)
        {
            if (_requestedWallpaperUri is not null)
            {
                RequestWallpaperLoad(_requestedWallpaperUri);
            }

            return;
        }

        RequestWallpaperRefresh();
    }

    private void RequestWallpaperLoad(string wallpaperUri)
    {
        if (string.Equals(_presentedWallpaperUri, wallpaperUri, StringComparison.Ordinal) &&
            (_wallpaperImageService is not null || _videoWallpaperService is not null || _mpvSession is not null)) return;
        _requestedWallpaperUri = wallpaperUri;
        if (!_isLoaded || !HasUsablePresentationSize())
        {
            return;
        }

        _ = LoadWallpaperAsync(wallpaperUri);
    }

    public async Task<bool> TryPresentWallpaperAsync(string wallpaperUri, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.Equals(_presentedWallpaperUri, wallpaperUri, StringComparison.Ordinal)) return true;
        _presentationCompletion?.TrySetResult(false);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _presentationCompletion = completion;
        PresentationState = WallpaperPresentationState.Loading;
        RequestWallpaperLoad(wallpaperUri);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try { return await completion.Task.WaitAsync(timeoutCts.Token); }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_presentationCompletion, completion))
            {
                Interlocked.Increment(ref _refreshRequestVersion);
                _presentationCompletion = null;
                FailVideoCandidate(wallpaperUri);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private bool HasUsablePresentationSize() => Bounds.Width > 1 && Bounds.Height > 1;

    private async Task LoadWallpaperAsync(string wallpaperUri)
    {
        if (!HasUsablePresentationSize())
        {
            return;
        }

        var requestVersion = Interlocked.Increment(ref _refreshRequestVersion);
        await _loadGate.WaitAsync();
        try
        {
            if (!_isLoaded || requestVersion != Volatile.Read(ref _refreshRequestVersion)) return;

            if (WallpaperSource.TryGetSolidColor(wallpaperUri, out var solidColor))
            {
                PresentSolidWallpaper(solidColor, TakeVisibleWallpaperResources());
                CompletePresentation(wallpaperUri, true);
                return;
            }

            string resolvedPath = ResolveWallpaperPath(wallpaperUri);
            if (!string.IsNullOrEmpty(resolvedPath) && WallpaperFileTypes.IsVideo(resolvedPath))
            {
                if (MpvWallpaperSession.Enabled && _attachedWindow is not null)
                {
                    var metadata = await VideoMetadata.ProbeAsync(resolvedPath);
                    if (!_isLoaded || requestVersion != Volatile.Read(ref _refreshRequestVersion)) return;
                    await LoadNativeVideoAsync(resolvedPath, wallpaperUri, metadata, requestVersion);
                    return;
                }

                // Opening a container and negotiating a hardware decoder can take noticeable
                // time on some drivers. Do it away from the UI thread and keep the current
                // wallpaper alive until the replacement is proven usable.
                var candidate = await Task.Run(() =>
                {
                    var service = new VideoWallpaperService();
                    if (!service.Load(resolvedPath))
                    {
                        service.Dispose();
                        return (VideoWallpaperService?)null;
                    }
                    return service;
                });

                if (candidate is null)
                {
                    CompletePresentation(wallpaperUri, false);
                    ReportWallpaperFailure("This video can’t be used as a wallpaper. Your current wallpaper is still active.");
                    return;
                }

                if (!_isLoaded || requestVersion != Volatile.Read(ref _refreshRequestVersion))
                {
                    candidate.Dispose();
                    return;
                }

                candidate.SetOutputSize(GetPresentationWidth(), GetPresentationHeight());
                candidate.SetFpsCap(MaximumWallpaperPresentationPollRate);
                var previousResources = TakeVisibleWallpaperResources();
                candidate.FrameAvailable += OnNativeVideoFrameAvailable;
                candidate.EnableFrameNotifications();
                _videoWallpaperService = candidate;
                _videoCandidateWallpaperUri = wallpaperUri;
                _isVideoPrerolling = true;
                PresentationState = WallpaperPresentationState.Loading;
                _reportedVideoFrameFailure = false;
                _lastVideoFrameAt = DateTimeOffset.UtcNow;
                var sourceFrameRate = candidate.GetStats(out var stats) && stats.SrcFps > 0
                    ? stats.SrcFps
                    : 30.0;
                _videoFrameTimer.Interval = TimeSpan.FromMilliseconds(candidate.HasFrameNotifications ? 1000 : 1000.0 / Math.Clamp(sourceFrameRate, 1.0, MaximumWallpaperPresentationPollRate));
                _pendingVideoTransitionResources = previousResources;
                UpdateVisibilityAndPauseState();
                return;
            }

            var imageCandidate = new WallpaperImageService(wallpaperUri);
            var initialMetrics = GetWallpaperMetrics();
            var bitmap = await imageCandidate.GetWallpaperAsync(initialMetrics);
            if (bitmap is null)
            {
                imageCandidate.Dispose();
                CompletePresentation(wallpaperUri, false);
                ReportWallpaperFailure("This image can’t be used as a wallpaper. Your current wallpaper is still active.");
                return;
            }

            if (!_isLoaded || requestVersion != Volatile.Read(ref _refreshRequestVersion))
            {
                imageCandidate.Dispose();
                return;
            }

            var previousImageResources = TakeVisibleWallpaperResources();
            _wallpaperImageService = imageCandidate;
            _wallpaperMetrics = initialMetrics;
            PresentWallpaper(bitmap, previousImageResources);
            CompletePresentation(wallpaperUri, true);

            // KWin may deliver one more configure while a large image is
            // decoding. Regenerate once at the settled output size instead of
            // leaving a low-resolution (or effectively solid) first frame.
            if (!initialMetrics.Equals(GetWallpaperMetrics()))
            {
                RequestWallpaperRefresh();
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or AccessViolationException))
        {
            CompletePresentation(wallpaperUri, false);
            global::System.Console.Error.WriteLine($"[wallpaper] Could not load wallpaper safely: {ex.Message}");
            ReportWallpaperFailure("This wallpaper couldn’t be loaded. Your current wallpaper is still active.");
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task LoadNativeVideoAsync(string path, string sourceUri, VideoMetadata metadata, int requestVersion,
        bool derivative = false, bool recovery = false, int optimizedWidth = 0, int optimizedHeight = 0)
    {
        if (_attachedWindow is null) return;
        var poster = await MpvWallpaperSession.PreviewAsync(path);
        if (poster is null) throw new IOException("The video could not produce a color-correct frame.");
        var position = string.Equals(sourceUri, _presentedWallpaperUri, StringComparison.Ordinal)
            ? _mpvSession?.Stats.PositionSeconds ?? 0 : 0;
        if (metadata.Duration > 0) position %= metadata.Duration;
        MpvWallpaperSession candidate;
        try { candidate = await MpvWallpaperSession.CreateAsync(_attachedWindow, path,
            Math.Max(1, (int)Math.Round(Bounds.Width)), Math.Max(1, (int)Math.Round(Bounds.Height)),
            TopLevel.GetTopLevel(this)?.RenderScaling ?? 1, metadata, position); }
        catch { poster.Dispose(); throw; }
        if (!_isLoaded || requestVersion != Volatile.Read(ref _refreshRequestVersion)) { candidate.Dispose(); poster.Dispose(); return; }
        var oldResources = TakeVisibleWallpaperResources();
        _mpvSession = candidate;
        _mpvPoster = poster;
        _activeVideoFile = VideoWallpaperOptimization.KeepActive(path);
        _videoSourcePath = ResolveWallpaperPath(sourceUri);
        _videoDemand = new VideoWallpaperOptimization.PlaybackDemand(_videoSourcePath);
        _videoMetadata = metadata;
        _videoCandidateWallpaperUri = sourceUri;
        _requestedWallpaperUri = sourceUri;
        _pendingVideoTransitionResources = oldResources;
        _isVideoPrerolling = true;
        _optimizationStarted = derivative;
        _usingOptimizedVideo = derivative;
        _optimizedWidth = optimizedWidth;
        _optimizedHeight = optimizedHeight;
        _nativeRecoveryAttempted = recovery;
        _videoWork = new CancellationTokenSource();
        _statisticsWindowStart = DateTimeOffset.UtcNow;
        _windowPresented = _windowDiscarded = _windowEngineDropped = 0;
        _lastVideoFrameAt = DateTimeOffset.UtcNow;
        _reportedVideoFrameFailure = false;
        _videoFrameTimer.Interval = TimeSpan.FromMilliseconds(100);
        UpdateVisibilityAndPauseState();
    }

    private void PollNativeVideo()
    {
        if (_mpvSession is not { } session) return;
        var stats = session.Stats;
        if (stats.State == 3)
        {
            global::System.Console.Error.WriteLine($"[wallpaper] GPU playback failed: {stats.Message}");
            if (!_isVideoPrerolling)
            {
                if (!_nativeRecoveryAttempted && _videoSourcePath is { } source && _videoMetadata is { } sourceMetadata && _presentedWallpaperUri is { } uri)
                {
                    _nativeRecoveryAttempted = true;
                    _videoFrameTimer.Stop();
                    WallpaperImage.Source = _mpvPoster;
                    _ = RecoverNativeVideoAsync(source, uri, sourceMetadata);
                    return;
                }
                session.Pause(true);
                _videoFrameTimer.Stop();
                _videoWork?.Cancel();
                WallpaperImage.Source = _mpvPoster;
                WallpaperImage.Opacity = 1;
            }
            ReportVideoFrameFailure();
            return;
        }
        if (_isVideoPrerolling)
        {
            if (stats.Submitted == 0)
            {
                if (DateTimeOffset.UtcNow - _lastVideoFrameAt > TimeSpan.FromSeconds(10)) ReportVideoFrameFailure();
                return;
            }
            Interlocked.Increment(ref _wallpaperTransitionVersion);
            WallpaperImage.Source = null;
            WallpaperImage.Opacity = 1;
            SolidColorBackground.Background = Brushes.Transparent;
            if (_attachedWindow is not null)
            {
                _attachedWindow.Background = Brushes.Transparent;
                _attachedWindow.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            }
            foreach (var panel in this.GetVisualAncestors().OfType<Panel>().Where(panel => panel.Name == "LockBackground"))
                panel.Background = Brushes.Transparent;
            if (_pendingVideoTransitionResources is { } old) _ = RetireResourcesAfterSourceReplacementAsync(old);
            _pendingVideoTransitionResources = null;
            _isVideoPrerolling = false;
            CompletePresentation(_videoCandidateWallpaperUri, true);
            UpdateVisibilityAndPauseState();
            global::System.Console.Error.WriteLine($"[wallpaper] GPU video ready: color-managed={stats.ColorManaged}, HDR={stats.HdrActive}, depth={stats.BufferDepth}");
        }
        if (session.IsPaused || stats.State == 2)
        {
            _videoDemand?.SetVisible(false);
            _statisticsWindowStart = DateTimeOffset.UtcNow;
            _windowPresented = stats.Presented; _windowDiscarded = stats.Discarded; _windowEngineDropped = stats.EngineDropped;
            return;
        }
        _videoDemand?.SetVisible(true);
        var largerTarget = _usingOptimizedVideo &&
            (GetPresentationWidth() > _optimizedWidth || GetPresentationHeight() > _optimizedHeight);
        if ((!_optimizationStarted || largerTarget) && _videoMetadata is { } metadata)
        {
            _optimizationStarted = true;
            _usingOptimizedVideo = false;
            _ = PrepareOptimizedVideoAsync(session, metadata, _videoWork!.Token);
        }
    }

    private async Task RecoverNativeVideoAsync(string source, string uri, VideoMetadata metadata)
    {
        await _loadGate.WaitAsync();
        try
        {
            if (_isLoaded)
            {
                metadata = await VideoMetadata.ProbeAsync(source);
                await LoadNativeVideoAsync(source, uri, metadata, Volatile.Read(ref _refreshRequestVersion), recovery: true);
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or AccessViolationException))
        { global::System.Console.Error.WriteLine($"[wallpaper] GPU recovery failed: {ex.Message}"); ReportVideoFrameFailure(); }
        finally { _loadGate.Release(); }
    }

    private async Task PrepareOptimizedVideoAsync(MpvWallpaperSession session, VideoMetadata metadata, CancellationToken token)
    {
        var source = _videoSourcePath;
        var uri = _presentedWallpaperUri;
        if (source is null || uri is null) return;
        try
        {
            // Always encode from the original. A previous derivative cannot supply
            // pixels needed after moving to a larger or more highly scaled output.
            metadata = await VideoMetadata.ProbeAsync(source, token);
            var targetWidth = GetPresentationWidth();
            var targetHeight = GetPresentationHeight();
            var path = await VideoWallpaperOptimization.PrepareAsync(source, metadata,
                targetWidth, targetHeight, force: true, token);
            if (path == source || token.IsCancellationRequested || !ReferenceEquals(session, _mpvSession) || !_isLoaded) return;
            var verified = await VideoMetadata.ProbeAsync(path, token);
            await _loadGate.WaitAsync(token);
            try
            {
                if (ReferenceEquals(session, _mpvSession) && _isLoaded)
                    await LoadNativeVideoAsync(path, uri, verified, Volatile.Read(ref _refreshRequestVersion), derivative: true,
                        optimizedWidth: targetWidth, optimizedHeight: targetHeight);
            }
            finally { _loadGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not (OutOfMemoryException or AccessViolationException))
        { global::System.Console.Error.WriteLine($"[wallpaper] Could not switch to optimized video: {ex.Message}"); }
    }

    private static string ResolveWallpaperPath(string uri) =>
        WallpaperSource.ResolveWallpaperPath(uri);

    private int _videoPresentationPending;
    private void OnNativeVideoFrameAvailable(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _videoPresentationPending, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _videoPresentationPending, 0);
            if (_isLoaded && ReferenceEquals(sender, _videoWallpaperService)) OnVideoFrameTick(sender, e);
        }, DispatcherPriority.Render);
    }

    private void OnVideoFrameTick(object? sender, EventArgs e)
    {
        if (_mpvSession is not null) { PollNativeVideo(); return; }
        if (!_isLoaded || _videoWallpaperService is null || !_videoWallpaperService.IsLoaded || _videoWallpaperService.IsPaused)
            return;

        // New libraries drive presentation from notifications. The timer only checks stalled playback.
        if (sender is DispatcherTimer && _videoWallpaperService.HasFrameNotifications &&
            DateTimeOffset.UtcNow - _lastVideoFrameAt < TimeSpan.FromSeconds(5)) return;
        using var trace = DaisyOS.Shell.Services.Diagnostics.ResponsivenessTrace.Measure("wallpaper.frame.cpu-present");
        if (!_videoWallpaperService.TryGetFrame(out var frame))
        {
            if (!_reportedVideoFrameFailure && DateTimeOffset.UtcNow - _lastVideoFrameAt > TimeSpan.FromSeconds(5))
            {
                ReportVideoFrameFailure();
            }
            return;
        }

        _lastVideoFrameAt = DateTimeOffset.UtcNow;
        {
            ulong ptrVal = (ulong)frame.Stride0 | ((ulong)frame.Stride1 << 32);
            try
            {
                int width = frame.Width;
                int height = frame.Height;

                if (width <= 0 || height <= 0 || width > MaximumPresentationWidth || height > MaximumPresentationHeight ||
                    (long)width * height * 4 > MaximumPresentationBytes || frame.Fd0 != -2 || ptrVal == 0)
                {
                    ReportVideoFrameFailure();
                    return;
                }

                if (GpuVideoFrameImage.TryCreate(frame, out var gpuImage))
                {
                    frame = new DaisyNativeWallpaper.NativeVideoFrame { Fd0 = -1, Fd1 = -1, Fd2 = -1, Fd3 = -1, AcquireFence = -1 };
                    var previousImage = _gpuVideoImage;
                    _gpuVideoImage = gpuImage;
                    PresentWallpaper(gpuImage!, _pendingVideoTransitionResources);
                    _pendingVideoTransitionResources = null;
                    _isVideoPrerolling = false;
                    CompletePresentation(_videoCandidateWallpaperUri, true);
                    UpdateVisibilityAndPauseState();
                    previousImage?.Dispose();
                    WallpaperImage.InvalidateVisual();
                    return;
                }
                if (_videoBitmapA is null || _videoBitmapA.PixelSize.Width != width || _videoBitmapA.PixelSize.Height != height)
                {
                    var previousA = _videoBitmapA;
                    var previousB = _videoBitmapB;
                    _videoBitmapA = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                    _videoBitmapB = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                    _ = RetireVideoBitmapsAfterPresent(previousA, previousB);
                }

                _useBitmapA = !_useBitmapA;
                var targetBitmap = _useBitmapA ? _videoBitmapA : _videoBitmapB;
                if (targetBitmap is null) return;

                using (var lockedBuf = targetBitmap.Lock())
                {
                    unsafe
                    {
                        int sourceRowBytes = width * 4;
                        if (lockedBuf.RowBytes == sourceRowBytes)
                        {
                            long bytesToCopy = (long)sourceRowBytes * height;
                            Buffer.MemoryCopy((void*)ptrVal, (void*)lockedBuf.Address, bytesToCopy, bytesToCopy);
                        }
                        else
                        {
                            for (int row = 0; row < height; row++)
                                Buffer.MemoryCopy((byte*)ptrVal + (long)row * sourceRowBytes,
                                    (byte*)lockedBuf.Address + (long)row * lockedBuf.RowBytes,
                                    lockedBuf.RowBytes, sourceRowBytes);
                        }
                    }
                }

                PresentWallpaper(targetBitmap, _pendingVideoTransitionResources);
                _pendingVideoTransitionResources = null;
                _isVideoPrerolling = false;
                CompletePresentation(_videoCandidateWallpaperUri, true);
                UpdateVisibilityAndPauseState();
                WallpaperImage.InvalidateVisual();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or AccessViolationException))
            {
                global::System.Console.Error.WriteLine($"[wallpaper] Frame presentation failed safely: {ex.Message}");
                ReportVideoFrameFailure();
            }
            finally
            {
                VideoWallpaperService.ReleaseFrame(ref frame);
            }
        }
    }

    private void ReportVideoFrameFailure()
    {
        var candidateUri = _videoCandidateWallpaperUri;
        FailVideoCandidate(candidateUri);
        CompletePresentation(candidateUri, false);
        if (_reportedVideoFrameFailure) return;
        _reportedVideoFrameFailure = true;
        ReportWallpaperFailure("Video wallpaper playback paused to keep the desktop stable. Choose another video to try again.");
    }

    private void CompletePresentation(string? wallpaperUri, bool succeeded)
    {
        PresentationState = succeeded ? WallpaperPresentationState.Presented : WallpaperPresentationState.Failed;
        if (succeeded && !string.IsNullOrWhiteSpace(wallpaperUri))
        {
            _presentedWallpaperUri = wallpaperUri;
            _videoCandidateWallpaperUri = null;
        }
        var completion = _presentationCompletion;
        _presentationCompletion = null;
        completion?.TrySetResult(succeeded);
    }

    private void FailVideoCandidate(string? candidateUri)
    {
        if (!_isVideoPrerolling || !string.Equals(candidateUri, _videoCandidateWallpaperUri, StringComparison.Ordinal)) return;
        _videoFrameTimer.Stop();
        _videoWallpaperService?.Pause();
        _mpvSession?.Pause(true);
        var failedCandidate = DetachWallpaperResources();
        failedCandidate.Dispose();
        _pendingVideoTransitionResources?.RestoreTo(this);
        _pendingVideoTransitionResources = null;
        _videoCandidateWallpaperUri = null;
        _isVideoPrerolling = false;
        PresentationState = WallpaperPresentationState.Failed;
        UpdateVisibilityAndPauseState();
    }

    private void ReportWallpaperFailure(string message)
    {
        // A failed visual asset must not be allowed to become a shell failure.
        // The current source stays on screen, and Settings can choose another file.
        global::System.Console.Error.WriteLine($"[wallpaper] {message}");
        Dispatcher.UIThread.Post(() => _app?.Feedback.Show(message));
    }

    private static void CloseFileDescriptor(int fd)
    {
        if (fd < 0) return;
        try { LibC.close(fd); } catch { }
    }

    private static Task RetireVideoBitmapsAfterPresent(WriteableBitmap? first, WriteableBitmap? second)
    {
        if (first is null && second is null) return Task.CompletedTask;
        return RetireAsync(first, second);

        static async Task RetireAsync(WriteableBitmap? first, WriteableBitmap? second)
        {
            await Task.Delay(WallpaperFadeHalfDurationMilliseconds);
            first?.Dispose();
            second?.Dispose();
        }
    }

    private static class LibC
    {
        [DllImport("libc", SetLastError = true)]
        public static extern int close(int fd);
    }

    private void RequestWallpaperRefresh()
    {
        if (_isLoaded && _wallpaperImageService is not null)
        {
            _ = RefreshWallpaperAsync(Interlocked.Increment(ref _refreshRequestVersion));
        }
    }

    private void ConfigureVideoOutput()
    {
        _mpvSession?.Resize(Math.Max(1, (int)Math.Round(Bounds.Width)), Math.Max(1, (int)Math.Round(Bounds.Height)),
            TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        _videoWallpaperService?.SetOutputSize(GetPresentationWidth(), GetPresentationHeight());
        _videoWallpaperService?.SetFpsCap(MaximumWallpaperPresentationPollRate);
    }

    private int GetPresentationWidth()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        return Math.Clamp((int)Math.Round(Bounds.Width * scaling), 1, MaximumPresentationWidth);
    }

    private int GetPresentationHeight()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        return Math.Clamp((int)Math.Round(Bounds.Height * scaling), 1, MaximumPresentationHeight);
    }

    private WallpaperRenderMetrics GetWallpaperMetrics()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        return WallpaperRenderMetrics.FromPixelSize(
            GetPresentationWidth(),
            GetPresentationHeight(),
            scaling,
            Stretch.UniformToFill);
    }

    private void ApplyWallpaperGeometry()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        double targetW = Bounds.Width * WallpaperScale;
        double targetH = Bounds.Height * WallpaperScale;

        if (Math.Abs(WallpaperImage.Width - targetW) < 0.5 && Math.Abs(WallpaperImage.Height - targetH) < 0.5)
        {
            return;
        }

        WallpaperImage.Width = targetW;
        WallpaperImage.Height = targetH;
        ConfigureVideoOutput();
        _horizontalTravelLimit = Math.Max(0, Math.Min(
            MaximumParallaxOffset,
            (WallpaperImage.Width - Bounds.Width) / 2 - EdgeSafetyInset));
        _verticalTravelLimit = Math.Max(0, Math.Min(
            MaximumParallaxOffset,
            (WallpaperImage.Height - Bounds.Height) / 2 - EdgeSafetyInset));
        _targetOffsetX = Math.Clamp(_targetOffsetX, -_horizontalTravelLimit, _horizontalTravelLimit);
        _targetOffsetY = Math.Clamp(_targetOffsetY, -_verticalTravelLimit, _verticalTravelLimit);
    }

    private async Task RefreshWallpaperAsync(int requestVersion, WallpaperResources? previousResources = null)
    {
        if (_wallpaperImageService is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var refreshGateAcquired = false;
        try
        {
            await _refreshGate.WaitAsync();
            refreshGateAcquired = true;
            if (!_isLoaded || requestVersion != Volatile.Read(ref _refreshRequestVersion))
            {
                return;
            }

            var wallpaperService = _wallpaperImageService;
            if (wallpaperService is null)
            {
                return;
            }

            _wallpaperMetrics = GetWallpaperMetrics();
            var bitmap = await wallpaperService.GetWallpaperAsync(_wallpaperMetrics);
            if (_isLoaded
                && requestVersion == Volatile.Read(ref _refreshRequestVersion)
                && ReferenceEquals(wallpaperService, _wallpaperImageService)
                && bitmap is not null)
            {
                PresentWallpaper(bitmap, previousResources);
                _ = RetireSupersededBitmapsAsync(wallpaperService.TakeSupersededBitmaps());
            }
        }
        catch
        {
            // Cosmetic fallback.
        }
        finally
        {
            if (refreshGateAcquired)
            {
                _refreshGate.Release();
            }
        }
    }

    private WallpaperResources DetachWallpaperResources()
    {
        _videoFrameTimer.Stop();
        _videoWork?.Cancel();
        _videoWork?.Dispose();
        _videoWork = null;
        _mpvSession?.Pause(true);
        _videoDemand?.SetVisible(false);
        var resources = new WallpaperResources(
            _wallpaperImageService,
            _videoWallpaperService,
            _videoBitmapA,
            _videoBitmapB,
            _gpuVideoImage,
            _mpvSession,
            _activeVideoFile,
            _mpvPoster,
            _videoDemand,
            _videoMetadata,
            _videoSourcePath,
            _optimizationStarted,
            _nativeRecoveryAttempted,
            _usingOptimizedVideo, _optimizedWidth, _optimizedHeight);
        _mpvSession = null;
        _mpvPoster = null;
        _activeVideoFile = null;
        _videoDemand = null;
        _gpuVideoImage = null;
        _wallpaperImageService = null;
        _videoWallpaperService = null;
        _isVideoPrerolling = false;
        _videoBitmapA = null;
        _videoBitmapB = null;
        _useBitmapA = false;
        return resources;
    }

    private WallpaperResources TakeVisibleWallpaperResources()
    {
        var resources = DetachWallpaperResources();
        if (_pendingVideoTransitionResources is not { } pendingResources)
        {
            return resources;
        }

        // A video can be replaced before it produces its first frame. Keep the
        // resource that is actually visible for the hand-off and immediately
        // release the decoder that never reached the compositor.
        resources.Dispose();
        _pendingVideoTransitionResources = null;
        return pendingResources;
    }

    private async Task RetireSupersededBitmapsAsync(Bitmap[] retired)
    {
        if (retired.Length == 0) return;
        try
        {
            var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (TopLevel.GetTopLevel(WallpaperImage) is { } topLevel)
            {
                topLevel.RequestAnimationFrame(_ => frame.TrySetResult());
                await frame.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(WallpaperImage) is { } visual)
                    await visual.Compositor.RequestCompositionBatchCommitAsync().Rendered.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception exception) when (exception is TimeoutException or ObjectDisposedException)
        {
            // A paused output can defer rendering. Keep these targets until
            // the layer's service is disposed rather than disposing live data.
            if (_isLoaded)
            {
                _retiredWallpaperBitmaps.AddRange(retired);
                return;
            }
        }
        foreach (var bitmap in retired) bitmap.Dispose();
    }

    private void PresentWallpaper(IImage source, WallpaperResources? previousResources)
    {
        SolidColorBackground.Background = Brushes.Transparent;

        // Video wallpapers update continuously. Present frames directly with full opacity,
        // avoiding crossfade race conditions and black flashes.
        if (_videoWallpaperService is not null)
        {
            WallpaperImage.Source = source;
            WallpaperImage.Opacity = 1;
            if (previousResources is not null)
            {
                Interlocked.Increment(ref _wallpaperTransitionVersion);
                _ = RetireResourcesAfterSourceReplacementAsync(previousResources);
            }
            return;
        }

        bool shouldAnimate = previousResources?.HasResources == true
                             && WallpaperImage.Source is not null
                             && !(_app?.ReduceMotionEnabled ?? false);

        if (!shouldAnimate)
        {
            if (previousResources is not null)
            {
                Interlocked.Increment(ref _wallpaperTransitionVersion);
            }
            WallpaperImage.Source = source;
            WallpaperImage.Opacity = 1;
            if (previousResources is not null)
            {
                _ = RetireResourcesAfterSourceReplacementAsync(previousResources);
            }
            return;
        }

        int transitionVersion = Interlocked.Increment(ref _wallpaperTransitionVersion);
        _ = FadeToWallpaperAsync(source, previousResources!, transitionVersion);
    }

    private void PresentSolidWallpaper(Color color, WallpaperResources previousResources)
    {
        var transitionVersion = Interlocked.Increment(ref _wallpaperTransitionVersion);
        SolidColorBackground.Background = new SolidColorBrush(color);
        WallpaperImage.Opacity = 0;
        _ = ClearImageAfterSolidColourTransitionAsync(transitionVersion, previousResources);
    }

    private async Task ClearImageAfterSolidColourTransitionAsync(int transitionVersion, WallpaperResources previousResources)
    {
        await Task.Delay(WallpaperFadeHalfDurationMilliseconds);
        if (_isLoaded && transitionVersion == Volatile.Read(ref _wallpaperTransitionVersion))
        {
            WallpaperImage.Source = null;
        }

        previousResources.Dispose();
    }

    private async Task FadeToWallpaperAsync(IImage source, WallpaperResources previousResources, int transitionVersion)
    {
        WallpaperImage.Opacity = 0;
        await Task.Delay(WallpaperFadeHalfDurationMilliseconds);

        if (!_isLoaded || transitionVersion != Volatile.Read(ref _wallpaperTransitionVersion))
        {
            // A newer selection owns the source replacement now. Leave the old resources
            // alive long enough for that replacement to reach the compositor.
            await Task.Delay(WallpaperFadeHalfDurationMilliseconds * 2);
            previousResources.Dispose();
            return;
        }

        WallpaperImage.Source = source;
        WallpaperImage.Opacity = 1;

        await RetireResourcesAfterSourceReplacementAsync(previousResources);
    }

    private static async Task RetireResourcesAfterSourceReplacementAsync(WallpaperResources resources)
    {
        // Keep retired data through the rest of the fade. This protects compositors that
        // retain a source for more than one frame while preparing the replacement.
        await Task.Delay(WallpaperFadeHalfDurationMilliseconds);
        resources.Dispose();
    }

    private sealed class WallpaperResources(
        WallpaperImageService? imageService,
        VideoWallpaperService? videoService,
        WriteableBitmap? bitmapA,
        WriteableBitmap? bitmapB,
        GpuVideoFrameImage? gpuImage = null,
        MpvWallpaperSession? mpvSession = null,
        IDisposable? activeVideoFile = null,
        WriteableBitmap? mpvPoster = null,
        VideoWallpaperOptimization.PlaybackDemand? videoDemand = null,
        VideoMetadata? videoMetadata = null,
        string? videoSourcePath = null,
        bool optimizationStarted = false,
        bool nativeRecoveryAttempted = false,
        bool usingOptimizedVideo = false, int optimizedWidth = 0, int optimizedHeight = 0) : IDisposable
    {
        public bool HasResources => mpvSession is not null || gpuImage is not null || imageService is not null || videoService is not null || bitmapA is not null || bitmapB is not null;

        public void RestoreTo(WallpaperLayer owner)
        {
            owner._wallpaperImageService = imageService;
            owner._videoWallpaperService = videoService;
            owner._videoBitmapA = bitmapA;
            owner._videoBitmapB = bitmapB;
            owner._gpuVideoImage = gpuImage;
            owner._mpvSession = mpvSession;
            owner._mpvPoster = mpvPoster;
            owner._activeVideoFile = activeVideoFile;
            owner._videoDemand = videoDemand;
            owner._videoMetadata = videoMetadata;
            owner._videoSourcePath = videoSourcePath;
            owner._optimizationStarted = optimizationStarted;
            owner._nativeRecoveryAttempted = nativeRecoveryAttempted;
            owner._usingOptimizedVideo = usingOptimizedVideo;
            owner._optimizedWidth = optimizedWidth;
            owner._optimizedHeight = optimizedHeight;
            if (mpvSession is not null) owner._videoWork = new CancellationTokenSource();
        }

        public void Dispose()
        {
            mpvSession?.Dispose();
            mpvPoster?.Dispose();
            activeVideoFile?.Dispose();
            videoDemand?.Dispose();
            gpuImage?.Dispose();
            imageService?.Dispose();
            videoService?.Dispose();
            bitmapA?.Dispose();
            bitmapB?.Dispose();
        }
    }

    private void AdvanceParallax()
    {
        _offsetX += (_targetOffsetX - _offsetX) * 0.16;
        _offsetY += (_targetOffsetY - _offsetY) * 0.16;
        _parallaxTranslation.X = _offsetX;
        _parallaxTranslation.Y = _offsetY;

        if (Math.Abs(_targetOffsetX - _offsetX) < 0.05
            && Math.Abs(_targetOffsetY - _offsetY) < 0.05)
        {
            _offsetX = _targetOffsetX;
            _offsetY = _targetOffsetY;
            _parallaxTranslation.X = _offsetX;
            _parallaxTranslation.Y = _offsetY;
        }
    }
}
