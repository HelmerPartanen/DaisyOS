using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace DaisyOS.Shell.Controls;

public partial class AppWindowTitleBar : UserControl
{
    private Window? _observedWindow;

    public AppWindowTitleBar()
    {
        InitializeComponent();
    }

    private Window? HostWindow => TopLevel.GetTopLevel(this) as Window;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DetachWindow();
        if (HostWindow is { } window)
        {
            _observedWindow = window;
            window.PropertyChanged += OnWindowPropertyChanged;
            UpdateMaximizeState();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DetachWindow();
    }

    private void DetachWindow()
    {
        if (_observedWindow is { } window)
        {
            window.PropertyChanged -= OnWindowPropertyChanged;
            _observedWindow = null;
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            UpdateMaximizeState();
        }
    }

    private void UpdateMaximizeState()
    {
        if (HostWindow is { } window && this.FindControl<WindowCaptionGlyph>("MaximizeIcon") is { } icon)
        {
            var isMaximized = window.WindowState == WindowState.Maximized;
            icon.Kind = isMaximized ? WindowCaptionKind.Restore : WindowCaptionKind.Maximize;
            if (this.FindControl<Button>("MaximizeButton") is { } btn)
            {
                ToolTip.SetTip(btn, isMaximized ? Loc.Get("Restore") : Loc.Get("Maximize"));
            }
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            (e.Source is Visual source && IsInteractiveControl(source)))
        {
            return;
        }

        if (HostWindow is { } window)
            Apps.SystemAppWindowChrome.HandleTitleBarPress(window, e);
    }

    private static bool IsInteractiveControl(Visual source)
    {
        return source is Button || source is TextBox || source is Controls.SearchBar ||
               source.GetVisualAncestors().Any(v => v is Button || v is TextBox || v is Controls.SearchBar);
    }

    private void OnMinimizeClicked(object? sender, RoutedEventArgs e)
    {
        if (HostWindow is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeRestoreClicked(object? sender, RoutedEventArgs e) => ToggleMaximizeRestore();

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => HostWindow?.Close();

    private void ToggleMaximizeRestore()
    {
        if (HostWindow is not { } window)
        {
            return;
        }

        Apps.SystemAppWindowChrome.ToggleMaximizeRestore(window);
    }
}
