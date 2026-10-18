using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Screenshot;

namespace DaisyOS.Shell.Views.Components.Screenshot;

public partial class ScreenshotOverlay : UserControl
{
    private const double MinSelectionSize = 10;
    private const double HandleHitSize = 22;           // must match Border.ResizeHandle size in AXAML
    private const double EdgeHandleMinLength = 48;     // hide edge handles on very small selections
    private const double ToolbarBottomMargin = 48;
    private const double ToolbarTopSlot = 88;          // y of the toolbar when it hops above a selection

    private readonly DaisyScreenshotService _screenshotService = new();
    private readonly TranslateTransform _toolbarTransform = new();
    private bool _toolbarUserMoved;
    private ScreenshotCaptureMode _currentMode = ScreenshotCaptureMode.Area;
    private int _timerSeconds = 0;
    private bool _includeCursor = false;

    // Selection lifecycle state
    private bool _isDragging;       // drawing a brand new selection
    private bool _isMoving;         // moving an existing selection
    private string? _resizeHandle;  // non-null while resizing via a handle
    private Point _startPoint;
    private Point _moveOffset;
    private Rect _resizeAnchorRect;
    private Rect _selectedRect;

    // Toolbar movement state
    private bool _isDraggingToolbar;
    private Point _toolbarDragStart;
    private Point _toolbarInitialTransform;

    private CancellationTokenSource? _countdownCancellation;
    private CancellationTokenSource? _captureCancellation;
    private bool _isCapturing;
    private int _openVersion;
    // The clipboard serves images lazily. Retain the last image until replaced.
    private Bitmap? _clipboardImage;
    private SkiaSharp.SKBitmap? _frozenSkBitmap;
    private Bitmap? _frozenAvaloniaBitmap;
    private string? _frozenSnapshotPath;

    public bool IsOpen => IsVisible;

    public event EventHandler? Closed;
    public event EventHandler<bool>? CaptureInputChanged;

    public ScreenshotOverlay()
    {
        InitializeComponent();
        ToolbarPill.RenderTransform = _toolbarTransform;
        AddHandler(KeyDownEvent, OnOverlayKeyDown, RoutingStrategies.Tunnel);
        RootLayout.SizeChanged += (_, _) => UpdateToolbarLayout();
        SelectionCanvas.SizeChanged += (_, _) =>
        {
            if (!IsVisible) return;
            UpdateSelectionVisuals();
            AvoidSelectionWithToolbar();
        };
    }

    internal ScreenshotOverlay(DaisyScreenshotService service) : this() => _screenshotService = service;

    /// <summary>The frozen desktop must be below this surface so KWin can blur it behind the toolbar.</summary>
    internal Control DetachFrozenBackdrop()
    {
        RootLayout.Children.Remove(FrozenScreenImage);
        return FrozenScreenImage;
    }

    internal IEnumerable<Control> MaterialRegions => IsVisible
        ? new Control[] { ToolbarPill, InstructionBanner, ModeHintCard, CountdownMaterial }
        : [];

    private void UpdateToolbarLayout()
    {
        var compact = ModeAreaBtn.FontSize > 18 || RootLayout.Bounds.Width < 640 * Math.Max(1, ModeAreaBtn.FontSize / 13);
        ModeHintCard.MaxWidth = InstructionBanner.MaxWidth = Math.Max(0, RootLayout.Bounds.Width - 32);
        ModeAreaLabel.IsVisible = ModeWindowLabel.IsVisible = ModeScreenLabel.IsVisible = CaptureActionLabel.IsVisible = !compact;
    }

    private sealed record PreparedSnapshot(SkiaSharp.SKBitmap Pixels, Bitmap Image);

    private static PreparedSnapshot? LoadSnapshot(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        SkiaSharp.SKBitmap? pixels = null;
        try
        {
            using var codec = SkiaSharp.SKCodec.Create(path);
            if (codec is null) return null;
            using var colorSpace = SkiaSharp.SKColorSpace.CreateSrgb();
            var opaque = codec.Info.AlphaType == SkiaSharp.SKAlphaType.Opaque;
            var info = new SkiaSharp.SKImageInfo(codec.Info.Width, codec.Info.Height, SkiaSharp.SKColorType.Bgra8888,
                opaque ? SkiaSharp.SKAlphaType.Opaque : SkiaSharp.SKAlphaType.Premul, colorSpace);
            pixels = new SkiaSharp.SKBitmap(info);
            if (codec.GetPixels(info, pixels.GetPixels()) != SkiaSharp.SKCodecResult.Success)
            {
                pixels.Dispose();
                return null;
            }
            // The Avalonia constructor copies these decoded pixels. Avoid reading and
            // decoding the same full-screen image a second time just to display it.
            return new PreparedSnapshot(pixels, new Bitmap(PixelFormat.Bgra8888,
                opaque ? AlphaFormat.Opaque : AlphaFormat.Premul, pixels.GetPixels(),
                new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), pixels.RowBytes));
        }
        catch { pixels?.Dispose(); return null; }
    }

    public async Task<bool> OpenAsync(string? snapshotPath, ScreenshotCaptureMode mode = ScreenshotCaptureMode.Area)
    {
        var version = ++_openVersion;
        var snapshot = await Task.Run(() => LoadSnapshot(snapshotPath));
        if (version != _openVersion)
        {
            snapshot?.Pixels.Dispose();
            snapshot?.Image.Dispose();
            DaisyScreenshotService.DeleteFile(snapshotPath);
            return false;
        }
        OpenCore(snapshotPath, mode, snapshot);
        return IsOpen;
    }

    public void Open(string? snapshotPath = null, ScreenshotCaptureMode mode = ScreenshotCaptureMode.Area)
        => OpenCore(snapshotPath, mode, LoadSnapshot(snapshotPath));

    private void OpenCore(string? snapshotPath, ScreenshotCaptureMode mode, PreparedSnapshot? snapshot)
    {
        if (_captureCancellation is not null)
        {
            snapshot?.Pixels.Dispose();
            snapshot?.Image.Dispose();
            if (_frozenSnapshotPath != snapshotPath) DaisyScreenshotService.DeleteFile(snapshotPath);
            return;
        }
        var openVersion = ++_openVersion;
        _isCapturing = false;
        _isDragging = false;
        _isMoving = false;
        _resizeHandle = null;
        _selectedRect = default;
        _currentMode = mode;
        SelectionBox.IsVisible = false;
        CountdownBadge.IsVisible = false;
        FlashOverlay.IsVisible = false;
        _toolbarTransform.Transitions = null;
        if (!_toolbarUserMoved) _toolbarTransform.Y = 0;
        _toolbarTransform.Transitions = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = Services.Motion.GuiMotion.ShellSlideDuration(this, true),
                Easing = new Avalonia.Animation.Easings.SpringEasing(1, 256, 28)
            }
        };
        ToolbarPill.Opacity = 0;
        InstructionBanner.Opacity = 0;
        ToolbarPill.IsVisible = true;
        InstructionBanner.IsVisible = true;

        // Dispose previous frozen snapshot if any
        FrozenScreenImage.Source = null;
        _frozenAvaloniaBitmap?.Dispose();
        _frozenAvaloniaBitmap = null;
        _frozenSkBitmap?.Dispose();
        _frozenSkBitmap = null;
        if (_frozenSnapshotPath != null && _frozenSnapshotPath != snapshotPath)
        {
            DaisyScreenshotService.DeleteFile(_frozenSnapshotPath);
            _frozenSnapshotPath = null;
        }

        _frozenSnapshotPath = snapshotPath;
        _frozenSkBitmap = snapshot?.Pixels;
        _frozenAvaloniaBitmap = snapshot?.Image;
        FrozenScreenImage.Source = snapshot?.Image;
        FrozenScreenImage.IsVisible = snapshot is not null;

        IsVisible = true;

        UpdateToolbarLayout();
        UpdateModeUi();
        UpdateDimMask();
        Focus();

        // Fade the chrome in on the next frame so it feels like it settles
        // in rather than popping onto the screen.
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible || openVersion != _openVersion) return;
            ToolbarPill.Opacity = 1;
            UpdateHints();
        }, DispatcherPriority.Render);
    }

    public void Close()
    {
        ++_openVersion;
        _countdownCancellation?.Cancel();
        _captureCancellation?.Cancel();
        _isCapturing = false;
        _isDraggingToolbar = false;
        _isDragging = false;
        _isMoving = false;
        _resizeHandle = null;

        FrozenScreenImage.Source = null;
        _frozenAvaloniaBitmap?.Dispose();
        _frozenAvaloniaBitmap = null;
        _frozenSkBitmap?.Dispose();
        _frozenSkBitmap = null;
        FrozenScreenImage.Source = null;
        FrozenScreenImage.IsVisible = false;

        if (_frozenSnapshotPath != null)
        {
            DaisyScreenshotService.DeleteFile(_frozenSnapshotPath);
            _frozenSnapshotPath = null;
        }

        IsVisible = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnOverlayKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_isCapturing) { Close(); return; }
            if (_isDragging || _isMoving || _resizeHandle != null)
            {
                CancelActiveManipulation();
            }
            else if (_currentMode == ScreenshotCaptureMode.Area && HasSelection())
            {
                ClearSelection();
            }
            else
            {
                Close();
            }
            return;
        }

        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            _currentMode == ScreenshotCaptureMode.Area && !_isCapturing)
        {
            e.Handled = true;
            SelectEntireScreen();
            return;
        }

        if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            e.Handled = true;
            if (!_isCapturing && _currentMode == ScreenshotCaptureMode.Area && !HasSelection())
            {
                ShowFeedback("Drag to select an area first, or press Ctrl+A to select the whole screen");
                return;
            }
            _ = ExecuteCaptureAsync();
            return;
        }

        if (e.Key == Key.D1 || e.Key == Key.NumPad1)
        {
            e.Handled = true;
            SetMode(ScreenshotCaptureMode.Area);
            return;
        }

        if (e.Key == Key.D2 || e.Key == Key.NumPad2)
        {
            e.Handled = true;
            SetMode(ScreenshotCaptureMode.Window);
            return;
        }

        if (e.Key == Key.D3 || e.Key == Key.NumPad3)
        {
            e.Handled = true;
            SetMode(ScreenshotCaptureMode.Screen);
            return;
        }

        // Arrow-key fine-tuning of an existing Area selection:
        //   Arrow            -> move 1px   (10px with Shift)
        //   Alt + Arrow      -> resize 1px (10px with Shift)
        if (_currentMode == ScreenshotCaptureMode.Area && HasSelection() && !_isCapturing &&
            IsArrowKey(e.Key, out var dx, out var dy))
        {
            e.Handled = true;
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                NudgeResize(dx * step, dy * step);
            }
            else
            {
                NudgeMove(dx * step, dy * step);
            }
        }
    }

    private static bool IsArrowKey(Key key, out int dx, out int dy)
    {
        switch (key)
        {
            case Key.Left: dx = -1; dy = 0; return true;
            case Key.Right: dx = 1; dy = 0; return true;
            case Key.Up: dx = 0; dy = -1; return true;
            case Key.Down: dx = 0; dy = 1; return true;
            default: dx = 0; dy = 0; return false;
        }
    }

    private bool HasSelection() => _selectedRect.Width >= MinSelectionSize && _selectedRect.Height >= MinSelectionSize;

    private void ClearSelection()
    {
        _selectedRect = default;
        SelectionBox.IsVisible = false;
        UpdateSelectionVisuals(); // also hides the resize handles
    }

    private void CancelActiveManipulation()
    {
        _isDraggingToolbar = false;
        _isDragging = false;
        _isMoving = false;
        _resizeHandle = null;

        if (!HasSelection())
        {
            SelectionBox.IsVisible = false;
        }

        RootLayout.Cursor = null;
        UpdateSelectionVisuals();
    }

    private void SetMode(ScreenshotCaptureMode mode)
    {
        if (_isCapturing) return;
        _currentMode = mode;
        UpdateModeUi();
    }

    private void UpdateModeUi()
    {
        ModeAreaBtn.Classes.Set("Active", _currentMode == ScreenshotCaptureMode.Area);
        ModeWindowBtn.Classes.Set("Active", _currentMode == ScreenshotCaptureMode.Window);
        ModeScreenBtn.Classes.Set("Active", _currentMode == ScreenshotCaptureMode.Screen);

        SelectionCanvas.IsVisible = _currentMode == ScreenshotCaptureMode.Area;
        SelectionBox.IsVisible = _currentMode == ScreenshotCaptureMode.Area && HasSelection();

        // Refreshes mask, handles, hint card and capture button in one go.
        UpdateSelectionVisuals();
    }

    private void UpdateCaptureButtonState()
    {
        var ready = _currentMode != ScreenshotCaptureMode.Area || HasSelection();
        CaptureButton.IsEnabled = ready;
        CaptureButton.Opacity = ready ? 1.0 : 0.5;
        ToolTip.SetTip(CaptureButton, ready
            ? Loc.Get("Take Screenshot (Enter)")
            : Loc.Get("Select an area first"));
    }

    private void UpdateHints()
    {
        var area = _currentMode == ScreenshotCaptureMode.Area;
        var hasSelection = area && HasSelection();
        var manipulating = _isDragging || _isMoving || _resizeHandle != null;

        HintIconArea.IsVisible = area;
        HintIconWindow.IsVisible = _currentMode == ScreenshotCaptureMode.Window;
        HintIconScreen.IsVisible = _currentMode == ScreenshotCaptureMode.Screen;

        switch (_currentMode)
        {
            case ScreenshotCaptureMode.Area:
                HintTitle.Text = Loc.Get("Drag to select an area");
                HintSubtitle.Text = Loc.Get("Press Ctrl+A to select the whole screen");
                break;
            case ScreenshotCaptureMode.Window:
                HintTitle.Text = Loc.Get("Capture the active window");
                HintSubtitle.Text = Loc.Get("Press Enter to capture the window you were using");
                break;
            default:
                HintTitle.Text = Loc.Get("Capture the full screen");
                HintSubtitle.Text = Loc.Get("Press Enter to capture everything on screen");
                break;
        }

        ModeHintCard.IsVisible = !_isCapturing && (!area || (!hasSelection && !_isDragging));
        ScreenOutline.IsVisible = !_isCapturing && _currentMode == ScreenshotCaptureMode.Screen;

        // Shortcut banner only appears when there's something to adjust, and
        // steps aside while the user is actively dragging.
        InstructionBanner.Opacity = hasSelection && !manipulating && !_isCapturing ? 1 : 0;
    }

    private void OnModeAreaClicked(object? sender, RoutedEventArgs e) => SetMode(ScreenshotCaptureMode.Area);
    private void OnModeWindowClicked(object? sender, RoutedEventArgs e) => SetMode(ScreenshotCaptureMode.Window);
    private void OnModeScreenClicked(object? sender, RoutedEventArgs e) => SetMode(ScreenshotCaptureMode.Screen);

    private void OnTimerClicked(object? sender, RoutedEventArgs e)
    {
        if (_isCapturing) return;
        _timerSeconds = _timerSeconds switch
        {
            0 => 3,
            3 => 5,
            5 => 10,
            _ => 0
        };

        TimerLabel.Text = Loc.Format("{0}s", _timerSeconds);
        TimerLabel.IsVisible = _timerSeconds > 0;
        TimerBtn.Classes.Set("Active", _timerSeconds > 0);
        ToolTip.SetTip(TimerBtn, _timerSeconds == 0
            ? Loc.Get("No capture delay. Click to add one")
            : Loc.Format("Captures after {0} seconds. Click to change", _timerSeconds));
    }

    private void OnCursorToggleChanged(object? sender, RoutedEventArgs e)
    {
        _includeCursor = CursorToggle.IsChecked == true;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnCaptureClicked(object? sender, RoutedEventArgs e) => _ = ExecuteCaptureAsync();

    private void OnCountdownBadgeClicked(object? sender, PointerPressedEventArgs e)
    {
        // Let users skip the countdown by clicking it, instead of forcing a wait.
        _countdownCancellation?.Cancel();
    }

    // ---------------------------------------------------------------
    // Selection creation (drag on empty canvas)
    // ---------------------------------------------------------------

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_currentMode != ScreenshotCaptureMode.Area || _isCapturing) return;

        var point = e.GetCurrentPoint(SelectionCanvas);
        if (point.Properties.IsRightButtonPressed)
        {
            // Right-click clears the current selection so users can start over.
            if (HasSelection())
            {
                ClearSelection();
                e.Handled = true;
            }
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;

        _isDragging = true;
        _startPoint = point.Position;
        _selectedRect = new Rect(_startPoint, new Size(0, 0));
        SelectionBox.IsVisible = true;
        ModeHintCard.IsVisible = false;

        UpdateSelectionVisuals();
        e.Pointer.Capture(RootLayout);
    }

    // ---------------------------------------------------------------
    // Selection move (drag inside an existing selection)
    // ---------------------------------------------------------------

    private void OnSelectionBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_currentMode != ScreenshotCaptureMode.Area || _isCapturing) return;

        var point = e.GetCurrentPoint(SelectionCanvas);
        if (point.Properties.IsRightButtonPressed)
        {
            ClearSelection();
            e.Handled = true;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;

        // Double-click inside the selection captures it.
        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            _ = ExecuteCaptureAsync();
            return;
        }

        _isMoving = true;
        _moveOffset = point.Position - _selectedRect.Position;
        e.Pointer.Capture(RootLayout);
        e.Handled = true; // don't let this bubble down and start a new selection
    }

    // ---------------------------------------------------------------
    // Resize handles
    // ---------------------------------------------------------------

    private void OnHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_currentMode != ScreenshotCaptureMode.Area || _isCapturing) return;
        if (sender is not Border { Tag: string handle }) return;

        var point = e.GetCurrentPoint(SelectionCanvas);
        if (!point.Properties.IsLeftButtonPressed) return;

        _resizeHandle = handle;
        _resizeAnchorRect = _selectedRect;
        e.Pointer.Capture(RootLayout);
        e.Handled = true;
    }

    // ---------------------------------------------------------------
    // Toolbar movement
    // ---------------------------------------------------------------

    private void OnToolbarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isCapturing) return;

        var point = e.GetCurrentPoint(RootLayout);
        if (!point.Properties.IsLeftButtonPressed) return;

        if (e.Source is Visual source && IsInteractiveToolbarControl(source))
        {
            return;
        }

        // Automatic avoidance springs into place; a manual drag follows the pointer directly.
        var current = new Point(_toolbarTransform.X, _toolbarTransform.Y);
        _toolbarTransform.Transitions = null;
        _toolbarTransform.X = current.X;
        _toolbarTransform.Y = current.Y;
        _isDraggingToolbar = true;
        _toolbarUserMoved = true;
        _toolbarDragStart = point.Position;
        _toolbarInitialTransform = new Point(_toolbarTransform.X, _toolbarTransform.Y);
        e.Pointer.Capture(RootLayout);
        e.Handled = true;
    }

    private static bool IsInteractiveToolbarControl(Visual source)
    {
        if (source is Border { Name: "ToolbarDragGrip" } ||
            source.GetVisualAncestors().OfType<Border>().Any(b => b.Name == "ToolbarDragGrip"))
        {
            return false;
        }

        return source is Button || source is ToggleButton ||
               source.GetVisualAncestors().Any(v => v is Button || v is ToggleButton);
    }

    // ---------------------------------------------------------------
    // Centralized pointer move/release: whichever manipulation is
    // active (draw / move / resize / toolbar drag) gets updated here.
    // ---------------------------------------------------------------

    private void OnRootPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDraggingToolbar)
        {
            var pos = e.GetPosition(RootLayout);
            var delta = pos - _toolbarDragStart;

            var bounds = RootLayout.Bounds;
            var pillBounds = ToolbarPill.Bounds;
            if (bounds.Width > 0 && bounds.Height > 0 && pillBounds.Width > 0 && pillBounds.Height > 0)
            {
                var baseLeft = (bounds.Width - pillBounds.Width) / 2.0;
                var baseTop = bounds.Height - pillBounds.Height - 48.0;

                var targetLeft = baseLeft + _toolbarInitialTransform.X + delta.X;
                var targetTop = baseTop + _toolbarInitialTransform.Y + delta.Y;

                var clampedLeft = Clamp(targetLeft, 12, Math.Max(12, bounds.Width - pillBounds.Width - 12));
                var clampedTop = Clamp(targetTop, 12, Math.Max(12, bounds.Height - pillBounds.Height - 12));

                _toolbarTransform.X = clampedLeft - baseLeft;
                _toolbarTransform.Y = clampedTop - baseTop;
            }
            else
            {
                _toolbarTransform.X = _toolbarInitialTransform.X + delta.X;
                _toolbarTransform.Y = _toolbarInitialTransform.Y + delta.Y;
            }
            return;
        }

        if (_currentMode != ScreenshotCaptureMode.Area) return;

        var rawPosition = e.GetPosition(SelectionCanvas);
        var canvasPos = new Point(Clamp(rawPosition.X, 0, SelectionCanvas.Bounds.Width),
            Clamp(rawPosition.Y, 0, SelectionCanvas.Bounds.Height));


        if (_isDragging)
        {
            var x = Math.Min(_startPoint.X, canvasPos.X);
            var y = Math.Min(_startPoint.Y, canvasPos.Y);
            var width = Math.Abs(canvasPos.X - _startPoint.X);
            var height = Math.Abs(canvasPos.Y - _startPoint.Y);
            _selectedRect = new Rect(x, y, width, height);

            UpdateSelectionVisuals();
        }
        else if (_isMoving)
        {
            var bounds = SelectionCanvas.Bounds;
            var newX = Clamp(canvasPos.X - _moveOffset.X, 0, Math.Max(0, bounds.Width - _selectedRect.Width));
            var newY = Clamp(canvasPos.Y - _moveOffset.Y, 0, Math.Max(0, bounds.Height - _selectedRect.Height));
            _selectedRect = new Rect(newX, newY, _selectedRect.Width, _selectedRect.Height);
            UpdateSelectionVisuals();
        }
        else if (_resizeHandle != null)
        {
            ApplyResize(_resizeHandle, canvasPos);
            UpdateSelectionVisuals();
        }
    }

    private void OnRootPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingToolbar)
        {
            _isDraggingToolbar = false;
            e.Pointer.Capture(null);
            return;
        }

        if (_isDragging)
        {
            _isDragging = false;

            // Treat a tiny drag (accidental click) as "no selection"
            if (!HasSelection())
            {
                _selectedRect = default;
                SelectionBox.IsVisible = false;
            }
            UpdateSelectionVisuals();
        }

        _isMoving = false;
        _resizeHandle = null;

        e.Pointer.Capture(null);

        if (_currentMode == ScreenshotCaptureMode.Area && HasSelection())
        {
            AvoidSelectionWithToolbar();
            UpdateHints(); // manipulation just ended, bring the shortcut banner back
        }
    }

    private void ApplyResize(string handle, Point pos)
    {
        var anchorLeft = _resizeAnchorRect.X;
        var anchorRight = _resizeAnchorRect.X + _resizeAnchorRect.Width;
        var anchorTop = _resizeAnchorRect.Y;
        var anchorBottom = _resizeAnchorRect.Y + _resizeAnchorRect.Height;

        var bounds = SelectionCanvas.Bounds;
        var px = Clamp(pos.X, 0, bounds.Width);
        var py = Clamp(pos.Y, 0, bounds.Height);

        var left = handle.Contains('W') ? Math.Min(px, anchorRight - MinSelectionSize) : anchorLeft;
        var right = handle.Contains('E') ? Math.Max(px, anchorLeft + MinSelectionSize) : anchorRight;
        var top = handle.Contains('N') ? Math.Min(py, anchorBottom - MinSelectionSize) : anchorTop;
        var bottom = handle.Contains('S') ? Math.Max(py, anchorTop + MinSelectionSize) : anchorBottom;

        _selectedRect = new Rect(left, top, right - left, bottom - top);
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(Math.Max(value, min), Math.Max(min, max));

    // ---------------------------------------------------------------
    // Keyboard nudging
    // ---------------------------------------------------------------

    private void NudgeMove(int dx, int dy)
    {
        var bounds = SelectionCanvas.Bounds;
        var newX = Clamp(_selectedRect.X + dx, 0, Math.Max(0, bounds.Width - _selectedRect.Width));
        var newY = Clamp(_selectedRect.Y + dy, 0, Math.Max(0, bounds.Height - _selectedRect.Height));
        _selectedRect = new Rect(newX, newY, _selectedRect.Width, _selectedRect.Height);
        UpdateSelectionVisuals();
    }

    private void NudgeResize(int dx, int dy)
    {
        var bounds = SelectionCanvas.Bounds;
        var newWidth = Math.Max(MinSelectionSize, Math.Min(_selectedRect.Width + dx, bounds.Width - _selectedRect.X));
        var newHeight = Math.Max(MinSelectionSize, Math.Min(_selectedRect.Height + dy, bounds.Height - _selectedRect.Y));
        _selectedRect = new Rect(_selectedRect.X, _selectedRect.Y, newWidth, newHeight);
        UpdateSelectionVisuals();
    }

    // ---------------------------------------------------------------
    // Visual sync: box, handles, dimension label, mask, hint, button
    // ---------------------------------------------------------------

    private void UpdateSelectionVisuals()
    {
        Canvas.SetLeft(SelectionBox, _selectedRect.X);
        Canvas.SetTop(SelectionBox, _selectedRect.Y);
        SelectionBox.Width = _selectedRect.Width;
        SelectionBox.Height = _selectedRect.Height;
        // Show real output pixels (HiDPI-aware), not logical units.
        var (pixelScaleX, pixelScaleY) = GetPixelScale();
        DimensionLabel.Text = $"{(int)Math.Round(_selectedRect.Width * pixelScaleX)} × {(int)Math.Round(_selectedRect.Height * pixelScaleY)}";

        // Badge goes below the selection; if there's no room, above; if neither, inside.
        var canvasHeight = SelectionCanvas.Bounds.Height;
        if (canvasHeight <= 0 || canvasHeight - _selectedRect.Bottom >= 36)
        {
            DimensionBadge.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            DimensionBadge.Margin = new Thickness(0, 0, 0, -32);
        }
        else if (_selectedRect.Top >= 36)
        {
            DimensionBadge.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            DimensionBadge.Margin = new Thickness(0, -32, 0, 0);
        }
        else
        {
            DimensionBadge.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            DimensionBadge.Margin = new Thickness(0, 0, 0, 10);
        }

        PositionHandle(HandleNW, _selectedRect.X, _selectedRect.Y);
        PositionHandle(HandleN, _selectedRect.X + _selectedRect.Width / 2, _selectedRect.Y);
        PositionHandle(HandleNE, _selectedRect.X + _selectedRect.Width, _selectedRect.Y);
        PositionHandle(HandleE, _selectedRect.X + _selectedRect.Width, _selectedRect.Y + _selectedRect.Height / 2);
        PositionHandle(HandleSE, _selectedRect.X + _selectedRect.Width, _selectedRect.Y + _selectedRect.Height);
        PositionHandle(HandleS, _selectedRect.X + _selectedRect.Width / 2, _selectedRect.Y + _selectedRect.Height);
        PositionHandle(HandleSW, _selectedRect.X, _selectedRect.Y + _selectedRect.Height);
        PositionHandle(HandleW, _selectedRect.X, _selectedRect.Y + _selectedRect.Height / 2);

        var showHandles = HasSelection();
        var showHorizontalEdges = showHandles && _selectedRect.Width >= EdgeHandleMinLength;
        var showVerticalEdges = showHandles && _selectedRect.Height >= EdgeHandleMinLength;
        HandleNW.IsVisible = showHandles;
        HandleNE.IsVisible = showHandles;
        HandleSE.IsVisible = showHandles;
        HandleSW.IsVisible = showHandles;
        HandleN.IsVisible = showHorizontalEdges;
        HandleS.IsVisible = showHorizontalEdges;
        HandleE.IsVisible = showVerticalEdges;
        HandleW.IsVisible = showVerticalEdges;

        UpdateDimMask();
        UpdateHints();
        UpdateCaptureButtonState();
    }

    /// <summary>Logical-to-output-pixel scale (HiDPI-aware).</summary>
    private (double X, double Y) GetPixelScale()
    {
        var bounds = SelectionCanvas.Bounds;
        if (_frozenSkBitmap is { } frozen && bounds.Width > 0 && bounds.Height > 0)
        {
            return (frozen.Width / bounds.Width, frozen.Height / bounds.Height);
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        return (scaling, scaling);
    }

    private void SelectEntireScreen()
    {
        var bounds = SelectionCanvas.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        _selectedRect = new Rect(0, 0, bounds.Width, bounds.Height);
        SelectionBox.IsVisible = true;
        UpdateSelectionVisuals();
        AvoidSelectionWithToolbar();
    }

    /// <summary>
    /// If the toolbar would sit on top of the selection, hop it to the top of the
    /// screen. Skipped once the user has positioned the toolbar themselves.
    /// </summary>
    private void AvoidSelectionWithToolbar()
    {
        if (_toolbarUserMoved || !HasSelection()) return;

        var root = RootLayout.Bounds;
        var pill = ToolbarPill.Bounds;
        if (root.Width <= 0 || root.Height <= 0 || pill.Width <= 0 || pill.Height <= 0) return;

        var left = (root.Width - pill.Width) / 2.0;
        var bottomTop = root.Height - pill.Height - ToolbarBottomMargin;
        var bottomRect = new Rect(left, bottomTop, pill.Width, pill.Height).Inflate(16);
        var topRect = new Rect(left, ToolbarTopSlot, pill.Width, pill.Height).Inflate(16);

        var targetTop = !bottomRect.Intersects(_selectedRect) ? bottomTop
                      : !topRect.Intersects(_selectedRect) ? ToolbarTopSlot
                      : bottomTop;
        _toolbarTransform.Y = targetTop - bottomTop;
    }

    private static void PositionHandle(Border handle, double centerX, double centerY)
    {
        Canvas.SetLeft(handle, centerX - HandleHitSize / 2);
        Canvas.SetTop(handle, centerY - HandleHitSize / 2);
    }

    private void UpdateDimMask()
    {
        var bounds = SelectionCanvas.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var outer = new RectangleGeometry(new Rect(0, 0, bounds.Width, bounds.Height));

        if (_currentMode == ScreenshotCaptureMode.Area && _selectedRect.Width > 0 && _selectedRect.Height > 0)
        {
            var inner = new RectangleGeometry(_selectedRect);
            var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            group.Children.Add(outer);
            group.Children.Add(inner);
            DimMask.Data = group;
        }
        else
        {
            DimMask.Data = outer;
        }
    }

    // ---------------------------------------------------------------
    // Capture
    // ---------------------------------------------------------------

    private async Task ExecuteInstantFrozenCaptureAsync()
    {
        if (_isCapturing || _frozenSkBitmap is null) return;
        if (_currentMode == ScreenshotCaptureMode.Area && !HasSelection()) return;

        _isCapturing = true;
        using var cancellation = new CancellationTokenSource();
        _captureCancellation = cancellation;
        var ct = cancellation.Token;
        // Transfer ownership to this operation. Escape may close the overlay while
        // PNG encoding is still running; Close must never dispose pixels on a worker.
        var frozen = _frozenSkBitmap;
        _frozenSkBitmap = null;
        var isArea = _currentMode == ScreenshotCaptureMode.Area;
        var selection = _selectedRect;
        var canvas = SelectionCanvas.Bounds;
        string? outputPath = null;
        var completed = false;
        HideCaptureChrome();
        try
        {
            outputPath = _screenshotService.GenerateDestinationPath();
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (isArea)
                {
                    var scaleX = canvas.Width > 0 ? frozen.Width / canvas.Width : 1;
                    var scaleY = canvas.Height > 0 ? frozen.Height / canvas.Height : 1;
                    var x = (int)Math.Clamp(Math.Round(selection.X * scaleX), 0, frozen.Width - 1);
                    var y = (int)Math.Clamp(Math.Round(selection.Y * scaleY), 0, frozen.Height - 1);
                    var width = (int)Math.Clamp(Math.Round(selection.Width * scaleX), 1, frozen.Width - x);
                    var height = (int)Math.Clamp(Math.Round(selection.Height * scaleY), 1, frozen.Height - y);
                    DaisyScreenshotService.SaveCroppedPng(frozen, new SkiaSharp.SKRectI(x, y, x + width, y + height), outputPath);
                }
                else DaisyScreenshotService.SavePng(frozen, outputPath);
            }, ct);
            ct.ThrowIfCancellationRequested();
            completed = true;
            var imageCopied = false;
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    var image = await Task.Run(() => new Bitmap(outputPath), ct);
                    try { ct.ThrowIfCancellationRequested(); await clipboard.SetBitmapAsync(image); }
                    catch { image.Dispose(); throw; }
                    var previous = _clipboardImage;
                    _clipboardImage = image;
                    previous?.Dispose();
                    imageCopied = true;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* A saved file remains useful if clipboard ownership is unavailable. */ }
            ShowFeedback(imageCopied
                ? "Screenshot saved to Pictures/Screenshots — image copied to clipboard"
                : "Screenshot saved to Pictures/Screenshots — image could not be copied");
            FlashOverlay.IsVisible = true;
            FlashOverlay.Opacity = 0.6;
            await Task.Delay(40, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ShowFeedback($"Screenshot failed: {exception.Message}"); }
        finally
        {
            _captureCancellation = null;
            _isCapturing = false;
            FlashOverlay.Opacity = 0;
            FlashOverlay.IsVisible = false;
            if (!completed) DaisyScreenshotService.DeleteFile(outputPath);
            if (completed || ct.IsCancellationRequested || !IsVisible)
            {
                frozen.Dispose();
                Close();
            }
            else
            {
                _frozenSkBitmap = frozen;
                RestoreCaptureChrome();
            }
        }
    }

    private void HideCaptureChrome()
    {
        ToolbarPill.IsVisible = InstructionBanner.IsVisible = ModeHintCard.IsVisible = false;
        ScreenOutline.IsVisible = DimMask.IsVisible = SelectionBox.IsVisible = false;
        HandleNW.IsVisible = HandleN.IsVisible = HandleNE.IsVisible = HandleE.IsVisible =
            HandleSE.IsVisible = HandleS.IsVisible = HandleSW.IsVisible = HandleW.IsVisible = false;
    }

    private void RestoreCaptureChrome()
    {
        CaptureInputChanged?.Invoke(this, true);
        FrozenScreenImage.IsVisible = _frozenAvaloniaBitmap is not null;
        ToolbarPill.IsVisible = InstructionBanner.IsVisible = DimMask.IsVisible = true;
        ToolbarPill.Opacity = 1;
        SelectionBox.IsVisible = _currentMode == ScreenshotCaptureMode.Area && HasSelection();
        UpdateSelectionVisuals();
        Focus();
    }

    private async Task ExecuteCaptureAsync()
    {
        if (_isCapturing) return;
        if (_currentMode == ScreenshotCaptureMode.Area && !HasSelection()) return;

        // If we have an in-memory frozen snapshot, timer is 0, and not capturing a distinct active window,
        // capture is 100% instant from memory (<20ms).
        if (_timerSeconds == 0 && !_includeCursor && _frozenSkBitmap is not null && _currentMode != ScreenshotCaptureMode.Window)
        {
            await ExecuteInstantFrozenCaptureAsync();
            return;
        }

        _isCapturing = true;

        HideCaptureChrome();

        // A delayed or window capture targets the LIVE screen, so stop covering it with
        // the frozen snapshot (otherwise the countdown is pointless and the snapshot could
        // end up in the shot). Restored below if the tool stays open.
        FrozenScreenImage.IsVisible = false;

        using var captureCancellation = new CancellationTokenSource();
        using var countdownCancellation = CancellationTokenSource.CreateLinkedTokenSource(captureCancellation.Token);
        _captureCancellation = captureCancellation;
        _countdownCancellation = countdownCancellation;
        var ct = captureCancellation.Token;
        var completed = false;

        try
        {
            // Clicking skips only the countdown; Escape cancels the whole capture.
            if (_timerSeconds > 0)
            {
                CountdownBadge.IsVisible = true;
                try
                {
                    for (int i = _timerSeconds; i > 0; i--)
                    {
                        CountdownLabel.Text = i.ToString();
                        await Task.Delay(1000, countdownCancellation.Token);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                CountdownBadge.IsVisible = false;
            }

            // Release the exclusive layer-shell focus so KWin can capture the
            // previously active application, not the screenshot tool itself.
            CaptureInputChanged?.Invoke(this, false);
            await WaitForChromeFramesAsync(ct);

            // Prepare screenshot request
            ScreenshotRequest request;
            if (_currentMode == ScreenshotCaptureMode.Area && HasSelection())
            {
                // Account for scale if needed
                var topLevel = TopLevel.GetTopLevel(this);
                var scaling = topLevel?.RenderScaling ?? 1.0;
                var origin = topLevel is null ? default : topLevel.Screens?.ScreenFromTopLevel(topLevel)?.Bounds.Position ?? default;
                request = new ScreenshotRequest(
                    ScreenshotCaptureMode.Area,
                    IncludeCursor: _includeCursor,
                    X: (int)Math.Round(_selectedRect.X * scaling),
                    Y: (int)Math.Round(_selectedRect.Y * scaling),
                    Width: (int)Math.Round(_selectedRect.Width * scaling),
                    Height: (int)Math.Round(_selectedRect.Height * scaling),
                    LogicalArea: new ScreenshotArea(origin.X + (int)Math.Round(_selectedRect.X),
                        origin.Y + (int)Math.Round(_selectedRect.Y),
                        (int)Math.Round(_selectedRect.Width), (int)Math.Round(_selectedRect.Height))
                );
            }
            else if (_currentMode == ScreenshotCaptureMode.Window)
            {
                request = new ScreenshotRequest(ScreenshotCaptureMode.Window, IncludeCursor: _includeCursor);
            }
            else
            {
                request = new ScreenshotRequest(ScreenshotCaptureMode.Screen, IncludeCursor: _includeCursor);
            }

            var result = await _screenshotService.CaptureAsync(request, ct);

            if (result.Succeeded)
            {
                completed = true;
                // Shutter Flash visual feedback
                FlashOverlay.IsVisible = true;
                FlashOverlay.Opacity = 0.6;
                await Task.Delay(40, CancellationToken.None);
                FlashOverlay.Opacity = 0;
            }

            if (result.Succeeded && !string.IsNullOrEmpty(result.Path))
            {
                var imageCopied = false;
                try
                {
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel?.Clipboard is { } clipboard)
                    {
                        // Wayland clipboard ownership requires focus and a valid input serial.
                        CaptureInputChanged?.Invoke(this, true);
                        await WaitForChromeFramesAsync(ct);
                        var image = await Task.Run(() => new Bitmap(result.Path), ct);
                        try { await clipboard.SetBitmapAsync(image); }
                        catch { image.Dispose(); throw; }
                        var previousImage = _clipboardImage;
                        _clipboardImage = image;
                        previousImage?.Dispose();
                        imageCopied = true;
                    }
                }
                catch
                {
                    // Non-fatal if clipboard provider is unavailable
                }

                ShowFeedback(imageCopied
                    ? "Screenshot saved to Pictures/Screenshots — image copied to clipboard"
                    : "Screenshot saved to Pictures/Screenshots — image could not be copied");
            }
            else
            {
                ShowFeedback(result.Error ?? "Screenshot capture failed.");
            }
        }
        catch (OperationCanceledException)
        {
            // Escape cancels the operation, including the backend process.
        }
        catch (Exception ex)
        {
            ShowFeedback($"Screenshot failed: {ex.Message}");
        }
        finally
        {
            _captureCancellation = null;
            _countdownCancellation = null;
            FlashOverlay.IsVisible = false;
            FlashOverlay.Opacity = 0;
            DimMask.IsVisible = true;
            _isCapturing = false;
            CountdownBadge.IsVisible = false;
            if (completed || ct.IsCancellationRequested || !IsVisible) Close();
            else
            {
                RestoreCaptureChrome();
            }
        }
    }

    private async Task WaitForChromeFramesAsync(CancellationToken cancellationToken)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        // Follow the renderer instead of assuming two frames always fit in 32ms.
        topLevel.RequestAnimationFrame(_ => topLevel.RequestAnimationFrame(_ => completion.TrySetResult()));
        await completion.Task;
    }

    private static void ShowFeedback(string message)
    {
        if (Application.Current is App app)
        {
            app.Feedback.Show(message);
        }
    }
}