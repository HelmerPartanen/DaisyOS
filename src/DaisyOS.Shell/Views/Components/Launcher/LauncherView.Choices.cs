using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.ViewModels;

namespace DaisyOS.Shell.Views.Components.Launcher;

public partial class LauncherView
{
    private Border? _choiceMenu;
    private ListBox? _choiceList;
    private ComboBox? _choiceAnchor;
    private SettingSearchResultViewModel? _choiceSetting;
    public event EventHandler<bool>? SearchChoiceMenuVisibilityChanged;
    public bool IsSearchChoiceMenuVisible => _choiceMenu?.IsVisible == true;

    public Control? DetachSearchChoiceMenuForNativeOverlay()
    {
        _choiceMenu = this.FindControl<Border>("LauncherChoiceMenuHost");
        _choiceList = this.FindControl<ListBox>("LauncherChoiceMenuList");
        _choiceList?.AddHandler(InputElement.KeyDownEvent, OnSearchChoiceKeyDown, RoutingStrategies.Tunnel);
        if (_choiceMenu?.Parent is Panel panel) panel.Children.Remove(_choiceMenu);
        return _choiceMenu;
    }

    private void OnSearchChoiceOpened(object? sender, EventArgs e)
    {
        // Windowed hosts keep Avalonia's ordinary popup. Native shells use the same
        // separate, blurred layer-shell surface as the launcher power menu.
        if (_choiceMenu is null || _choiceList is null || sender is not ComboBox combo ||
            combo.DataContext is not SettingSearchResultViewModel setting ||
            TopLevel.GetTopLevel(_choiceMenu) is not { } surface) return;
        // Finish the popup's two-way IsOpen notification before switching hosts.
        // Closing it synchronously inside DropDownOpened re-enters its binding.
        Dispatcher.UIThread.Post(() => OpenSearchChoiceOverlay(combo, setting), DispatcherPriority.Input);
    }

    private void OpenSearchChoiceOverlay(ComboBox combo, SettingSearchResultViewModel setting)
    {
        if (!combo.IsDropDownOpen || !combo.IsAttachedToVisualTree() || _choiceMenu is null || _choiceList is null ||
            TopLevel.GetTopLevel(_choiceMenu) is not { } surface) return;
        combo.IsDropDownOpen = false;
        ClosePowerMenu();
        CloseAppContextMenu();
        _choiceAnchor = combo;
        _choiceSetting = setting;
        var items = setting.Choices.Select(value => new ComboBoxItem { Content = Loc.Get(value), Tag = value }).ToArray();
        _choiceList.ItemsSource = items;
        _choiceList.SelectedItem = items.FirstOrDefault(item => Equals(item.Tag, setting.Choice));
        _choiceMenu.Margin = default;
        _choiceMenu.MaxWidth = double.PositiveInfinity;
        _choiceMenu.MaxHeight = double.PositiveInfinity;
        _choiceMenu.Width = Math.Max(144, combo.Bounds.Width);
        _choiceMenu.IsVisible = true;
        // KWin receives the material geometry before the overlay is mapped.
        _choiceMenu.Measure(Size.Infinity);
        var topLeft = surface.PointToClient(combo.PointToScreen(default));
        var bottomRight = surface.PointToClient(combo.PointToScreen(new Point(combo.Bounds.Width, combo.Bounds.Height)));
        var bounds = new Rect(topLeft, bottomRight);
        var placement = PlaceSearchChoiceMenu(bounds, surface.Bounds.Size, _choiceMenu.DesiredSize);
        _choiceMenu.Width = placement.Width;
        _choiceMenu.MaxWidth = placement.Width;
        _choiceMenu.MaxHeight = placement.Height;
        _choiceMenu.Margin = new Thickness(placement.X, placement.Y, 0, 0);
        _choiceMenu.Measure(placement.Size);
        SearchChoiceMenuVisibilityChanged?.Invoke(this, true);
        _choiceList.UpdateLayout();
        _choiceList.Focus();
        (_choiceList.SelectedItem as Control)?.Focus();
    }

    internal static Rect PlaceSearchChoiceMenu(Rect anchor, Size surface, Size desired)
    {
        const double inset = 6, gap = 4;
        var width = Math.Min(desired.Width, Math.Max(0, surface.Width - inset * 2));
        var below = Math.Max(0, surface.Height - inset - anchor.Bottom - gap);
        var above = Math.Max(0, anchor.Top - gap - inset);
        var openBelow = desired.Height <= below || below >= above;
        var height = Math.Min(desired.Height, openBelow ? below : above);
        var x = Math.Clamp(anchor.Right - width, inset, Math.Max(inset, surface.Width - inset - width));
        var y = openBelow ? anchor.Bottom + gap : anchor.Top - gap - height;
        y = Math.Clamp(y, inset, Math.Max(inset, surface.Height - inset - height));
        return new Rect(x, y, width, height);
    }

    public void CloseSearchChoiceMenu()
    {
        if (_choiceMenu is not { IsVisible: true }) return;
        _choiceMenu.IsVisible = false;
        _choiceSetting = null;
        var anchor = _choiceAnchor;
        _choiceAnchor = null;
        SearchChoiceMenuVisibilityChanged?.Invoke(this, false);
        if (IsEffectivelyVisible && anchor?.IsEffectivelyVisible == true && !anchor.Focus())
            FocusSearchAtEnd();
    }

    private void CommitSearchChoice()
    {
        if (_choiceSetting is { CanUpdate: true } setting && _choiceList?.SelectedItem is ComboBoxItem { Tag: string value })
            setting.Choice = value;
        CloseSearchChoiceMenu();
    }

    private void OnSearchChoiceKeyDown(object? sender, KeyEventArgs e)
    {
        if (_choiceList is { } list && e.Key is Key.Up or Key.Down or Key.Home or Key.End)
        {
            list.SelectedIndex = e.Key switch
            {
                Key.Home => 0, Key.End => list.ItemCount - 1,
                Key.Up => Math.Max(0, list.SelectedIndex - 1),
                _ => Math.Min(list.ItemCount - 1, list.SelectedIndex + 1)
            };
            if (list.SelectedItem is Control item) { list.ScrollIntoView(item); item.Focus(); }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter) { CommitSearchChoice(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseSearchChoiceMenu(); e.Handled = true; }
        else if (e.Key == Key.Tab) { CloseSearchChoiceMenu(); e.Handled = true; }
    }

    private void OnSearchChoiceReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Visual visual &&
            visual.GetSelfAndVisualAncestors().OfType<ComboBoxItem>().Any())
        { CommitSearchChoice(); e.Handled = true; }
    }
}
