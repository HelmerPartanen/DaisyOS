using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using System.Threading.Tasks;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Controls;

namespace DaisyOS.Shell.Views.Components.Clipboard;

public partial class ClipboardHistoryWindow : Window
{
    private string _filterQuery = string.Empty;
    private bool _copying;

    private readonly ClipboardHistoryService _history;

    public ClipboardHistoryWindow() : this(ClipboardHistoryService.Instance) { }

    internal ClipboardHistoryWindow(ClipboardHistoryService history)
    {
        _history = history;
        InitializeComponent();
        Loc.RefreshOnChange(this, RefreshList);

        Avalonia.Automation.AutomationProperties.SetName(CloseButton, Loc.Get("Close"));
        CloseButton.Click += (_, _) => Close();
        ClearAllButton.Click += (_, _) =>
        {
            _history.ClearAll();
            RefreshList();
        };

        SearchBox.PropertyChanged += (_, args) =>
        {
            if (args.Property != Controls.SearchBar.TextProperty) return;
            _filterQuery = SearchBox.Text?.Trim() ?? string.Empty;
            RefreshList();
        };

        AddHandler(KeyDownEvent, (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = string.Empty;
                else Close();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        Opened += (_, _) =>
        {
            SearchBox.FocusInput();
            RefreshList();
        };
    }

    public void RefreshList()
    {
        ItemsContainer.Children.Clear();
        var allItems = _history.GetItems();

        var filtered = string.IsNullOrWhiteSpace(_filterQuery)
            ? allItems
            : allItems.Where(i => i.Text.Contains(_filterQuery, StringComparison.OrdinalIgnoreCase)).ToList();

        EmptyState.IsVisible = filtered.Count == 0;
        EmptyTitle.Text = Loc.Get(string.IsNullOrEmpty(_filterQuery) ? "No clips yet" : "No matching clips");
        EmptyDetail.Text = Loc.Get(string.IsNullOrEmpty(_filterQuery) ? "Copy text to see it here." : "Try a different search or clear the filter.");
        ClearAllButton.IsEnabled = allItems.Any(item => !item.IsPinned);
        StatusText.Text = allItems.Count == 1 ? Loc.Get("1 clip saved") : Loc.Format("{0} clips saved", allItems.Count);

        foreach (var item in filtered)
        {
            var card = BuildClipCard(item);
            ItemsContainer.Children.Add(card);
        }
    }

    private Border BuildClipCard(ClipboardItem item)
    {
        var card = new Border
        {
            Classes = { "ClipCard" },
            CornerRadius = this.FindResource("RoundedMD") as CornerRadius? ?? new CornerRadius(8),
            Padding = new Thickness(12, 8),
            Focusable = true,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto")
        };

        // Pin icon or copy icon
        var icon = new DaisyIcon
        {
            Icon = item.IsPinned ? "push-pin-fill" : "clipboard",
            Size = 16,
            Foreground = item.IsPinned
                ? this.FindResource("AccentPrimaryBrush") as IBrush ?? Brushes.Blue
                : this.FindResource("ContentTertiaryBrush") as IBrush ?? Brushes.Gray,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        // Text & Metadata
        var textStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2
        };
        var previewText = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = item.Preview,
            FontSize = 12,
            FontWeight = FontWeight.Normal,
            Foreground = this.FindResource("ContentPrimaryBrush") as IBrush ?? Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2
        }, "Caption");
        var metaText = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = Loc.Format("{0} • {1} chars", FormatTimeAgo(item.Timestamp), item.Text.Length),
            FontSize = 10,
            Foreground = this.FindResource("ContentTertiaryBrush") as IBrush ?? Brushes.Gray
        }, "Caption");
        textStack.Children.Add(previewText);
        textStack.Children.Add(metaText);
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        // Pin button
        var pinBtn = new Button
        {
            Classes = { "GhostButton" },
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new DaisyIcon
            {
                Icon = item.IsPinned ? "push-pin-slash" : "push-pin",
                Size = 14,
                Foreground = this.FindResource("ContentSecondaryBrush") as IBrush ?? Brushes.Gray
            }
        };
        pinBtn.Click += (s, e) =>
        {
            _history.TogglePin(item.Id);
            RefreshList();
        };
        Grid.SetColumn(pinBtn, 2);
        grid.Children.Add(pinBtn);

        // Delete button
        var delBtn = new Button
        {
            Classes = { "GhostButton" },
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new DaisyIcon
            {
                Icon = "trash",
                Size = 14,
                Foreground = this.FindResource("ContentTertiaryBrush") as IBrush ?? Brushes.Gray
            }
        };
        delBtn.Click += (s, e) =>
        {
            _history.RemoveClip(item.Id);
            RefreshList();
        };
        Grid.SetColumn(delBtn, 3);
        grid.Children.Add(delBtn);

        ToolTip.SetTip(pinBtn, Loc.Get(item.IsPinned ? "Unpin" : "Pin"));
        ToolTip.SetTip(delBtn, Loc.Get("Delete"));
        Avalonia.Automation.AutomationProperties.SetName(pinBtn, Loc.Get(item.IsPinned ? "Unpin" : "Pin"));
        Avalonia.Automation.AutomationProperties.SetName(delBtn, Loc.Get("Delete"));
        Avalonia.Automation.AutomationProperties.SetName(card, item.Preview);

        // Nested actions must not also copy the clip and close the window.
        card.PointerPressed += async (s, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed ||
                e.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;
            e.Handled = true;
            await CopyClipAsync(item);
        };
        card.KeyDown += async (_, e) =>
        {
            if (!ReferenceEquals(e.Source, card) || e.Key is not (Key.Enter or Key.Space)) return;
            e.Handled = true;
            await CopyClipAsync(item);
        };

        card.Child = grid;
        return card;
    }

    private async Task CopyClipAsync(ClipboardItem item)
    {
        if (_copying) return;
        _copying = true;
        try
        {
            if (Clipboard is not { } clipboard)
            {
                StatusText.Text = Loc.Get("Clipboard is unavailable. Try again.");
                return;
            }
            await clipboard.SetTextAsync(item.Text);
            Close();
        }
        catch (Exception)
        {
            StatusText.Text = Loc.Get("Couldn’t copy this clip. Try again.");
        }
        finally { _copying = false; }
    }

    private static string FormatTimeAgo(DateTimeOffset time)
    {
        var diff = DateTimeOffset.Now - time;
        if (diff.TotalSeconds < 60) return Loc.Get("Just now");
        if (diff.TotalMinutes < 60) return Loc.Format("{0}m ago", (int)diff.TotalMinutes);
        if (diff.TotalHours < 24) return Loc.Format("{0}h ago", (int)diff.TotalHours);
        return time.ToString("MMM d");
    }
}

