using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using DaisyOS.Core.Desktop;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.Services.Desktop;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Widgets;
using DaisyOS.System.Display;
using DaisyOS.System.Processes;
using Point = Avalonia.Point;

namespace DaisyOS.Shell.Views.Components.Desktop
{
    public partial class DesktopView : UserControl
    {
        public static readonly DirectProperty<DesktopView, DesktopGridMetrics?> CurrentMetricsProperty =
            AvaloniaProperty.RegisterDirect<DesktopView, DesktopGridMetrics?>(
                nameof(CurrentMetrics),
                o => o.CurrentMetrics);

        private DesktopGridMetrics? _currentMetrics;
        public DesktopGridMetrics? CurrentMetrics
        {
            get => _currentMetrics;
            private set => SetAndRaise(CurrentMetricsProperty, ref _currentMetrics, value);
        }

        private TrashStateObserver? _trashObserver;
        private readonly DesktopViewModel _viewModel;
        public DesktopViewModel ViewModel => _viewModel;

        private readonly AvaloniaMetricsProvider _metricsProvider;
        private readonly DesktopDragController _dragController;
        private App? _app;
        private bool _isLaunchingDesktopItem;

        /// <summary>
        /// Raised whenever a desktop or widget context menu changes its popup
        /// visual. The native layer-shell surface uses this to publish KWin's
        /// blur mask in the same frame.
        /// </summary>
        public event EventHandler? ContextMenuVisualChanged;
        public event EventHandler<bool>? WidgetContextMenuVisibilityChanged;
        private DesktopItemViewModel? _contextWidget;

        public bool IsWidgetContextMenuVisible => DesktopWidgetContextMenuHost.IsVisible;

        public Control DetachWidgetContextMenuForNativeOverlay()
        {
            if (DesktopWidgetContextMenuHost.Parent is Panel parent)
                parent.Children.Remove(DesktopWidgetContextMenuHost);
            return DesktopWidgetContextMenuHost;
        }

        public void ShowWidgetContextMenu(DesktopItemViewModel widget, Point position)
        {
            _contextWidget = widget;
            DesktopWidgetContextMenuHost.DataContext = widget;
            var x = Math.Clamp(position.X - 12, 6, Math.Max(6, Bounds.Width - 212));
            var y = Math.Clamp(position.Y - 42, 6, Math.Max(6, Bounds.Height - 160));
            DesktopWidgetContextMenuHost.Margin = new Thickness(x, y, 0, 0);
            DesktopWidgetContextMenuHost.IsVisible = true;
            WidgetContextMenuVisibilityChanged?.Invoke(this, true);
        }

        public void HideWidgetContextMenu()
        {
            if (!DesktopWidgetContextMenuHost.IsVisible) return;
            DesktopWidgetContextMenuHost.IsVisible = false;
            _contextWidget = null;
            WidgetContextMenuVisibilityChanged?.Invoke(this, false);
        }

        private void ResizeContextWidget(int columns, int rows)
        {
            if (_contextWidget is { } widget)
                _viewModel.ResizeWidget(widget, columns, rows, CurrentMetrics);
            HideWidgetContextMenu();
            InvalidateReorderPanel();
        }

        private void OnOverlaySizeSmallClicked(object? sender, RoutedEventArgs e) => ResizeContextWidget(1, 1);
        private void OnOverlaySizeMediumClicked(object? sender, RoutedEventArgs e) => ResizeContextWidget(3, 2);
        private void OnOverlaySizeLargeClicked(object? sender, RoutedEventArgs e) => ResizeContextWidget(_contextWidget?.IsMediaWidget == true ? 5 : 4, 2);
        private void OnOverlaySizeExtraLargeClicked(object? sender, RoutedEventArgs e) => ResizeContextWidget(5, _contextWidget?.ExtraLargeRowSpan ?? 4);
        private async void OnOverlayChoosePhotosClicked(object? sender, RoutedEventArgs e)
        {
            var widget = _contextWidget;
            HideWidgetContextMenu();
            if (widget?.WidgetContent is DesktopWidgets.ImageWidget imageWidget)
            {
                await imageWidget.PickImagesAsync();
            }
        }
        private void OnOverlayRemoveWidgetClicked(object? sender, RoutedEventArgs e)
        {
            if (_contextWidget is { } widget) _viewModel.RemoveWidget(widget, CurrentMetrics);
            HideWidgetContextMenu();
            InvalidateReorderPanel();
        }
        private void OnOverlayMoveToBottomShelfClicked(object? sender, RoutedEventArgs e)
        {
            if (_contextWidget is { IsMediaWidget: true } widget) _viewModel.MoveMediaWidgetToBottomShelf(widget);
            HideWidgetContextMenu();
            InvalidateReorderPanel();
        }

        public DesktopView()
        {
            InitializeComponent();

            _viewModel = new DesktopViewModel();
            Loc.RefreshOnChange(this, () =>
            {
                var trash = _viewModel.Items.FirstOrDefault(item => item.Id.Value == DesktopItemViewModel.TrashItemId);
                if (trash is not null) trash.Label = Loc.Get("Trash");
                foreach (var item in _viewModel.Items.Where(item => item.IsDesktopEntry))
                    item.Label = DesktopItemLoader.ParseDesktopFile(item.Id.Value).name;
            });
            _metricsProvider = new AvaloniaMetricsProvider();
            _dragController = new DesktopDragController();

            _viewModel.LayoutRequested += () =>
            {
                if (CurrentMetrics != null)
                {
                    _viewModel.CalculateLayout(CurrentMetrics);
                }
                InvalidateReorderPanel();
            };

            DataContext = _viewModel;

            SizeChanged += DesktopView_SizeChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble, handledEventsToo: true);

            if (InteractionSurface.ContextMenu is { } desktopMenu)
            {
                desktopMenu.Opened += (_, _) => NotifyContextMenuVisualChanged();
                desktopMenu.Closed += (_, _) => NotifyContextMenuVisualChanged();
            }
        }

        internal void NotifyContextMenuVisualChanged() =>
            ContextMenuVisualChanged?.Invoke(this, EventArgs.Empty);


        public void InvalidateReorderPanel()
        {
            var panel = this.FindDescendantOfType<DesktopReorderPanel>();
            panel?.InvalidateMeasure();
            panel?.InvalidateArrange();
            if (this.FindControl<WidgetSnapOutline>("WidgetSnapOutline") is { } outline)
                outline.TargetBounds = panel?.WidgetSnapBounds;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var sourceVisual = e.Source as Visual;
            // Let the rename editor own caret placement and text selection.
            if (sourceVisual is TextBox || sourceVisual?.FindAncestorOfType<TextBox>() is not null)
                return;
            if (sourceVisual?.FindAncestorOfType<Button>() is { Name: "RemoveWidgetButton" })
            {
                return;
            }

            var itemView = sourceVisual?.FindAncestorOfType<DesktopItemView>();
            var itemVm = itemView?.DataContext as DesktopItemViewModel;

            var surface = (Control?)this.FindControl<Grid>("InteractionSurface") ?? this;
            Point posInSurface = e.GetPosition(surface);
            Point posInItem = itemView != null ? e.GetPosition(itemView) : new Point(0, 0);
            Size itemSize = itemView != null ? itemView.Bounds.Size : new Size(74, 88);
            var isPrimaryPress = e.GetCurrentPoint(surface).Properties.IsLeftButtonPressed;

            if (!isPrimaryPress)
            {
                return;
            }

            CloseAnyOpenContextMenu();

            if (itemVm != null)

            {
                _viewModel.SelectItem(itemVm);

                // Use the platform's click count directly. Routed DoubleTapped
                // is gesture recognition and can be retargeted by pointer capture
                // while a desktop item is preparing to drag. Native file managers
                // instead make the second primary press the activation boundary.
                if (e.ClickCount == 2)
                {
                    e.Handled = true;
                    _ = ActivateDesktopItemAsync(itemVm, Avalonia.Wayland.WaylandActivation.RequestAsync(e, Path.GetFileNameWithoutExtension(itemVm.State.Id)));
                    return;
                }
            }
            else
            {
                _viewModel.ClearSelection();
            }

            _dragController.OnPointerPressed(
                itemVm,
                posInSurface,
                posInItem,
                itemSize,
                _viewModel,
                CurrentMetrics,
                InvalidateReorderPanel);
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            var surface = (Control?)this.FindControl<Grid>("InteractionSurface") ?? this;
            Point posInSurface = e.GetPosition(surface);
            var wasDragging = _dragController.State == DragState.Reordering;

            _dragController.OnPointerMoved(
                posInSurface,
                _viewModel,
                CurrentMetrics,
                InvalidateReorderPanel);

            if (!wasDragging && _dragController.State == DragState.Reordering)
            {
                // Do not capture the initial click. Avalonia resolves a double
                // click against the captured element, and an early capture made
                // the desktop surface the target instead of the desktop icon.
                // Capture only after a deliberate drag has begun.
                e.Pointer.Capture(surface);
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _dragController.OnPointerReleased(
                _viewModel,
                CurrentMetrics,
                InvalidateReorderPanel);
            e.Pointer.Capture(null);
        }

        private async Task ActivateDesktopItemAsync(DesktopItemViewModel item, Task<string>? activation = null)
        {
            if (_isLaunchingDesktopItem)
            {
                return;
            }

            _isLaunchingDesktopItem = true;
            try
            {
                // Activation is the acknowledgement. Do not leave an item looking
                // selected after its double-click has been accepted.
                _viewModel.ClearSelection();
                if (item.State.Id.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) && Application.Current is App shell)
                {
                    await shell.LaunchDesktopApplicationAsync(item.State.Id, activation);
                    return;
                }
                if (item.Id.Value == DesktopItemViewModel.TrashItemId && Application.Current is App desktopShell)
                {
                    await desktopShell.ShowTrashAsync();
                    return;
                }
                using var busyCursor = DaisyOS.System.Processes.BusyCursor.Begin();
                var result = item.Id.Value == DesktopItemViewModel.TrashItemId
                    ? await DaisyOS.Shell.Services.PackagedApplicationLauncher.LaunchFilesAsync(new FileManagerActivationRequest([DaisyOS.Shell.Apps.Files.UserTrashPaths.Files], false))
                    : item.State.Id.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
                        ? await new LinuxAppLauncherService().LaunchDesktopEntryFileAsync(item.State.Id)
                        : await Task.Run(() => new SafeProcessLauncher().Launch("xdg-open", [item.State.Id]));
                if (!result.Succeeded)
                {
                    (Application.Current as App)?.Feedback.Show(Loc.Format("Couldn’t open {0}.", item.Label));
                }
            }
            catch (Exception)
            {
                (Application.Current as App)?.Feedback.Show(Loc.Format("Couldn’t open {0}.", item.Label));
            }
            finally
            {
                _isLaunchingDesktopItem = false;
            }
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            _trashObserver?.Dispose();
            _trashObserver = new TrashStateObserver(DaisyOS.Shell.Apps.Files.UserTrashPaths.Files,
                populated => _viewModel.UpdateTrashIcon(populated, ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light));
            _app = Application.Current as App;
            if (_app is not null)
            {
            }

            if (TopLevel.GetTopLevel(this) is Window window)
            {
                window.Deactivated += OnWindowDeactivated;
            }

            var widgetLibrary = this.FindControl<WidgetLibraryOverlay>("WidgetLibrary");
            if (widgetLibrary != null)
            {
                widgetLibrary.WidgetDragRequested += OnWidgetDragRequested;
            }

            ActualThemeVariantChanged += OnThemeVariantChanged;
            _viewModel.UpdateTheme(ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light);
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            _trashObserver?.Dispose();
            _trashObserver = null;
            ActualThemeVariantChanged -= OnThemeVariantChanged;
            var widgetLibrary = this.FindControl<WidgetLibraryOverlay>("WidgetLibrary");
            if (widgetLibrary != null)
            {
                widgetLibrary.WidgetDragRequested -= OnWidgetDragRequested;
            }

            if (_app is not null)
            {
                _app = null;
            }

            if (TopLevel.GetTopLevel(this) is Window window)
            {
                window.Deactivated -= OnWindowDeactivated;
            }

        }

        private void OnThemeVariantChanged(object? sender, EventArgs e)
        {
            _viewModel.UpdateTheme(ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light);
        }

        internal bool IsNativeContextMenuOpen { get; set; }
        private void OnWindowDeactivated(object? sender, EventArgs e) { if (!IsNativeContextMenuOpen) CloseAnyOpenContextMenu(); }
        private void OnLostFocus(object? sender, RoutedEventArgs e) { if (!IsNativeContextMenuOpen) CloseAnyOpenContextMenu(); }

        public void CloseAnyOpenContextMenu()
        {
            var surface = this.FindControl<Grid>("InteractionSurface");
            if (surface?.ContextMenu is { IsOpen: true } surfaceMenu)
            {
                surfaceMenu.Close();
            }

            foreach (var item in this.GetVisualDescendants().OfType<DesktopItemView>())
            {
                item.CloseContextMenu();
            }
        }


        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            _dragController.OnPointerCaptureLost(
                _viewModel,
                InvalidateReorderPanel);
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            if (e.DataTransfer.Contains(DataFormat.File))
            {
                var surface = (Control?)this.FindControl<Grid>("InteractionSurface") ?? this;
                e.DragEffects = _viewModel.GetTrashAtPoint(e.GetPosition(surface), CurrentMetrics) != null
                    ? DragDropEffects.Move : DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private async void OnDrop(object? sender, DragEventArgs e)
        {
            var droppedItems = e.DataTransfer.TryGetFiles();
            if (droppedItems is not { Length: > 0 })
            {
                return;
            }

            var surface = (Control?)this.FindControl<Grid>("InteractionSurface") ?? this;
            if (_viewModel.GetTrashAtPoint(e.GetPosition(surface), CurrentMetrics) != null)
            {
                e.Handled = true;
                var succeeded = false;
                foreach (var storageItem in droppedItems)
                {
                    var path = storageItem.TryGetLocalPath();
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    succeeded |= await FileSystemService.MoveToTrashAsync(path);
                }
                _viewModel.RefreshTrashIcon();
                e.DragEffects = succeeded ? DragDropEffects.Move : DragDropEffects.None;
                return;
            }

            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrWhiteSpace(desktopPath))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(desktopPath);
                var copiedCount = 0;
                foreach (var storageItem in droppedItems)
                {
                    var sourcePath = storageItem.TryGetLocalPath();
                    if (string.IsNullOrWhiteSpace(sourcePath) ||
                        !File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                    {
                        continue;
                    }

                    var destinationPath = GetAvailableDesktopPath(desktopPath, Path.GetFileName(sourcePath));
                    if (await FileSystemService.CopyAsync(sourcePath, destinationPath,
                            _ => Task.FromResult(ConflictResolution.Skip)))
                    {
                        _viewModel.AddDesktopPath(destinationPath);
                        copiedCount++;
                    }
                }

                if (copiedCount > 0)
                {
                    if (CurrentMetrics is not null)
                    {
                        _viewModel.CalculateLayout(CurrentMetrics);
                    }
                    InvalidateReorderPanel();
                    (Application.Current as App)?.Feedback.Show(copiedCount == 1
                        ? Loc.Get("Item added to Desktop.")
                        : Loc.Format("{0} items added to Desktop.", copiedCount));
                    e.DragEffects = DragDropEffects.Copy;
                    e.Handled = true;
                }
            }
            catch
            {
                (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t add that item to Desktop."));
            }
        }

        private static string GetAvailableDesktopPath(string desktopPath, string fileName)
        {
            var candidate = Path.Combine(desktopPath, fileName);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }

            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            for (var suffix = 2; ; suffix++)
            {
                candidate = Path.Combine(desktopPath, $"{stem} {suffix}{extension}");
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            // Desktop shortcuts are registered with handledEventsToo so they still
            // work after ordinary controls process a key. Never reinterpret text
            // editor input (notably Space) as Quick Look or another desktop action.
            if (e.Source is TextBox ||
                e.Source is Visual source && source.GetVisualAncestors().Any(visual => visual is TextBox) ||
                _viewModel.Items.Any(item => item.IsRenaming))
            {
                return;
            }

            if (e.Key == Key.Space)
            {
                var selected = _viewModel.Items.FirstOrDefault(i => i.IsSelected);
                if (selected is not null && !string.IsNullOrEmpty(selected.State.Id))
                {
                    DaisyOS.Shell.Views.Components.QuickLook.QuickLookWindow.ShowOrToggle(selected.State.Id);
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Escape)
            {
                var widgetLibrary = this.FindControl<WidgetLibraryOverlay>("WidgetLibrary");
                if (widgetLibrary?.IsOpen == true)
                {
                    widgetLibrary.Dismiss();
                    e.Handled = true;
                    return;
                }

                _viewModel.IsEditMode = false;
                _dragController.CancelDrag(_viewModel, InvalidateReorderPanel);
            }
        }

        private void OnWidgetDragRequested(string widgetType, int colSpan, int rowSpan, Point posInSurface, Point grabOffset, Size cardSize)
        {
            var widgetLibrary = this.FindControl<WidgetLibraryOverlay>("WidgetLibrary");
            widgetLibrary?.Dismiss();

            var item = _viewModel.AddNewWidget(widgetType, colSpan, rowSpan, CurrentMetrics);

            double cellWidth = CurrentMetrics?.CellWidth ?? 74;
            double cellHeight = CurrentMetrics?.CellHeight ?? 88;
            Size itemSize = new Size(cellWidth * colSpan, cellHeight * rowSpan);
            if (cardSize.Width > 0 && cardSize.Height > 0)
                grabOffset = new Point(grabOffset.X * itemSize.Width / cardSize.Width,
                    grabOffset.Y * itemSize.Height / cardSize.Height);

            _dragController.StartDirectDrag(
                item,
                posInSurface,
                grabOffset,
                itemSize,
                _viewModel,
                CurrentMetrics,
                InvalidateReorderPanel);
        }

        private void DesktopView_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            RebuildGrid();
        }

        private void RebuildGrid()
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

            _dragController.CancelDrag(_viewModel, InvalidateReorderPanel);
            var panel = this.FindDescendantOfType<DesktopReorderPanel>();
            panel?.SuppressFlipOnce();

            // Keep desktop cells outside the complete 64px bottom safe area:
            // 6px top gap + 52px chrome + 6px bottom gap. DesktopGridMetrics
            // then applies its own 6px inset on every edge of this work area.
            const double bottomSectionHeight = 64.0;
            var usableHeight = Math.Max(0, Bounds.Height - bottomSectionHeight);
            var workArea = new DaisyOS.Core.Desktop.Rect(0, 0, Bounds.Width, usableHeight);
            // Bounds and pointer coordinates are already Avalonia logical units.
            // Keep the physical DPI as metadata, but request logical cell sizes;
            // applying RenderScaling to both made icons and widgets enormous.
            var dpi = (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0) * 96.0;

            double cellW = _metricsProvider.GetCellWidth(dpi);
            double cellH = _metricsProvider.GetCellHeight(dpi);

            CurrentMetrics = new DesktopGridMetrics("primary", workArea, dpi, cellW, cellH, edgeInsetX: 6, edgeInsetY: 6);
            _viewModel.CalculateLayout(CurrentMetrics);
            InvalidateReorderPanel();
        }

        public void ActivateItem(DesktopItemViewModel item)
        {
            _ = ActivateDesktopItemAsync(item);
        }


        // ============================================================
        // EMPTY DESKTOP CONTEXT MENU EVENT HANDLERS
        // ============================================================

        private void OnNewFolderClicked(object? sender, RoutedEventArgs e)
        {
            _viewModel.CreateNewFolder(CurrentMetrics);
            InvalidateReorderPanel();
        }

        private void OnNewTextDocumentClicked(object? sender, RoutedEventArgs e)
        {
            _viewModel.CreateNewTextFile(CurrentMetrics);
            InvalidateReorderPanel();
        }

        private void OnAddWidgetsClicked(object? sender, RoutedEventArgs e)
        {
            var widgetLibrary = this.FindControl<WidgetLibraryOverlay>("WidgetLibrary");
            widgetLibrary?.Open();
        }

        private void OnPasteClicked(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            _ = _viewModel.PasteFromClipboardAsync(topLevel?.Clipboard, CurrentMetrics);
            InvalidateReorderPanel();
        }

        private void OnSortByNameClicked(object? sender, RoutedEventArgs e)
        {
            _viewModel.SortItemsBy(DesktopSortMode.Name, CurrentMetrics);
            InvalidateReorderPanel();
        }

        private void OnSortByDateClicked(object? sender, RoutedEventArgs e)
        {
            _viewModel.SortItemsBy(DesktopSortMode.DateModified, CurrentMetrics);
            InvalidateReorderPanel();
        }

        private void OnSortBySizeClicked(object? sender, RoutedEventArgs e)
        {
            _viewModel.SortItemsBy(DesktopSortMode.Size, CurrentMetrics);
            InvalidateReorderPanel();
        }

        private void OnSortByTypeClicked(object? sender, RoutedEventArgs e)
        {
            _viewModel.SortItemsBy(DesktopSortMode.Type, CurrentMetrics);
            InvalidateReorderPanel();
        }

        private void OnAutoArrangeClicked(object? sender, RoutedEventArgs e)
        {
            if (CurrentMetrics != null)
            {
                _viewModel.CalculateLayout(CurrentMetrics, forceAutoArrange: true);
                _viewModel.SaveLayout();
                InvalidateReorderPanel();
            }
        }

        private void OnChangeWallpaperClicked(object? sender, RoutedEventArgs e)
        {
            _viewModel.OpenSettings("desktop");
        }

        private void OnOpenInFilesClicked(object? sender, RoutedEventArgs e)
        {
            _viewModel.OpenInFiles();
        }

        private void OnOpenTerminalClicked(object? sender, RoutedEventArgs e)
        {
            _viewModel.OpenInTerminal();
        }

        private void OnDesktopSettingsClicked(object? sender, RoutedEventArgs e)
        {
            _viewModel.OpenSettings("desktop");
        }
    }
}
