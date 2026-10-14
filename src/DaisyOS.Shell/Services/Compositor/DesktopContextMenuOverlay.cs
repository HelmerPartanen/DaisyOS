using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.ViewModels;

namespace DaisyOS.Shell.Services.Compositor;

/// <summary>Hosts desktop menus on the existing flyout surface, above desktop content.</summary>
public sealed class DesktopContextMenuOverlay
{
    public static readonly AttachedProperty<bool> IsHostedProperty =
        AvaloniaProperty.RegisterAttached<DesktopContextMenuOverlay, ContextMenu, bool>("IsHosted");
    public static bool GetIsHosted(ContextMenu menu) => menu.GetValue(IsHostedProperty);
    public static void SetIsHosted(ContextMenu menu, bool value) => menu.SetValue(IsHostedProperty, value);
    private readonly Control _desktop;
    private readonly Control _host;
    private readonly Action<bool> _visibility;
    private ContextMenu? _openMenu;
    public bool IsOpen => _openMenu is not null;

    public DesktopContextMenuOverlay(Control desktop, Control host, Action<bool> visibility)
    {
        _desktop = desktop;
        _host = host;
        _visibility = visibility;
        desktop.AddHandler(Control.ContextRequestedEvent, Requested, RoutingStrategies.Tunnel);
    }

    public void Close() => _openMenu?.Close();

    private void Requested(object? sender, ContextRequestedEventArgs args)
    {
        if (args.Handled || args.Source is not Visual source) return;
        var ancestors = source.GetSelfAndVisualAncestors().OfType<Control>().ToArray();
        // Widgets have their own menu already hosted on this flyout surface.
        if (ancestors.OfType<DesktopItemView>().Any(item => item.DataContext is DesktopItemViewModel { IsWidgetCard: true })) return;
        var owner = ancestors.FirstOrDefault(control => control.ContextMenu is not null);
        if (owner?.ContextMenu is not { } menu) return;
        Close();
        var position = args.TryGetPosition(_desktop, out var pointer)
            ? pointer : owner.TranslatePoint(new Point(0, owner.Bounds.Height), _desktop) ?? default;
        position = _desktop.TranslatePoint(position, TopLevel.GetTopLevel(_desktop)!) ?? position;
        menu.PlacementTarget = _host;
        menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
        menu.PlacementRect = new Rect(position, new Size(1, 1));
        menu.HorizontalOffset = -16;
        menu.VerticalOffset = -16;
        SetIsHosted(menu, true);
        _openMenu = menu;
        menu.Closed += Closed;
        _visibility(true);
        args.Handled = true;
        try { menu.Open(owner); }
        catch { Closed(menu, new RoutedEventArgs()); throw; }
    }

    private void Closed(object? sender, RoutedEventArgs args)
    {
        if (sender is not ContextMenu menu) return;
        menu.Closed -= Closed;
        SetIsHosted(menu, false);
        menu.PlacementTarget = null;
        _openMenu = null;
        _visibility(false);
    }
}
