using Avalonia;
using Avalonia.Controls;
using DaisyOS.Shell.Services.Localization;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Views.Components.Clipboard;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class ClipboardPolishTests
{
    [Fact]
    public void HistoryPreservesWhitespaceAndKeepsDistinctClips()
    {
        var history = new ClipboardHistoryService();
        history.AddClip("  indented text\n");
        history.AddClip("indented text");
        Assert.Equal(2, history.GetItems().Count);
        Assert.Contains(history.GetItems(), item => item.Text == "  indented text\n");
    }

    [AvaloniaFact]
    public async Task KeyboardActivationCopiesExactTextAndClosesHistory()
    {
        const string text = "  keep indentation\n";
        var history = new ClipboardHistoryService();
        history.AddClip(text);
        var window = new ClipboardHistoryWindow(history);
        window.Show();
        try
        {
            var clipboard = window.Clipboard!;
            var card = Assert.IsType<Border>(Assert.Single(window.FindControl<StackPanel>("ItemsContainer")!.Children));
            Assert.True(card.Focus(Avalonia.Input.NavigationMethod.Tab));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (var i = 0; i < 50 && window.IsVisible; i++) await Task.Delay(10);
            Assert.False(window.IsVisible);
            Assert.Equal(text, await clipboard.TryGetTextAsync());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EscapeClearsSearchBeforeClosingAndShowsDistinctEmptyState()
    {
        var history = new ClipboardHistoryService();
        history.AddClip("A saved clip");
        var window = new ClipboardHistoryWindow(history);
        window.Show();
        try
        {
            var search = window.FindControl<SearchBar>("SearchBox")!;
            search.Text = "unmatched query";
            Assert.Equal(Loc.Get("No matching clips"), window.FindControl<TextBlock>("EmptyTitle")!.Text);
            window.KeyPress(Key.Escape, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.True(window.IsVisible);
            Assert.Equal(string.Empty, search.Text);
            window.KeyPress(Key.Escape, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClickingPinAndDeleteIconsDoesNotCopyOrDismiss()
    {
        var history = new ClipboardHistoryService();
        history.AddClip("A saved clip");
        var window = new ClipboardHistoryWindow(history);
        window.Show();
        try
        {
            window.UpdateLayout();
            var host = window.FindControl<StackPanel>("ItemsContainer")!;
            var pin = host.GetVisualDescendants().OfType<Button>().First();
            var icon = (Control)pin.Content!;
            var point = icon.TranslatePoint(new Avalonia.Point(icon.Bounds.Width / 2, icon.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(window.IsVisible);
            Assert.True(Assert.Single(history.GetItems()).IsPinned);
            Assert.False(window.FindControl<Button>("ClearAllButton")!.IsEnabled);
            window.UpdateLayout();
            var delete = host.GetVisualDescendants().OfType<Button>().Last();
            var deleteIcon = (Control)delete.Content!;
            point = deleteIcon.TranslatePoint(new Avalonia.Point(deleteIcon.Bounds.Width / 2, deleteIcon.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(window.IsVisible);
            Assert.Empty(history.GetItems());
        }
        finally { window.Close(); }
    }
}
