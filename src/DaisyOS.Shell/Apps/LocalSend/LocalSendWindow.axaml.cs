using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.System.Networking.LocalSend;

namespace DaisyOS.Shell.Apps.LocalSend;

/// <summary>
/// A deliberately small, source-owned share sheet. In a native session this
/// control is hosted by a persistent layer-shell overlay, never an
/// xdg-toplevel: it therefore has no KWin title bar or taskbar presence.
/// </summary>
public partial class LocalSendWindow : UserControl
{
    private ILocalSendService? _service;
    private readonly List<string> _queuedPaths = [];
    private LocalSendDevice? _selectedDevice;
    private Bitmap? _previewBitmap;
    private int _previewRequestVersion;
    private bool _serviceUnavailable;
    private bool _isSending;

    public event EventHandler? DismissRequested;

    public LocalSendWindow()
    {
        InitializeComponent();
    }

    public bool IsOpen { get; private set; }

    public void Open(ILocalSendService? service, bool unavailable, IEnumerable<string>? paths = null)
    {
        AttachService(service, unavailable);
        QueueFiles(paths ?? []);
        IsOpen = true;
        IsVisible = true;
        _isSending = false;
        SendButton.Classes.Remove("Loading");
        SendButton.Content = Loc.Get("Send");
        _service?.RequestDiscoveryRefresh(forceSubnetProbe: true);
        Dispatcher.UIThread.Post(() => DevicesList.Focus(), DispatcherPriority.Input);
    }

    public void QueueFiles(IEnumerable<string> paths)
    {
        var files = paths.Where(File.Exists).Distinct(StringComparer.Ordinal).ToArray();
        if (files.Length > 0)
        {
            _queuedPaths.Clear();
            _queuedPaths.AddRange(files);
        }
        UpdateFilePreview();
        UpdateSendAction();
    }

    public void AttachService(ILocalSendService? service, bool unavailable = false)
    {
        if (ReferenceEquals(_service, service))
        {
            _serviceUnavailable = unavailable;
            RefreshDevices();
            if (IsOpen) _service?.RequestDiscoveryRefresh(forceSubnetProbe: true);
            return;
        }

        if (_service is not null) _service.DevicesChanged -= OnDevicesChanged;
        _service = service;
        _serviceUnavailable = unavailable;
        if (_service is not null) _service.DevicesChanged += OnDevicesChanged;
        RefreshDevices();
        if (IsOpen) _service?.RequestDiscoveryRefresh(forceSubnetProbe: true);
    }

    public void Dismiss()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        IsVisible = false;
        DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    public bool IsWithinShareDialog(Visual source) => IsWithin(source, ShareDialogCard);

    private void OnDevicesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshDevices);

    private void RefreshDevices()
    {
        if (_service is null)
        {
            DevicesList.ItemsSource = Array.Empty<LocalSendDevice>();
            NoDevicesText.Text = _serviceUnavailable
                ? Loc.Get("LocalSend is unavailable on this computer.")
                : Loc.Get("Starting LocalSend and looking for nearby devices…");
            NoDevicesPanel.IsVisible = true;
            ServiceStatusText.Text = _serviceUnavailable
                ? Loc.Get("The selected file is still safe. Close this sheet and try again once LocalSend is available.")
                : Loc.Get("Open LocalSend on the receiving device.");
            ServiceStatusText.IsVisible = !_serviceUnavailable;
            _selectedDevice = null;
            UpdateSendAction();
            return;
        }

        var devices = _service.GetDevices();
        DevicesList.ItemsSource = devices;
        NoDevicesPanel.IsVisible = devices.Count == 0;
        ServiceStatusText.IsVisible = devices.Count == 0;
        NoDevicesText.Text = Loc.Get("Looking for LocalSend devices on this network…");
        if (_selectedDevice is not null && devices.All(device => device.Id != _selectedDevice.Id))
        {
            _selectedDevice = null;
            DevicesList.SelectedItem = null;
        }
        if (_selectedDevice is null && devices.Count == 1)
        {
            _selectedDevice = devices[0];
            DevicesList.SelectedItem = _selectedDevice;
        }
        ServiceStatusText.Text = Loc.Get("Open LocalSend on the receiving device.");
        UpdateSendAction();
    }

    private async void UpdateFilePreview()
    {
        var requestVersion = ++_previewRequestVersion;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        FilePreviewImage.Source = null;
        FilePreviewImage.IsVisible = false;
        FilePreviewIcon.IsVisible = true;

        if (_queuedPaths.Count == 0)
        {
            QueuedFilesText.Text = Loc.Get("No file selected");
            FileDetailText.Text = Loc.Get("Choose Share from a file in Files");
            FilePreviewIcon.Icon = "file";
            return;
        }

        var path = _queuedPaths[0];
        QueuedFilesText.Text = _queuedPaths.Count == 1 ? Path.GetFileName(path) : Loc.Format("{0} files selected", _queuedPaths.Count);
        FileDetailText.Text = _queuedPaths.Count == 1
            ? Loc.Format("{0} · Ready to share", FormatSize(new FileInfo(path).Length))
            : Loc.Get("Ready to share");
        FilePreviewIcon.Icon = GetFileIcon(path);

        if (_queuedPaths.Count != 1 || !IsImage(path)) return;
        try
        {
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, 640);
            });
            if (requestVersion != _previewRequestVersion
                || _queuedPaths.Count != 1
                || !string.Equals(_queuedPaths[0], path, StringComparison.Ordinal))
            {
                bitmap.Dispose();
                return;
            }
            _previewBitmap = bitmap;
            FilePreviewImage.Source = _previewBitmap;
            FilePreviewImage.IsVisible = true;
            FilePreviewIcon.IsVisible = false;
        }
        catch
        {
            // A malformed preview must never stop the share flow; retain the
            // meaningful type icon instead.
        }
    }

    private void UpdateSendAction()
    {
        SendButton.IsEnabled = _service is not null && _selectedDevice is not null && _queuedPaths.Count > 0;
        SelectedDeviceText.Text = _selectedDevice is null ? string.Empty : _selectedDevice.Alias;
    }

    private void OnDeviceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedDevice = DevicesList.SelectedItem as LocalSendDevice;
        UpdateSendAction();
    }

    private async void OnSendClicked(object? sender, RoutedEventArgs e) => await SendSelectedAsync();

    private async Task SendSelectedAsync()
    {
        if (_isSending || _service is null || _selectedDevice is null || _queuedPaths.Count == 0) return;
        _isSending = true;
        SendButton.IsEnabled = false;
        SendButton.Classes.Add("Loading");
        SendButton.Content = Loc.Get("Sending…");
        try
        {
            await _service.SendAsync(_selectedDevice, _queuedPaths);
            (Application.Current as App)?.Feedback.Show(Loc.Get("LocalSend transfer completed."));
            Dismiss();
        }
        catch
        {
            (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t complete the LocalSend transfer."));
            SendButton.Content = Loc.Get("Send");
            UpdateSendAction();
        }
        finally
        {
            _isSending = false;
            SendButton.Classes.Remove("Loading");
        }
    }

    private async void OnDeviceDoubleTapped(object? sender, TappedEventArgs e) => await SendSelectedAsync();

    private async void OnDevicesKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
        e.Handled = true;
        await SendSelectedAsync();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return;
        e.Handled = true;
        Dismiss();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Dismiss();

    private async void OnChooseFilesClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        if (Application.Current is not App app) return;
        var files = await app.PickFilesAsync(topLevel, new DaisyOS.Shell.Apps.Files.Picker.DaisyFilePickerOptions(
            "Choose files to share", true, false, []));
        QueueFiles(files ?? []);
        if (_queuedPaths.Count > 0) DevicesList.Focus(NavigationMethod.Directional);
    }

    private void OnRefreshDevicesClicked(object? sender, RoutedEventArgs e)
    {
        NoDevicesText.Text = Loc.Get("Looking for LocalSend devices on this network…");
        _service?.RequestDiscoveryRefresh(forceSubnetProbe: true);
    }

    private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // The layer surface fills the output to act as a real modal boundary.
        // Only a press outside the visible card dismisses it; a press inside
        // continues normally to buttons and device rows.
        if (e.Source is Visual source && IsWithin(source, ShareDialogCard))
            return;

        e.Handled = true;
        Dismiss();
    }

    private static bool IsWithin(Visual source, Visual target) =>
        ReferenceEquals(source, target) || source.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, target));

    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif";

    private static string GetFileIcon(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" => "image",
        ".mp4" or ".mkv" or ".webm" => "film-strip",
        ".mp3" or ".flac" or ".wav" => "music-note",
        ".pdf" => "file-pdf",
        ".zip" or ".tar" or ".gz" or ".7z" => "file-zip",
        _ => "file"
    };

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }
}
