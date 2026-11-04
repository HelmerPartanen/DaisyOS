using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Views.Dialogs;

namespace DaisyOS.Shell.Apps.System.Disks;

public sealed partial class DiskManagerWindow : Window
{
    private readonly DiskService _service = new();
    private readonly ListBox _list;
    private readonly StackPanel _details;
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly WrapPanel _toolbar;
    private readonly Dictionary<string, Button> _actions = new();
    private IReadOnlyList<DiskItem> _items = [];
    private bool _busy;
    private readonly Dictionary<string, string> _health = new();
    private readonly SystemAppWindowChrome _fullscreen;

    public DiskManagerWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _list = this.FindControl<ListBox>("DeviceList")!;
        _details = this.FindControl<StackPanel>("DeviceDetails")!;
        _status = this.FindControl<TextBlock>("OperationStatus")!;
        _progress = this.FindControl<ProgressBar>("OperationProgress")!;
        _toolbar = this.FindControl<WrapPanel>("ActionToolbar")!;
        _fullscreen = new(this, this.FindControl<AppWindowTitleBar>("AppTitleBar")!,
            this.FindControl<Grid>("MainPaneLayout")!, "Auto,Auto,*,Auto", "0,Auto,*,Auto");
        foreach (var (action, icon) in new[] { ("Erase", "trash"), ("Partition", "hard-drive"), ("Mount", "folder-open"), ("First Aid", "shield-check"), ("Health", "gauge"), ("Restore / Clone", "copy"), ("Refresh", "arrows-clockwise") })
        {
            var variant = action switch { "Erase" => "Danger", "Mount" => "Primary", _ => "Secondary" };
            var button = new Button { Name = "Action" + action.Replace(" / ", "").Replace(" ", ""), Classes = { variant, "Small", "Flexible", "ToolbarAction" } };
            button.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new DaisyIcon { Icon = icon, Size = 16 }, new TextBlock { Text = action, VerticalAlignment = VerticalAlignment.Center } } };
            ToolTip.SetTip(button, action);
            button.Click += async (_, _) => await Action(action == "Mount" && _list.SelectedItem is DiskItem selected && DiskService.ResolveMountVolume(selected, _items)?.Mounted == true ? "Unmount" : action);
            _toolbar.Children.Add(button); _actions[action] = button;
        }
        _list.SelectionChanged += (_, _) => { ShowDetails(); this.FindControl<ScrollViewer>("DetailsScroll")!.Offset = new Vector(); };
        var menu = new ContextMenu();
        foreach (var action in _actions.Keys.Where(x => x != "Refresh").Concat(["Unmount"]))
        {
            var entry = new MenuItem { Header = action };
            entry.Click += async (_, _) => await Action(action); menu.Items.Add(entry);
        }
        menu.Opened += (_, _) =>
        {
            var selected = _list.SelectedItem as DiskItem;
            foreach (var entry in menu.Items.OfType<MenuItem>())
            {
                var action = entry.Header as string;
                entry.IsEnabled = !_busy && selected is not null && (action == "Unmount"
                    ? DiskService.ResolveMountVolume(selected, _items)?.Mounted == true : action == "Mount" ? DiskService.ResolveMountVolume(selected, _items) is { Mounted: false }
                    : action is not null && _actions[action].IsEnabled);
            }
        };
        _list.ContextMenu = menu;
        Opened += async (_, _) => await Action("Refresh");
        Closing += (_, e) => { if (_busy) { e.Cancel = true; _status.Text = "Wait for the current operation to finish before closing Disk Manager."; } };
        Closed += (_, _) => _fullscreen.Dispose();
    }
    private void OnDeviceClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_busy && sender is Button { DataContext: DiskItem item }) _list.SelectedItem = item;
    }
    private async Task Refresh()
    {
        var path = (_list.SelectedItem as DiskItem)?.Path;
        _items = await _service.Discover();
        _list.ItemsSource = _items;
        _list.SelectedItem = _items.FirstOrDefault(x => x.Path == path) ?? _items.FirstOrDefault();
        ShowDetails();
    }
    private void ShowDetails()
    {
        _details.Children.Clear();
        var item = _list.SelectedItem as DiskItem;
        foreach (var pair in _actions) pair.Value.IsEnabled = !_busy && (pair.Key == "Refresh" || item != null);
        if (item is null) { _details.Children.Add(new TextBlock { Text = "No disks found. Connect a disk and choose Refresh." }); return; }
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16 };
        var diskIcon = new DaisyIcon { Icon = item.Icon, Size = 36, VerticalAlignment = VerticalAlignment.Center };
        diskIcon.Bind(DaisyIcon.ForegroundProperty, this.GetResourceObservable("ContentSecondaryBrush"));
        heading.Children.Add(diskIcon);
        var title = new StackPanel { Spacing = 6, Children = { Label(item.Name, "TypeSizeHeading", weight: FontWeight.SemiBold), Label(item.Kind + " · " + item.Path, "TypeSizeBodySmall", true) } };
        Grid.SetColumn(title, 1); heading.Children.Add(title); _details.Children.Add(heading);
        var capacity = new StackPanel { Spacing = 12 };
        capacity.Children.Add(Label("Storage", "TypeSizeBody", weight: FontWeight.SemiBold));
        capacity.Children.Add(Label(DiskItem.Bytes(item.Size), "TypeSizeDisplay", weight: FontWeight.SemiBold));
        var summary = DiskStorageSummary.Create(item, _items);
        capacity.Children.Add(new StorageUsageView(summary));
        if (summary.Segments.Any(x => x.Label == "Usage unknown"))
            capacity.Children.Add(Label("Mount the unmounted volumes to see their used and free space.", "TypeSizeCaption", true));
        _details.Children.Add(Card(capacity, new Thickness(20)));
        var properties = new StackPanel();
        var detailRows = new List<(string Label, string Value)>();
        if (item.Type == "disk") detailRows.Add(("Partition layout", summary.PartitionLayout));
        if (item.Type != "disk" || item.Format.Length > 0) detailRows.Add(("Format", summary.Format));
        detailRows.AddRange([("Mount point", summary.MountPoints), ("Health", _health.GetValueOrDefault(item.Root, "Not checked")), ("Access", item.ReadOnly ? "Read-only" : "Read and write")]);
        foreach (var (label, value) in detailRows)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), ColumnSpacing = 20, Margin = new Thickness(20, 14) };
            row.Children.Add(Label(label, "TypeSizeBodySmall", true));
            var valueText = Label(value, "TypeSizeBodySmall"); Grid.SetColumn(valueText, 1); row.Children.Add(valueText);
            if (properties.Children.Count > 0) { var divider = new Border { Height = 1, Margin = new Thickness(20, 0) }; divider.Bind(Border.BackgroundProperty, this.GetResourceObservable("StrokeSubtleBrush")); properties.Children.Add(divider); }
            properties.Children.Add(row);
        }
        _details.Children.Add(new StackPanel { Spacing = 12, Children = { Label("Device details", "TypeSizeBody", weight: FontWeight.SemiBold), Card(properties, new Thickness(0)) } });
        if (summary.Volumes.Count > 0)
        {
            var volumeRows = new StackPanel { Spacing = 4 };
            foreach (var volume in summary.Volumes)
            {
                var text = new StackPanel { Spacing = 5, Children = { Label(volume.Name + " · " + DiskItem.Bytes(volume.Size), "TypeSizeBodySmall", weight: FontWeight.Medium), Label((volume.Format.Length > 0 ? DiskStorageSummary.FormatName(volume.Format) : "Unformatted") + " · " + (volume.Mounted ? string.Join(", ", volume.Mounts.Distinct()) : "Not mounted"), "TypeSizeCaption", true) } };
                var button = new Button { Tag = volume.Path, IsEnabled = !_busy, Content = text, Classes = { "SettingsNavItem" }, Height = double.NaN, MinHeight = 56, Padding = new Thickness(16, 12), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                button.Click += (_, _) => _list.SelectedItem = volume;
                volumeRows.Children.Add(button);
            }
            _details.Children.Add(new StackPanel { Spacing = 12, Children = { Label("Volumes", "TypeSizeBody", weight: FontWeight.SemiBold), Card(volumeRows, new Thickness(4)) } });
        }
        _details.Children.Add(Label(item.Type == "disk" ? "Erase resets this whole disk. Select a volume to format or repair only that volume." : "First Aid checks this volume for filesystem errors. Health checks the physical disk.", "TypeSizeBodySmall", true));
        var mountTarget = DiskService.ResolveMountVolume(item, _items);
        if (_actions["Mount"].Content is StackPanel mountContent && mountContent.Children[1] is TextBlock mountLabel) mountLabel.Text = mountTarget?.Mounted == true ? "Unmount" : "Mount";
        _actions["Partition"].IsEnabled &= item.Type == "disk";
        _actions["Erase"].IsEnabled &= item.Type is ("disk" or "part") && !item.ReadOnly;
        _actions["Mount"].IsEnabled &= mountTarget is not null;
        _actions["First Aid"].IsEnabled &= item.Format.Length > 0;
    }
    private TextBlock Label(string text, string size, bool secondary = false, FontWeight? weight = null)
    {
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeight.Regular };
        label.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable(size));
        label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(secondary ? "ContentSecondaryBrush" : "ContentPrimaryBrush"));
        return label;
    }
    private Border Card(Control child, Thickness padding)
    {
        var card = new Border { Child = child, Padding = padding };
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("SurfaceRaisedSubtleBrush"));
        card.Bind(Border.CornerRadiusProperty, this.GetResourceObservable("RoundedLG"));
        return card;
    }
    private async Task Action(string action)
    {
        if (_busy) return;
        _busy = true; ShowDetails();
        try
        {
            if (action == "Refresh") { await Refresh(); _status.Text = "Disk list refreshed. Select a device to continue."; return; }
            if (_list.SelectedItem is not DiskItem item) return;
            var operation = action; var option = ""; DiskItem? source = null;
            if (action == "Erase")
            {
                var formats = new ComboBox { ItemsSource = new[] { "FAT32 (USB drives and firmware)", "ext4 (Linux)" }, SelectedIndex = 0 };
                if (!await Options("Erase", $"All {(item.Type == "disk" ? "partitions and files" : "files")} on {item.Name} ({item.Path}) will be permanently lost. Mounted volumes will be unmounted. Choose the new format.", formats, "Erase", true)) return;
                option = formats.SelectedIndex == 0 ? "vfat" : "ext4";
            }
            else if (action == "Partition")
            {
                if (!await new ConfirmationDialog("Partition", "Replace all partitions?", $"All partitions and files on {item.Name} ({item.Path}) will be lost. A new GPT table and one full-size partition will be created. Choose Erase afterwards to format the new partition.", "Partition").ShowDialog<bool>(this)) return;
            }
            else if (action == "First Aid")
            {
                var mode = new ComboBox { ItemsSource = new[] { "Verify", "Repair" }, SelectedIndex = 0 };
                if (!await Options("First Aid", "Unmount the volume first. Verify checks for errors; Repair fixes errors that can be repaired safely.", mode, "Start")) return;
                operation = (string)mode.SelectedItem!;
            }
            else if (action == "Restore / Clone")
            {
                var choices = new ComboBox { ItemsSource = _items.Where(x => x.Root != item.Root && x.Size > 0 && x.Size <= item.Size).ToArray(), SelectedIndex = 0 };
                if (!await Options("Restore / Clone", $"Copy another device onto {item.Name} ({item.Path}). All files on this destination will be permanently lost. Both disks must be unmounted. Choose the source:", choices, "Clone", true)) return;
                source = choices.SelectedItem as DiskItem ?? throw new IOException("No source selected. Connect a suitable disk and refresh."); operation = "Clone";
            }
            _progress.IsVisible = true; _list.IsEnabled = false;
            _status.Text = $"{operation} in progress on {item.Path}. Complete administrator authentication if requested.";
            await _service.Operate(item, operation, option, source, message => Dispatcher.UIThread.Post(() => { _status.Text = message; if (operation == "Health") _health[item.Root] = message; }));
            await Refresh();
            if (operation == "Erase" && item.Type == "disk" && _list.SelectedItem is DiskItem refreshed &&
                DiskService.ResolveMountVolume(refreshed, _items) is { } volume)
                _list.SelectedItem = volume;
            _status.Text = operation == "Partition" ? "Success: partition created. Select the new partition and choose Erase to format it." : $"Success: {operation} completed on {item.Path}. You can select another action.";
        }
        catch (Exception ex) { _status.Text = "Could not complete the action. " + ex.Message; }
        finally { _busy = false; _progress.IsVisible = false; _list.IsEnabled = true; ShowDetails(); }
    }
    private async Task<bool> Options(string title, string message, Control field, string confirm, bool destructive = false)
    {
        var dialog = new Window { Title = title, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Bind(BackgroundProperty, this.GetResourceObservable("SurfaceBaseSolidBrush"));
        var cancel = new Button { Content = "Cancel", IsCancel = true, Classes = { "Secondary" } };
        var start = new Button { Content = confirm, Classes = { destructive ? "Danger" : "Primary" } };
        cancel.Click += (_, _) => dialog.Close(false); start.Click += (_, _) => dialog.Close(true);
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 16, Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, field, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, start } } } };
        return await dialog.ShowDialog<bool>(this);
    }
}
