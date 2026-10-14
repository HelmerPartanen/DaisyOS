using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Controls;

namespace DaisyOS.Shell.Views.Components.Clipboard;

public partial class ClipboardHistoryWindow : Window
{
    private string _filterQuery = string.Empty;

    public ClipboardHistoryWindow()
    {
        InitializeComponent();
        Loc.RefreshOnChange(this, RefreshList);

        CloseButton.Click += (_, _) => Close();
        ClearAllButton.Click += (_, _) =>
        {
            ClipboardHistoryService.Instance.ClearAll();
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
                Close();
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
        var allItems = ClipboardHistoryService.Instance.GetItems();

        var filtered = string.IsNullOrWhiteSpace(_filterQuery)
            ? allItems
            : allItems.Where(i => i.Text.Contains(_filterQuery, StringComparison.OrdinalIgnoreCase)).ToList();

        EmptyState.IsVisible = filtered.Count == 0;
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
            Background = this.FindResource("SurfaceSunkenBrush") as IBrush ?? Brushes.Transparent,
            CornerRadius = this.FindResource("RoundedMD") as CornerRadius? ?? new CornerRadius(8),
            Padding = new Thickness(12, 8),
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
            ClipboardHistoryService.Instance.TogglePin(item.Id);
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
            ClipboardHistoryService.Instance.RemoveClip(item.Id);
            RefreshList();
        };
        Grid.SetColumn(delBtn, 3);
        grid.Children.Add(delBtn);

        // Click card to paste
        card.PointerPressed += async (s, e) =>
        {
            if (e.Source == pinBtn || e.Source == delBtn) return;
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            {
                await cb.SetTextAsync(item.Text);
            }
            Close();
        };

        card.Child = grid;
        return card;
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

