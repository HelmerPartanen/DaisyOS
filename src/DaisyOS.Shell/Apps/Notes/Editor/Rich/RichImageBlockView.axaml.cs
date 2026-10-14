using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DaisyOS.Shell.Apps.Notes.Documents;

namespace DaisyOS.Shell.Apps.Notes.Editor.Rich;

public partial class RichImageBlockView : UserControl
{
    private NoteBlock? _block;
    private bool _isResizing;
    private bool _isAdjustingRadius;
    private Point _dragStartPoint;
    private double _initialWidth;
    private double _initialRadius;

    public NoteBlock? Block => _block;

    public event EventHandler? MoveUpRequested;
    public event EventHandler? MoveDownRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? Changed;

    public RichImageBlockView()
    {
        InitializeComponent();

        ImageContainer.GotFocus += (_, _) => AdornerOverlay.IsVisible = true;
        ImageContainer.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!ImageContainer.IsKeyboardFocusWithin && !_isResizing && !_isAdjustingRadius) AdornerOverlay.IsVisible = false;
        });
        ImageContainer.PointerEntered += (_, _) => AdornerOverlay.IsVisible = true;
        ImageContainer.PointerExited += (_, _) =>
        {
            if (!ImageContainer.IsKeyboardFocusWithin && !_isResizing && !_isAdjustingRadius)
            {
                AdornerOverlay.IsVisible = false;
            }
        };

        ResizeHandleRight.PointerPressed += OnResizePressed;
        ResizeHandleRight.PointerMoved += OnResizeMoved;
        ResizeHandleRight.PointerReleased += OnResizeReleased;

        RadiusHandle.PointerPressed += OnRadiusPressed;
        RadiusHandle.PointerMoved += OnRadiusMoved;
        RadiusHandle.PointerReleased += OnRadiusReleased;

        CaptionInput.TextChanged += (_, _) =>
        {
            if (_block?.Image != null)
            {
                _block.Image.Caption = CaptionInput.Text;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public void LoadBlock(NoteBlock block, byte[]? assetBytes)
    {
        _block = block;
        if (block.Image == null) return;

        if (assetBytes != null && assetBytes.Length > 0)
        {
            try
            {
                using var ms = new MemoryStream(assetBytes);
                ImageDisplay.Source = new Bitmap(ms);
            }
            catch { }
        }

        var width = block.Image.Width ?? (block.Image.PixelWidth > 0 ? Math.Min(560, block.Image.PixelWidth) : 480);
        ImageBorder.Width = width;

        var radius = block.Image.CornerRadius;
        ImageBorder.CornerRadius = new CornerRadius(radius);

        CaptionInput.Text = block.Image.Caption ?? string.Empty;

        ApplyAlignment(block.Image.Alignment);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (ImageDisplay.Source is Bitmap bitmap) bitmap.Dispose();
        ImageDisplay.Source = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void ApplyAlignment(ImageAlignment alignment)
    {
        if (_block?.Image != null) _block.Image.Alignment = alignment;

        AlignLeftBtn.Classes.Set("Active", alignment == ImageAlignment.Left);
        AlignCenterBtn.Classes.Set("Active", alignment == ImageAlignment.Center);
        AlignRightBtn.Classes.Set("Active", alignment == ImageAlignment.Right);
        AlignFullBtn.Classes.Set("Active", alignment == ImageAlignment.FullWidth);

        ImageContainer.HorizontalAlignment = alignment switch
        {
            ImageAlignment.Left => HorizontalAlignment.Left,
            ImageAlignment.Right => HorizontalAlignment.Right,
            ImageAlignment.FullWidth => HorizontalAlignment.Stretch,
            _ => HorizontalAlignment.Center
        };

        if (alignment == ImageAlignment.FullWidth)
        {
            ImageBorder.Width = double.NaN;
        }
        else if (double.IsNaN(ImageBorder.Width) && _block?.Image != null)
        {
            ImageBorder.Width = _block.Image.Width ?? 480;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetRadius(double radius)
    {
        radius = Math.Clamp(radius, 0, 48);
        ImageBorder.CornerRadius = new CornerRadius(radius);
        if (_block?.Image != null) _block.Image.CornerRadius = radius;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        _isResizing = true;
        _dragStartPoint = e.GetPosition(this);
        _initialWidth = double.IsNaN(ImageBorder.Width) ? ImageBorder.Bounds.Width : ImageBorder.Width;
        FeedbackBadge.IsVisible = true;
        e.Pointer.Capture(ResizeHandleRight);
        e.Handled = true;
    }

    private void OnResizeMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizing) return;
        var currentPoint = e.GetPosition(this);
        var deltaX = (currentPoint.X - _dragStartPoint.X) * 2; // Symmetric resize from center
        var newWidth = Math.Clamp(_initialWidth + deltaX, 120, 800);

        ImageBorder.Width = newWidth;
        if (_block?.Image != null) _block.Image.Width = newWidth;

        FeedbackBadgeText.Text = Loc.Format("{0} px", Math.Round(newWidth));
        e.Handled = true;
    }

    private void OnResizeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isResizing) return;
        _isResizing = false;
        FeedbackBadge.IsVisible = false;
        e.Pointer.Capture(null);
        Changed?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnRadiusPressed(object? sender, PointerPressedEventArgs e)
    {
        _isAdjustingRadius = true;
        _dragStartPoint = e.GetPosition(this);
        _initialRadius = ImageBorder.CornerRadius.TopLeft;
        FeedbackBadge.IsVisible = true;
        e.Pointer.Capture(RadiusHandle);
        e.Handled = true;
    }

    private void OnRadiusMoved(object? sender, PointerEventArgs e)
    {
        if (!_isAdjustingRadius) return;
        var currentPoint = e.GetPosition(this);
        var delta = (currentPoint.X - _dragStartPoint.X) + (currentPoint.Y - _dragStartPoint.Y);
        var newRadius = Math.Clamp(_initialRadius + delta, 0, 48);

        SetRadius(newRadius);
        FeedbackBadgeText.Text = Loc.Format("{0} px radius", Math.Round(newRadius));
        e.Handled = true;
    }

    private void OnRadiusReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isAdjustingRadius) return;
        _isAdjustingRadius = false;
        FeedbackBadge.IsVisible = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnAlignLeftClicked(object? sender, RoutedEventArgs e) => ApplyAlignment(ImageAlignment.Left);
    private void OnAlignCenterClicked(object? sender, RoutedEventArgs e) => ApplyAlignment(ImageAlignment.Center);
    private void OnAlignRightClicked(object? sender, RoutedEventArgs e) => ApplyAlignment(ImageAlignment.Right);
    private void OnAlignFullClicked(object? sender, RoutedEventArgs e) => ApplyAlignment(ImageAlignment.FullWidth);

    private void OnRadius0Clicked(object? sender, RoutedEventArgs e) => SetRadius(0);
    private void OnRadius8Clicked(object? sender, RoutedEventArgs e) => SetRadius(8);
    private void OnRadius16Clicked(object? sender, RoutedEventArgs e) => SetRadius(16);

    private void OnMoveUpClicked(object? sender, RoutedEventArgs e) => MoveUpRequested?.Invoke(this, EventArgs.Empty);
    private void OnMoveDownClicked(object? sender, RoutedEventArgs e) => MoveDownRequested?.Invoke(this, EventArgs.Empty);
    private void OnDeleteClicked(object? sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
}
