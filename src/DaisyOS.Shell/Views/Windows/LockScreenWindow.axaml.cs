using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;
using Avalonia.Wayland;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.Views.Components.Wallpaper;
using DaisyOS.Shell.Services.Theming;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Shell;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.System.Power;
using DaisyOS.System.Processes;
using DaisyOS.System.Security;

namespace DaisyOS.Shell.Views.Windows;

public partial class LockScreenWindow : Window
{
    private readonly ILockScreenService _lockService;
    private readonly IPowerService _powerService;
    private readonly IBatteryStatusService _batteryService;
    private readonly DispatcherTimer _clockTimer;
    private readonly bool _isSecondary;
    private bool _isAuthenticating;
    private bool _isShaking;
    private Task? _dismissal;
    private bool _dismissing;
    private readonly DispatcherTimer _presenceTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly global::System.Diagnostics.Stopwatch _presenceTime = new();
    private readonly TranslateTransform _clockPresence = new();
    private readonly TranslateTransform _credentialsPresence = new();
    private TaskCompletionSource? _presenceCompletion;
    private double _presenceProgress;
    private double _presenceStart, _presenceTarget;
    private TimeSpan _presenceDuration;
    private readonly DispatcherTimer _clockAnimation = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly global::System.Diagnostics.Stopwatch _clockAnimationTime = new();
    private bool _clockInitialized;
    private KWinBlur? _nativeBlur;
    private readonly Dictionary<(string, Typeface, double), Rect[]> _clockGlyphBands = new();

    public event Action? Dismissing;

    // KWin needs the sharp wallpaper beneath a transparent foreground surface.
    // Restrict blur to material containers and clock glyphs, never the fullscreen root.
    internal static LockScreenWindow CreateNative(ILayerShellWindowBackend backend,
        LayerShellSurfaceOptions options, string? wallpaperSource, bool isSecondary = false,
        ILockScreenService? lockService = null, WallpaperLayer? initialWallpaper = null)
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
            () => new LockScreenWindow(wallpaperSource, lockService, isSecondary));
        ThemeTransitionPolicy.SetEnabled(window, false);
        window.Background = Brushes.Transparent;
        if (initialWallpaper is not null) window.LockWallpaper.SeedFrom(initialWallpaper);
        window.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        window.RootGrid.Children.Remove(window.LockBackground);
        wallpaper.Content = window.LockBackground;
        window.Closed += (_, _) => wallpaper.Close();
        window._nativeBlur = new KWinBlur(window, window.RootGrid,
            () => new Control[] { window.AvatarBorder, window.PasswordPillBorder })
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

    private static Rect[] RasterizeClockGlyphBands(string value, Typeface typeface, double fontSize)
    {
        var formatted = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, fontSize, Brushes.White);
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
                var inside = x < width && pixels[(y * width + x) * 4 + 3] > 16;
                if (inside && start < 0) start = x;
                if (!inside && start >= 0) { bands.Add(new Rect(left + start, top + y, x - start, 1)); start = -1; }
            }
        }
        return bands.ToArray();
    }

    public LockScreenWindow() : this(null, null, false) { }

    public LockScreenWindow(ILockScreenService lockService) : this(null, lockService, false) { }

    public LockScreenWindow(string? wallpaperSource = null, ILockScreenService? lockService = null, bool isSecondary = false)
    {
        InitializeComponent();
        ClockArea.RenderTransform = _clockPresence;
        CredentialsPanel.RenderTransform = _credentialsPresence;
        _presenceTimer.Tick += (_, _) => AdvancePresence();
        ApplyPresence(0);
        _isSecondary = isSecondary;
        _lockService = lockService ?? new LinuxLockScreenService();
        _powerService = new LinuxPowerService(new SafeCommandRunner());
        _batteryService = new LinuxBatteryStatusService();

        UserNameText.Text = GetFormattedUserName();
        SizeChanged += (_, _) => UpdateLockLayout();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == FontSizeProperty || args.Property == FontFamilyProperty) UpdateLockLayout();
        };
        ClockText.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBlock.FontFamilyProperty) UpdateLockLayout();
        };
        LockContentScroll.ScrollChanged += (_, _) => _nativeBlur?.RefreshImmediately();
        Loc.RefreshOnChange(this, UpdateClock);
        if (Application.Current is App app)
        {
            EventHandler refreshLayout = (_, _) => UpdateLockLayout();
            app.AppearanceChanged += refreshLayout;
            Closed += (_, _) => app.AppearanceChanged -= refreshLayout;
        }

        LoadWallpaper(wallpaperSource);

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
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
            BatteryInfoText.IsVisible = false;
        }
        else
        {
            UpdateBatteryAsync();

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
            if (!_isSecondary) PasswordBox.Focus();
        };

        Closed += (_, _) =>
        {
            _clockTimer.Stop();
            _clockAnimation.Stop();
            _presenceTimer.Stop();
            _presenceCompletion?.TrySetResult();
        };
        Closing += (_, e) =>
        {
            if (!_isSecondary && _lockService.IsLocked)
            {
                e.Cancel = true;
            }
        };
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
            ClockText.FontStyle, ClockText.FontWeight, ClockText.FontStretch), size);
        if (measured.Width > width && measured.Width > 0) size *= width / measured.Width;
        ClockText.FontSize = PreviousClockText.FontSize = Math.Max(1, Math.Floor(size));
        DateText.FontSize = Math.Min(Resource("TypeSizeHeading", 24), Math.Max(18, Math.Round(size * 0.24)));
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
        var time = now.ToString("H:mm", CultureInfo.InvariantCulture);
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
                BatteryInfoText.Text = $"⚡ {status.ChargePercent}% {Loc.Get(status.State)}";
            }
        }
        catch
        {
            // Ignore battery query failures
        }
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
        await AnimatePresenceAsync(false);
        if (!_isSecondary) _lockService.Unlock();
        Close();
    }

    private Task AnimatePresenceAsync(bool entering)
    {
        _presenceTimer.Stop();
        _presenceCompletion?.TrySetResult();
        _presenceCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _presenceStart = _presenceProgress;
        _presenceTarget = entering ? 1 : 0;
        _presenceDuration = Application.Current is App { ReduceMotionEnabled: true } ? TimeSpan.Zero :
            this.TryFindResource(entering ? "MotionDurationEnter" : "MotionDurationExit", out var resource) && resource is TimeSpan duration
                ? duration : TimeSpan.FromMilliseconds(entering ? 280 : 140);
        _presenceDuration *= Math.Abs(_presenceTarget - _presenceStart);
        _presenceTime.Restart();
        AdvancePresence();
        if (!_presenceCompletion.Task.IsCompleted) _presenceTimer.Start();
        return _presenceCompletion.Task;
    }

    private void AdvancePresence()
    {
        var progress = _presenceDuration <= TimeSpan.Zero ? 1 : Math.Clamp(_presenceTime.Elapsed.TotalMilliseconds / _presenceDuration.TotalMilliseconds, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        ApplyPresence(_presenceStart + (_presenceTarget - _presenceStart) * eased);
        if (progress >= 1)
        {
            _presenceTimer.Stop();
            _presenceCompletion?.TrySetResult();
        }
    }

    private void ApplyPresence(double progress)
    {
        _presenceProgress = progress;
        void Update()
        {
            ClockArea.Opacity = CredentialsPanel.Opacity = CornerControlsGrid.Opacity = progress;
            _clockPresence.Y = -14 * (1 - progress);
            _credentialsPresence.Y = 14 * (1 - progress);
        }
        // Publish one coherent mask after all values and transform caches have
        // advanced, rather than several partially updated masks per frame.
        if (_nativeBlur is { } blur) blur.UpdateGeometry(Update);
        else Update();
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
