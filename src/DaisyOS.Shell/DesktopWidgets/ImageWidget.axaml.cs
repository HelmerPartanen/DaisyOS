using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Wallpaper;
using DaisyOS.Shell.ViewModels;

namespace DaisyOS.Shell.DesktopWidgets;

public partial class ImageWidget : UserControl
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"
    };

    private readonly List<string> _imagePaths = new();
    private int _currentIndex;
    private int _activeLayer; // 0: ImageLayerA, 1: ImageLayerB
    private bool _isPickingImages;
    private Point? _placeholderPress;
    private IPointer? _placeholderPointer;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };

    private int _columnSpan = 3;
    private int _rowSpan = 2;

    public event Action? ImagesChanged;

    public IReadOnlyList<string> ImagePaths => _imagePaths.AsReadOnly();

    public int CycleIntervalSeconds
    {
        get => (int)_timer.Interval.TotalSeconds;
        set
        {
            int sec = Math.Clamp(value, 2, 300);
            _timer.Interval = TimeSpan.FromSeconds(sec);
        }
    }

    public ImageWidget()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        _timer.Tick += (_, _) => ShowNextImage();

        SetImages(Array.Empty<string>());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_imagePaths.Count > 1 && !IsPointerOver)
        {
            _timer.Start();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (_timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_imagePaths.Count > 1 && !_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    public void SetGridSize(int columnSpan, int rowSpan)
    {
        _columnSpan = Math.Max(1, columnSpan);
        _rowSpan = Math.Max(1, rowSpan);

        var container = this.FindControl<WidgetContainer>("Container");
        container?.SetGridSize(_columnSpan, _rowSpan);
    }

    public void SetImages(IEnumerable<string> paths)
    {
        _imagePaths.Clear();

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (File.Exists(path) && IsImageFile(path))
            {
                _imagePaths.Add(path);
            }
        }

        _currentIndex = 0;
        _activeLayer = 0;

        var placeholder = this.FindControl<Border>("PlaceholderView");
        var imageA = this.FindControl<Image>("ImageLayerA");
        var imageB = this.FindControl<Image>("ImageLayerB");
        var multipleBar = this.FindControl<Border>("MultipleImagesBar");
        var counterText = this.FindControl<TextBlock>("CounterText");

        if (_imagePaths.Count == 0)
        {
            _timer.Stop();
            if (placeholder != null) placeholder.IsVisible = true;
            if (imageA != null) { imageA.Source = null; imageA.Opacity = 0; }
            if (imageB != null) { imageB.Source = null; imageB.Opacity = 0; }
            if (multipleBar != null) multipleBar.IsVisible = false;
            ImagesChanged?.Invoke();
            return;
        }

        if (placeholder != null) placeholder.IsVisible = false;

        var firstBitmap = DesktopItemLoader.LoadBitmapSafe(_imagePaths[0]);
        if (imageA != null)
        {
            imageA.Source = firstBitmap;
            imageA.Opacity = 1.0;
        }
        if (imageB != null)
        {
            imageB.Source = null;
            imageB.Opacity = 0.0;
        }

        if (_imagePaths.Count == 1)
        {
            _timer.Stop();
            if (multipleBar != null) multipleBar.IsVisible = false;
        }
        else
        {
            if (multipleBar != null) multipleBar.IsVisible = true;
            if (counterText != null) counterText.Text = $"1 / {_imagePaths.Count}";
            _timer.Start();
        }

        ImagesChanged?.Invoke();
    }

    public void ShowNextImage()
    {
        if (_imagePaths.Count <= 1) return;
        int nextIndex = (_currentIndex + 1) % _imagePaths.Count;
        TransitionToImage(nextIndex);
    }

    public void ShowPreviousImage()
    {
        if (_imagePaths.Count <= 1) return;
        int prevIndex = (_currentIndex - 1 + _imagePaths.Count) % _imagePaths.Count;
        TransitionToImage(prevIndex);
    }

    private void TransitionToImage(int nextIndex)
    {
        if (nextIndex < 0 || nextIndex >= _imagePaths.Count) return;
        var bitmap = DesktopItemLoader.LoadBitmapSafe(_imagePaths[nextIndex]);
        if (bitmap == null)
        {
            (Application.Current as App)?.Feedback.Show(Loc.Get("That photo could not be displayed."));
            return;
        }

        _currentIndex = nextIndex;

        var imageA = this.FindControl<Image>("ImageLayerA");
        var imageB = this.FindControl<Image>("ImageLayerB");
        var counterText = this.FindControl<TextBlock>("CounterText");

        if (_activeLayer == 0)
        {
            if (imageB != null)
            {
                imageB.Source = bitmap;
                imageB.Opacity = 1.0;
            }
            if (imageA != null)
            {
                imageA.Opacity = 0.0;
            }
            _activeLayer = 1;
        }
        else
        {
            if (imageA != null)
            {
                imageA.Source = bitmap;
                imageA.Opacity = 1.0;
            }
            if (imageB != null)
            {
                imageB.Opacity = 0.0;
            }
            _activeLayer = 0;
        }

        if (counterText != null)
        {
            counterText.Text = $"{_currentIndex + 1} / {_imagePaths.Count}";
        }
    }

    /// <summary>
    /// Opens the same file picker application used by the Appearance settings to choose background photos.
    /// Supports selecting one or multiple photos.
    /// </summary>
    public async Task PickImagesAsync()
    {
        if (_isPickingImages) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || Application.Current is not App app) return;

        try
        {
            _isPickingImages = true;
            _timer.Stop();
            var files = await app.PickImageFilesAsync(
                topLevel,
                allowMultiple: true,
                initialSelection: _imagePaths,
                allowFolderContents: true);
            var selectedPaths = files
                .Where(path => File.Exists(path) && IsImageFile(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // The picker returns the complete edited selection, including an
            // intentionally empty result after the user removes every photo.
            SetImages(selectedPaths);
        }
        finally
        {
            _isPickingImages = false;
            if (_imagePaths.Count > 1 && VisualRoot is not null && !IsPointerOver)
            {
                _timer.Start();
            }
        }
    }

    private async void OnChoosePhotosClicked(object? sender, RoutedEventArgs e)
    {
        await PickImagesAsync();
    }

    private void OnPlaceholderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _placeholderPress = e.GetPosition(this);
        _placeholderPointer = e.Pointer;
    }

    private void OnPlaceholderReleased(object? sender, PointerReleasedEventArgs e)
    {
        var start = _placeholderPress;
        var matches = _placeholderPointer == e.Pointer;
        _placeholderPress = null;
        _placeholderPointer = null;
        if (!matches || start is null || e.InitialPressMouseButton != MouseButton.Left) return;
        var delta = e.GetPosition(this) - start.Value;
        if (delta.X * delta.X + delta.Y * delta.Y >= 36) return;
        // Let the desktop finish its pointer release before opening a modal picker.
        Dispatcher.UIThread.Post(async () => await PickImagesAsync());
    }

    private void OnPlaceholderCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _placeholderPress = null;
        _placeholderPointer = null;
    }

    private void OnPreviousClicked(object? sender, RoutedEventArgs e)
    {
        ShowPreviousImage();
    }

    private void OnNextClicked(object? sender, RoutedEventArgs e)
    {
        ShowNextImage();
    }

    private static bool IsImageFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return SupportedImageExtensions.Contains(ext);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files != null && files.Any(f => IsImageFile(f.Path.LocalPath)))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files != null)
        {
            var droppedImages = files
                .Select(f => f.Path.LocalPath)
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p) && IsImageFile(p))
                .ToList();

            if (droppedImages.Count > 0)
            {
                SetImages(droppedImages);
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }
    }
}
