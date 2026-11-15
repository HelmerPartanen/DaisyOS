using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Views.Dialogs;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

public partial class TaskManagerView : UserControl
{
    private TaskManagerViewModel? _model;
    private bool _deviceSyncScheduled, _syncingDevice;
    private bool? _lastWide, _lastOpen;
    private double _lastPanelMaxWidth = -1;
    public TaskManagerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null) _model.PropertyChanged -= OnModelChanged;
            _model = DataContext as TaskManagerViewModel;
            if (_model is not null) _model.PropertyChanged += OnModelChanged;
            UpdateDetailsLayout();
        };
        SizeChanged += (_, _) => UpdateDetailsLayout();
        LayoutUpdated += (_, _) => SynchronizeTableHeader();
        KeyDown += (_, e) => { if (e.Key == Key.Escape && _model?.DetailsOpen == true) { _model.CloseDetails(); e.Handled = true; } };

    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => UpdateDetailsLayout();
    private void UpdateDetailsLayout()
    {
        var host = this.FindControl<Grid>("ListAndDetails");
        var panel = this.FindControl<Border>("DetailsPanel");
        SynchronizeDeviceSelection();
        if (host is null || panel is null) return;
        var wide = Bounds.Width >= 1200;
        var open = _model?.DetailsOpen == true;
        var maxWidth = Math.Max(0, Bounds.Width - 250);
        if (_lastWide == wide && _lastOpen == open && _lastPanelMaxWidth == maxWidth) return;
        _lastWide = wide; _lastOpen = open; _lastPanelMaxWidth = maxWidth;
        host.ColumnDefinitions = new(open && wide ? "*,316" : "*,0");
        Grid.SetColumn(panel, wide ? 1 : 0);
        panel.HorizontalAlignment = HorizontalAlignment.Right;
        panel.Width = 300;
        panel.MaxWidth = Math.Max(0, Bounds.Width - 250);
        panel.Margin = wide ? new Thickness(16, 0, 0, 0) : new Thickness(0);
    }
    private void SynchronizeTableHeader()
    {
        if (_model?.IsList != true) return;
        var processes = _model.IsProcesses;
        var list = this.FindControl<ListBox>(processes ? "ProcessList" : "ApplicationsList");
        var header = this.FindControl<Grid>(processes ? "ProcessHeader" : "ApplicationHeader");
        var row = list?.GetVisualDescendants().OfType<Grid>().FirstOrDefault(grid => grid.Classes.Contains(processes ? "ProcessDataRow" : "ApplicationDataRow"));
        if (header is null || row is null || row.Bounds.Width <= 0) return;
        // Scrollbar allocation differs between empty and scrolling lists. Follow
        // the actual row viewport rather than guessing a fixed scrollbar gutter.
        if (Math.Abs(header.MaxWidth - row.Bounds.Width) > .1) header.MaxWidth = row.Bounds.Width;
    }
    private void SynchronizeDeviceSelection()
    {
        if (_deviceSyncScheduled) return;
        _deviceSyncScheduled = true;
        // Replacing ItemsSource clears ComboBox selection even when the model's
        // index is unchanged. Restore it after the source binding has settled.
        Dispatcher.UIThread.Post(() =>
        {
            _deviceSyncScheduled = false;
            if (_model is not { HasDeviceSelector: true } model || this.FindControl<ComboBox>("DeviceSelector") is not { } selector) return;
            _syncingDevice = true;
            try { selector.SelectedIndex = model.DeviceIndex; }
            finally { _syncingDevice = false; }
        }, DispatcherPriority.Loaded);
    }
    private void OnDeviceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_syncingDevice && sender is ComboBox { SelectedIndex: >= 0 } selector && _model is { } model)
            model.DeviceIndex = selector.SelectedIndex;
    }
    private void OnNavigate(object? sender, RoutedEventArgs e) { if (_model is { } vm && sender is Button { Tag: string tag } && int.TryParse(tag, out var section)) vm.Navigate(section); }
    private void OnResource(object? sender, RoutedEventArgs e) { if (_model is { } vm && sender is Button { Tag: string tag } && int.TryParse(tag, out var resource)) vm.OpenResource(resource); }
    private void OnToggleCores(object? sender, RoutedEventArgs e) => _model?.ToggleCores();
    private void OnPause(object? sender, RoutedEventArgs e) => _model?.TogglePause();
    private async void OnRefresh(object? sender, RoutedEventArgs e) { if (_model is { } vm) await vm.RefreshAsync(); }
    private void OnSort(object? sender, RoutedEventArgs e) { if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index)) _model?.SortBy(index); }
    private void OnToggleApplication(object? sender, RoutedEventArgs e) { if (sender is Button { DataContext: ApplicationRowViewModel row }) row.IsExpanded = !row.IsExpanded; }
    private void OnToggleBranch(object? sender, RoutedEventArgs e) { if (sender is Button { DataContext: ProcessRowViewModel row }) _model?.ToggleBranch(row); }
    private void OnTopUser(object? sender, RoutedEventArgs e) { if (sender is Button { DataContext: TopUsageRow row }) _model?.InspectTop(row); }
    private void OnViewAll(object? sender, RoutedEventArgs e) => _model?.Navigate(_model.TopUsers.Any(r => r.AppKey is not null) ? 1 : 2);
    private void OnInspectProcess(object? sender, RoutedEventArgs e) { if (sender is Button { DataContext: ProcessRowViewModel row }) _model?.InspectProcess(row.Pid, row.StartTimeTicks); }
    private void OnContributor(object? sender, RoutedEventArgs e) { if (sender is Button { DataContext: ResourceContributor row }) _model?.InspectProcess(row.Pid, row.StartTimeTicks); }
    private void OnOpenDetails(object? sender, RoutedEventArgs e) => _model?.OpenDetails();
    private void OnListKeyDown(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter) { _model?.OpenDetails(); e.Handled = true; } }
    private void OnCloseDetails(object? sender, RoutedEventArgs e) => _model?.CloseDetails();
    private void OnDiskManager(object? sender, RoutedEventArgs e) => (Application.Current as App)?.ShowDiskManager();
    private async void OnShowWindow(object? sender, RoutedEventArgs e) { if (_model is { } vm && sender is Button { DataContext: WindowRowViewModel row }) await vm.ShowWindowAsync(row); }
    private async void OnCloseWindow(object? sender, RoutedEventArgs e) { if (_model is { } vm && sender is Button { DataContext: WindowRowViewModel row }) await vm.CloseWindowAsync(row); }
    private async void OnCloseApp(object? sender, RoutedEventArgs e) { if (_model is { } vm) await vm.CloseApplicationAsync(); }
    private async void OnEndSelectedProcessClicked(object? sender, RoutedEventArgs e) => await EndSelectedAsync(false);
    private async void OnForceQuit(object? sender, RoutedEventArgs e) => await EndSelectedAsync(true);
    private async Task EndSelectedAsync(bool force)
    {
        if (_model is not { } vm || vm.SelectedProcess is not { } process || !vm.CanEnd || TopLevel.GetTopLevel(this) is not Window owner) return;
        var dialog = new ConfirmationDialog(force ? "Force quit process" : "End process", $"{(force ? "Force quit" : "End")} {process.Name}?",
            $"PID {process.Pid}. {(force ? "The process will stop immediately." : "A termination request will be sent.")} Unsaved work may be lost.", force ? "Force quit" : "End process", "Cancel");
        if (await dialog.ShowDialog<bool>(owner)) await vm.EndProcessAsync(process, force);
    }
}
