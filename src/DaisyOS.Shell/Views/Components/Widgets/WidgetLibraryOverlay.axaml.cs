using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using DaisyOS.Shell.DesktopWidgets;

namespace DaisyOS.Shell.Views.Components.Widgets;

public partial class WidgetLibraryOverlay : UserControl
{
    public event Action<string, int, int, Point, Point, Size>? WidgetDragRequested;
    public event Action? Dismissed;

    public bool IsOpen => IsVisible;

    public WidgetLibraryOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, _) => FitLibrary();
        this.FindControl<Grid>("PreviewArea")!.SizeChanged += (_, _) => FitPreviews();
    }

    private void FitLibrary()
    {
        if (this.FindControl<Control>("LibraryFrame") is { } frame)
        {
            // Bottom chrome has a 3px screen inset; leave another 3px above it.
            var chromeHeight = this.TryFindResource("ShellChromeHeight", out var value) && value is double height ? height : 52;
            frame.Margin = new Thickness(0, 0, 0, chromeHeight + 6);
            frame.Width = Math.Min(820, Math.Max(0, Bounds.Width - 32));
            frame.Height = Math.Min(480, Math.Max(0, Bounds.Height - frame.Margin.Bottom - 32));
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void FitPreviews()
    {
        var area = this.FindControl<Grid>("PreviewArea")!;
        var metrics = this.FindAncestorOfType<Desktop.DesktopView>()?.CurrentMetrics;
        double cellWidth = metrics?.CellWidth ?? 74;
        double cellHeight = metrics?.CellHeight ?? 88;
        // One scale for every category, including the two stacked photo choices.
        // Reserve both 16px column gaps and 4px for layout rounding.
        // Labels and spacing remain readable while the widgets scale together.
        double scale = Math.Clamp(Math.Min((area.Bounds.Width - 36) / (9 * cellWidth),
            (area.Bounds.Height - 72) / (4 * cellHeight)), 0, 1);
        foreach (var card in area.GetLogicalDescendants().OfType<Border>())
        {
            if (!card.Classes.Contains("PreviewCard") || card.Tag is not string spans) continue;
            var parts = spans.Split(',');
            double width = int.Parse(parts[0]) * cellWidth;
            double height = int.Parse(parts[1]) * cellHeight;
            card.Width = width * scale;
            card.Height = height * scale;
            if (card.Child is Viewbox { Child: Grid content })
            {
                content.Width = width;
                content.Height = height;
            }
        }
    }

    public void Open()
    {
        IsVisible = true;
        FitLibrary();
        FitPreviews();

        var medCal = this.FindControl<CalendarWidget>("MediumCalendarPreview");
        var lrgCal = this.FindControl<CalendarWidget>("LargeCalendarPreview");

        medCal?.SetGridSize(3, 2);
        lrgCal?.SetGridSize(4, 2);
    }

    public void Dismiss()
    {
        IsVisible = false;
        Dismissed?.Invoke();
    }

    private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var card = this.FindControl<Border>("LibraryCard");
        if (card != null)
        {
            var posInCard = e.GetPosition(card);
            if (new Rect(0, 0, card.Bounds.Width, card.Bounds.Height).Contains(posInCard))
            {
                return;
            }
        }

        Dismiss();
        e.Handled = true;
    }

    private void SelectCategory(string selected)
    {
        foreach (var category in new[] { "Clock", "Calendar", "Media", "Photos" })
        {
            this.FindControl<Button>($"{category}CategoryButton")?.Classes.Set("Active", category == selected);
            if (this.FindControl<Control>($"{category}PreviewsPanel") is { } panel)
                panel.IsVisible = category == selected;
        }
        if (this.FindControl<Control>($"{selected}PreviewsPanel") is { } content)
            _ = DaisyOS.Shell.Services.Motion.GuiMotion.RevealAsync(content, 0);
    }

    private void OnClockCategoryClicked(object? sender, RoutedEventArgs e) => SelectCategory("Clock");
    private void OnCalendarCategoryClicked(object? sender, RoutedEventArgs e) => SelectCategory("Calendar");
    private void OnMediaCategoryClicked(object? sender, RoutedEventArgs e) => SelectCategory("Media");
    private void OnPhotosCategoryClicked(object? sender, RoutedEventArgs e) => SelectCategory("Photos");

    private void OnDoneClicked(object? sender, RoutedEventArgs e)
    {
        Dismiss();
    }

    private void StartDragForPreview(string widgetType, int colSpan, int rowSpan, Control card, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;

        var surface = this.FindAncestorOfType<Desktop.DesktopView>();
        Point posInSurface = surface != null ? e.GetPosition(surface) : e.GetPosition(this);
        Point grabOffset = e.GetPosition(card);
        Size cardSize = card.Bounds.Size;

        WidgetDragRequested?.Invoke(widgetType, colSpan, rowSpan, posInSurface, grabOffset, cardSize);
    }

    private void OnClockSweepPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control card)
        {
            StartDragForPreview("clock", 3, 2, card, e);
        }
    }

    private void OnClockAnalogPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control card)
            StartDragForPreview("clock-analog", 3, 2, card, e);
    }

    private void OnClockDigitalPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control card)
        {
            StartDragForPreview("clock-digital", 3, 2, card, e);
        }
    }

    private void OnCalendarMediumPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control card)
        {
            StartDragForPreview("calendar", 3, 2, card, e);
        }
    }

    private void OnCalendarLargePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control card)
        {
            StartDragForPreview("calendar", 4, 2, card, e);
        }
    }

    private void OnMediaMediumPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control card)
        {
            StartDragForPreview("media", 3, 2, card, e);
        }
    }

    private void OnMediaLargePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control card)
        {
            StartDragForPreview("media", 5, 2, card, e);
        }
    }

    private void OnPhotosMediumPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control card)
        {
            StartDragForPreview("image", 3, 2, card, e);
        }
    }

    private void OnPhotosLargePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control card)
        {
            StartDragForPreview("image", 4, 2, card, e);
        }
    }

    private void OnPhotosExtraLargePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control card)
        {
            StartDragForPreview("image", 5, 4, card, e);
        }
    }

    private void OnAddMediaToBottomShelfClicked(object? sender, RoutedEventArgs e)
    {
        var desktopView = this.FindAncestorOfType<Desktop.DesktopView>();
        desktopView?.ViewModel?.MoveMediaWidgetToBottomShelf();
        Dismiss();
    }
}
