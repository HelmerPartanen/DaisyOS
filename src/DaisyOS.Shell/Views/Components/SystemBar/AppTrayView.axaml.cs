using System.Runtime.InteropServices;
using DaisyOS.Shell.ViewModels;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Tray;
using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Views.Components.SystemBar;

public partial class AppTrayView : UserControl
{
    private StatusNotifierService? _service;
    private string _signature = "";
    private int _generation;
    private readonly List<Bitmap> _images = [];
    public Control MaterialSurface => TrayMaterialFrame;
    public Control AnimationContent => this.FindControl<Control>("MotionContent")!;
    public AppTrayView() => InitializeComponent();

    public void Connect(StatusNotifierService service)
    {
        if (_service is not null) _service.ItemsChanged -= OnItemsChanged;
        _service = service;
        _service.ItemsChanged += OnItemsChanged;
        Refresh();
    }
    public void Disconnect()
    {
        if (_service is not null) _service.ItemsChanged -= OnItemsChanged;
        _service = null;
        _generation++;
        TrayItems.Children.Clear();
        foreach (var image in _images) image.Dispose();
        _images.Clear(); _signature = "";
    }
    private void OnItemsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);
    private void Refresh()
    {
        if (_service is null) return;
        var items = _service.Items;
        var signature = string.Join('|', items.Select(item => $"{item.Service}{item.Path}:{item.Id}:{item.Title}:{item.Status}:{item.IconName}:{item.IconThemePath}:{item.MenuPath}:{item.ItemIsMenu}:{IconKey(item.IconPixmap)}"));
        if (signature == _signature && TrayItems.Children.Count == items.Count) return;
        _signature = signature;
        var generation = ++_generation;
        TrayItems.Children.Clear();
        foreach (var image in _images) image.Dispose();
        _images.Clear();
        EmptyMessage.IsVisible = items.Count == 0;
        EmptyMessage.Text = _service.IsAvailable ? Loc.Get("No background apps") : Loc.Get("Background apps are unavailable");
        foreach (var item in items)
        {
            var button = DaisyOS.Shell.Services.Theming.TypographyPolicy.BindHeight(new Button { Width = 32, Height = 32, Padding = new Thickness(4), Margin = new Thickness(4), Classes = { "ShellButton" } });
            ToolTip.SetTip(button, item.Title);
            AutomationProperties.SetName(button, item.Title);
            button.Content = new DaisyIcon { Icon = UIIcons.Dock, Size = 24 };
            _ = LoadIconAsync(button, item, generation);
            button.Click += async (_, _) =>
            {
                try
                {
                    if (item.ItemIsMenu) await ShowMenuAsync(button, item);
                    else { var point = button.PointToScreen(new Point(0, button.Bounds.Height)); await _service!.ActivateAsync(item, point.X, point.Y); }
                }
                catch (Exception ex) { ShowFailure(item, ex); }
            };
            button.PointerPressed += async (_, args) =>
            {
                if (!args.GetCurrentPoint(button).Properties.IsRightButtonPressed) return;
                args.Handled = true;
                try { await ShowMenuAsync(button, item); }
                catch (Exception ex) { ShowFailure(item, ex); }
            };
            TrayItems.Children.Add(button);
        }
    }

    private async Task LoadIconAsync(Button button, TrayItem item, int generation)
    {
        Bitmap? image = null;
        try
        {
            image = await Task.Run(() => LoadIcon(item));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (image is null || _service is null || generation != _generation) return;
                _images.Add(image);
                button.Content = new Image { Source = image, Width = 24, Height = 24, Stretch = Stretch.Uniform };
                image = null;
            });
        }
        catch (Exception ex) { global::System.Console.Error.WriteLine($"[tray] Could not load {item.Title} icon: {ex.Message}"); }
        finally { image?.Dispose(); }
    }

    private async Task ShowMenuAsync(Button button, TrayItem item)
    {
        var service = _service;
        if (service is null) return;
        var entries = await service.GetMenuAsync(item);
        if (entries.Count == 0)
        {
            var point = button.PointToScreen(new Point(0, button.Bounds.Height));
            await service.ContextMenuAsync(item, point.X, point.Y);
            return;
        }
        var menu = new ContextMenu { ItemsSource = BuildMenu(entries, item, service).ToArray() };
        button.ContextMenu = menu;
        menu.Open(button);
    }
    private IEnumerable<Control> BuildMenu(IReadOnlyList<TrayMenuEntry> entries, TrayItem item, StatusNotifierService service)
    {
        foreach (var entry in entries.Where(entry => entry.Visible))
        {
            if (entry.Separator) { yield return new Separator(); continue; }
            var control = new MenuItem { Header = entry.Label, IsEnabled = entry.Enabled };
            if (entry.Children.Count > 0) control.ItemsSource = BuildMenu(entry.Children, item, service).ToArray();
            else control.Click += async (_, _) =>
            {
                try { await service.InvokeMenuAsync(item, entry.Id); }
                catch (Exception ex) { ShowFailure(item, ex); }
            };
            yield return control;
        }
    }
    private static void ShowFailure(TrayItem item, Exception error)
    {
        global::System.Console.Error.WriteLine($"[tray] {item.Title}: {error.Message}");
        (Application.Current as App)?.Feedback.Show(Loc.Format("Couldn’t open {0}. Try again.", item.Title));
    }

    internal static IEnumerable<string> IconCandidates(TrayItem item, Func<IEnumerable<string?>, string> resolveApplication, Func<string, string> resolveIcon)
    {
        if (item.Status != "NeedsAttention")
        {
            var application = resolveApplication(new[] { item.Id, item.Title });
            if (!string.IsNullOrWhiteSpace(application)) yield return application;
        }
        if (File.Exists(item.IconName)) yield return item.IconName;
        if (!string.IsNullOrWhiteSpace(item.IconThemePath) && Directory.Exists(item.IconThemePath))
            foreach (var extension in new[] { "", ".png", ".svg" })
            {
                var path = Path.Combine(item.IconThemePath, item.IconName + extension);
                if (File.Exists(path)) yield return path;
            }
        var themed = resolveIcon(item.IconName);
        if (!string.IsNullOrWhiteSpace(themed)) yield return themed;
    }

    private static Bitmap? LoadIcon(TrayItem item)
    {
        foreach (var path in IconCandidates(item, DesktopItemLoader.ResolveApplicationIconPath, DesktopItemLoader.ResolveIconPath).Distinct(StringComparer.Ordinal))
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var stream = File.OpenRead(path);
                var bitmap = Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase)
                    ? DesktopItemLoader.LoadSvgBitmap(stream, 48) : Bitmap.DecodeToWidth(stream, 48);
                if (bitmap is not null) return bitmap;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { }
        }
        try { return DecodePixmap(item.IconPixmap); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return null; }
    }

    private static string IconKey(VariantValue pixmaps)
    {
        if (pixmaps.Type != VariantValueType.Array) return "";
        try
        {
            return string.Join(',', Enumerable.Range(0, Math.Min(pixmaps.Count, 8)).Select(index =>
            {
                var data = pixmaps.GetItem(index).GetItem(2).GetArray<byte>();
                return data.Length <= 512 * 512 * 4
                    ? Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(data)) : "oversized";
            }));
        }
        catch (InvalidOperationException) { return "invalid"; }
        catch (ArgumentException) { return "invalid"; }
    }

    internal static WriteableBitmap? DecodePixmap(VariantValue pixmaps)
    {
        if (pixmaps.Type != VariantValueType.Array) return null;
        for (var index = 0; index < pixmaps.Count; index++)
        {
            var pixmap = pixmaps.GetItem(index);
            var width = pixmap.GetItem(0).GetInt32(); var height = pixmap.GetItem(1).GetInt32();
            if (width <= 0 || height <= 0 || width > 512 || height > 512) continue;
            var bytes = pixmap.GetItem(2).GetArray<byte>();
            if (bytes.Length != width * height * 4) continue;
            var bgra = new byte[bytes.Length];
            for (var i = 0; i < bytes.Length; i += 4)
            { bgra[i] = bytes[i + 3]; bgra[i + 1] = bytes[i + 2]; bgra[i + 2] = bytes[i + 1]; bgra[i + 3] = bytes[i]; }
            var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using var buffer = bitmap.Lock();
            for (var y = 0; y < height; y++) Marshal.Copy(bgra, y * width * 4, buffer.Address + y * buffer.RowBytes, width * 4);
            return bitmap;
        }
        return null;
    }
}
