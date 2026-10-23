using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Desktop;


namespace DaisyOS.Shell.Controls;

public partial class DesktopItemView : UserControl
{
    public static readonly StyledProperty<bool> IsDragProxyProperty =
        AvaloniaProperty.Register<DesktopItemView, bool>(nameof(IsDragProxy));

    public bool IsDragProxy
    {
        get => GetValue(IsDragProxyProperty);
        set => SetValue(IsDragProxyProperty, value);
    }

    public static readonly StyledProperty<bool> IsHiddenPlaceholderProperty =
        AvaloniaProperty.Register<DesktopItemView, bool>(nameof(IsHiddenPlaceholder));

    public bool IsHiddenPlaceholder
    {
        get => GetValue(IsHiddenPlaceholderProperty);
        set => SetValue(IsHiddenPlaceholderProperty, value);
    }

    private DesktopItemViewModel? _subscribedVm;
    private ContextMenu? _contextMenu;

    public DesktopItemView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, (_, _) =>
        {
            if (_contextMenu is null || !DaisyOS.Shell.Services.Compositor.DesktopContextMenuOverlay.GetIsHosted(_contextMenu)) CloseContextMenu();
        }, RoutingStrategies.Bubble);
    }


    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }

        if (DataContext is DesktopItemViewModel vm)
        {
            _subscribedVm = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            SyncProperties(vm);

            if (this.FindControl<Button>("RemoveWidgetButton") is { } btn)
            {
                btn.IsVisible = true;
            }
            if (this.FindControl<ContentPresenter>("WidgetCard") is { } card)
            {
                card.Opacity = 1;
            }
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel { IsRenaming: true }) FocusRenameEditor();
        _contextMenu = this.FindControl<Panel>("RootPanel")?.ContextMenu;
        if (_contextMenu is null)
            return;

        _contextMenu.Opened += OnContextMenuVisualChanged;
        _contextMenu.Closed += OnContextMenuVisualChanged;
    }

    private void OnContextMenuVisualChanged(object? sender, RoutedEventArgs e) =>
        FindParentDesktopView()?.NotifyContextMenuVisualChanged();

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not DesktopItemViewModel { IsWidgetCard: true } widget)
            return;

        var desktop = FindParentDesktopView();
        if (desktop is null)
            return;

        e.TryGetPosition(desktop, out var position);
        desktop.ShowWidgetContextMenu(widget, position);
        e.Handled = true;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_contextMenu is not null)
        {
            _contextMenu.Opened -= OnContextMenuVisualChanged;
            _contextMenu.Closed -= OnContextMenuVisualChanged;
            _contextMenu = null;
        }

        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            SyncProperties(vm);
            if (e.PropertyName == nameof(DesktopItemViewModel.IsRenaming) && vm.IsRenaming)
            {
                FocusRenameEditor();
            }
        }
    }

    private void FocusRenameEditor() => Dispatcher.UIThread.Post(() =>
    {
        if (DataContext is not DesktopItemViewModel { IsRenaming: true }) return;
        if (this.FindControl<TextBox>("RenameBox") is { } editor)
        {
            editor.Focus();
            var extension = Path.GetExtension(editor.Text ?? "");
            editor.SelectionStart = 0;
            editor.SelectionEnd = DataContext is DesktopItemViewModel { IsDirectory: false } && extension.Length > 0
                ? (editor.Text?.Length ?? 0) - extension.Length : editor.Text?.Length ?? 0;
        }
    }, DispatcherPriority.Input);

    private void SyncProperties(DesktopItemViewModel vm)
    {
        ZIndex = vm.IsDragging ? 100 : vm.IsRenaming ? 50 : 0;
        IsDragProxy = vm.IsDragging;
        IsHiddenPlaceholder = vm.IsDragging ? false : vm.IsHiddenPlaceholder;
        Opacity = vm.IsCut ? 0.5 : 1;
    }

    public void CloseContextMenu()
    {
        if (this.ContextMenu is { IsOpen: true } menu)
        {
            menu.Close();
        }
        var rootPanel = this.FindControl<Panel>("RootPanel");
        if (rootPanel?.ContextMenu is { IsOpen: true } panelMenu)
        {
            panelMenu.Close();
        }
    }

    private DesktopView? FindParentDesktopView() =>

        this.FindAncestorOfType<DesktopView>();


    private DesktopViewModel? FindParentDesktopViewModel() =>
        FindParentDesktopView()?.ViewModel;

    // ============================================================
    // INLINE RENAME HANDLERS
    // ============================================================

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not DesktopItemViewModel vm || sender is not TextBox renameBox) return;

        if (e.Key == Key.Escape)
        {
            FindParentDesktopViewModel()?.CancelRename(vm);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            FindParentDesktopViewModel()?.CommitRename(vm, renameBox.Text ?? string.Empty);
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm && vm.IsRenaming && sender is TextBox renameBox)
        {
            FindParentDesktopViewModel()?.CommitRename(vm, renameBox.Text ?? string.Empty);
        }
    }

    // ============================================================
    // CONTEXT MENU ACTION HANDLERS
    // ============================================================

    private void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            FindParentDesktopView()?.ActivateItem(vm);
        }
    }

    private void OnSetWallpaperClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            FindParentDesktopViewModel()?.SetAsWallpaper(vm);
        }
    }

    private void OnOpenInTerminalClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            FindParentDesktopViewModel()?.OpenInTerminal(vm.State.Id);
        }
    }

    private void OnPinToTaskbarClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm && Application.Current is App app)
        {
            var appId = global::System.IO.Path.GetFileNameWithoutExtension(vm.State.Id);
            if (!app.SessionState.DockOrder.Contains(appId, StringComparer.OrdinalIgnoreCase))
            {
                // This updates persistence and immediately reconciles the live
                // taskbar (or forwards to the desktop-settings owner process).
                app.PinApplicationToTaskbar(appId);
                app.Feedback.Show(Loc.Format("Pinned {0} to Taskbar.", vm.Label));
            }
        }
    }



    private void OnShareLocalSendClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            FindParentDesktopViewModel()?.ShareWithLocalSend(vm);
        }
    }

    private void OnRenameClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            FindParentDesktopViewModel()?.StartRenaming(vm);
        }
    }

    private void OnCutClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            _ = FindParentDesktopViewModel()?.CutItemToClipboardAsync(vm, topLevel?.Clipboard);
        }
    }

    private void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            _ = FindParentDesktopViewModel()?.CopyItemToClipboardAsync(vm, topLevel?.Clipboard);
        }
    }

    private void OnCopyPathClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            FindParentDesktopViewModel()?.CopyPathToClipboard(vm, topLevel?.Clipboard);
        }
    }

    private void OnCompressClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            var desktopView = FindParentDesktopView();
            FindParentDesktopViewModel()?.CompressItem(vm, desktopView?.CurrentMetrics);
        }
    }

    private async void OnEmptyTrashClicked(object? sender, RoutedEventArgs e)
    {
        if (FindParentDesktopViewModel() is not { } model) return;
        CloseContextMenu();
        try
        {
            if (!await model.EmptyTrashAsync())
                (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t empty the trash."));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[trash] Empty action failed: {exception.Message}");
            (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t empty the trash."));
        }
    }

    private async void OnMoveToTrashClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            var desktopView = FindParentDesktopView();
            if (FindParentDesktopViewModel() is { } model)
                await model.MoveItemToTrash(vm, desktopView?.CurrentMetrics);
        }
    }

    private void OnRemoveWidgetClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            var desktopView = FindParentDesktopView();
            FindParentDesktopViewModel()?.RemoveWidget(vm, desktopView?.CurrentMetrics);
            desktopView?.InvalidateReorderPanel();
        }
    }

    private async void OnMoveToBottomShelfClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            CloseContextMenu();
            var desktopView = FindParentDesktopView();
            var desktopVm = FindParentDesktopViewModel();

            Opacity = 0;
            if (this.FindControl<Button>("RemoveWidgetButton") is { } btn)
            {
                btn.IsVisible = false;
            }
            if (this.FindControl<ContentPresenter>("WidgetCard") is { } card)
            {
                card.Opacity = 0;
            }
            await Task.Delay(160);

            desktopVm?.MoveMediaWidgetToBottomShelf(vm);
            desktopView?.InvalidateReorderPanel();
        }
    }

    private void OnSizeSmallClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            var desktopView = FindParentDesktopView();
            FindParentDesktopViewModel()?.ResizeWidget(vm, 1, 1, desktopView?.CurrentMetrics);
            CloseContextMenu();
            desktopView?.InvalidateReorderPanel();
        }
    }

    private void OnSizeMediumClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            var desktopView = FindParentDesktopView();
            FindParentDesktopViewModel()?.ResizeWidget(vm, 3, 2, desktopView?.CurrentMetrics);
            CloseContextMenu();
            desktopView?.InvalidateReorderPanel();
        }
    }

    private void OnSizeLargeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            var desktopView = FindParentDesktopView();
            int colSpan = vm.WidgetContent is DesktopWidgets.MediaWidget ? 5 : 4;
            FindParentDesktopViewModel()?.ResizeWidget(vm, colSpan, 2, desktopView?.CurrentMetrics);
            CloseContextMenu();
            desktopView?.InvalidateReorderPanel();
        }
    }

    private void OnSizeExtraLargeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            var desktopView = FindParentDesktopView();
            FindParentDesktopViewModel()?.ResizeWidget(vm, 5, vm.ExtraLargeRowSpan, desktopView?.CurrentMetrics);
            CloseContextMenu();
            desktopView?.InvalidateReorderPanel();
        }
    }

    private async void OnChoosePhotosClicked(object? sender, RoutedEventArgs e)
    {
        CloseContextMenu();
        if (DataContext is DesktopItemViewModel { WidgetContent: DesktopWidgets.ImageWidget imageWidget })
        {
            await imageWidget.PickImagesAsync();
        }
    }

    private void OnPropertiesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopItemViewModel vm)
        {
            var dialog = new DesktopItemPropertiesDialog(vm);
            if (TopLevel.GetTopLevel(this) is Window parentWindow)
            {
                dialog.Show(parentWindow);
            }
            else
            {
                dialog.Show();
            }
        }
    }
}
