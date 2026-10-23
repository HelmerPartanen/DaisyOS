using Avalonia.Controls;
using IOPath = global::System.IO.Path;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Screenshot;
using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Services.DesktopIntegration;

internal static class AdditionalPortals
{
    public static async Task<(uint Code, Dictionary<string, VariantValue> Results)> ShowAsync(string name, string method, List<string> args, Dictionary<string, VariantValue> options, CancellationToken stop)
    {
        var app = string.IsNullOrEmpty(args[1]) ? "This application" : args[1];
        if (name == "Background")
        {
            var dialog = PortalDialog.Create(() => new PortalDialog("Allow background activity?", $"{args[2]} wants to keep running in the background.", "Allow this time", "Deny"));
            using var cancel = stop.Register(() => Dispatcher.UIThread.Post(dialog.Close));
            var allowed = await dialog.Present();
            return (0, new() { ["result"] = allowed && !stop.IsCancellationRequested ? 2u : 0u });
        }
        if (name == "DynamicLauncher")
        {
            var type = options.TryGetValue("launcher_type", out var requested) ? requested.GetUInt32() : 1u;
            if (type is not (1 or 2)) return (2, []);
            var target = options.TryGetValue("target", out var url) ? url.GetString() : "";
            var dialog = PortalDialog.Create(() => new PortalDialog("Add application launcher?", $"{app} wants to add a launcher.\n{target}", "Add", "Deny"));
            var title = new TextBox { Text = args[3], IsReadOnly = options.TryGetValue("editable_name", out var editable) && !editable.GetBool() };
            dialog.Fields.Children.Add(title);
            using var cancel = stop.Register(() => Dispatcher.UIThread.Post(dialog.Close));
            if (!await dialog.Present() || stop.IsCancellationRequested || string.IsNullOrWhiteSpace(title.Text)) return (1, []);
            return (0, new() { ["name"] = title.Text, ["icon"] = VariantValue.Variant(options["daisy_icon"]) });
        }
        if (name == "Usb")
        {
            var devices = options["daisy_devices"];
            var dialog = PortalDialog.Create(() => new PortalDialog("Allow USB device access?", $"{args[2]} wants access to USB devices. Choose the devices to allow.", "Allow", "Deny"));
            var selected = new List<(VariantValue Device, CheckBox Check)>();
            for (var i = 0; i < devices.Count; i++)
            {
                var device = devices.GetItem(i);
                var id = device.GetItem(0).GetString();
                var info = device.GetItem(1).GetDictionary<string, VariantValue>();
                var access = device.GetItem(2).GetDictionary<string, VariantValue>();
                var writable = access.TryGetValue("writable", out var value) && value.GetBool();
                var check = new CheckBox { Content = id + (writable ? " · Read and write" : " · Read only") };
                check.IsCheckedChanged += (_, _) => dialog.Confirm.IsEnabled = selected.Any(item => item.Check.IsChecked == true);
                selected.Add((device, check)); dialog.Fields.Children.Add(check);
            }
            dialog.Confirm.IsEnabled = false;
            using var cancel = stop.Register(() => Dispatcher.UIThread.Post(dialog.Close));
            if (!await dialog.Present() || stop.IsCancellationRequested) return (1, []);
            var allowed = new Tmds.DBus.Protocol.Array<Struct<string, Dict<string, VariantValue>>>();
            foreach (var (device, _) in selected.Where(item => item.Check.IsChecked == true))
                allowed.Add(new Struct<string, Dict<string, VariantValue>>(device.GetItem(0).GetString(), new Dict<string, VariantValue>(device.GetItem(2).GetDictionary<string, VariantValue>())));
            return (0, new() { ["devices"] = allowed });
        }
        if (name == "Wallpaper")
        {
            if (options.TryGetValue("set-on", out var setOn) && setOn.GetString() == "lockscreen") return (2, []);
            if (!Uri.TryCreate(args[3], UriKind.Absolute, out var uri) || !uri.IsFile || !File.Exists(uri.LocalPath)) return (2, []);
            var dialog = PortalDialog.Create(() => new PortalDialog("Change wallpaper?", $"{app} wants to set {IOPath.GetFileName(uri.LocalPath)} as your wallpaper.", "Set wallpaper", "Deny"));
            using var cancel = stop.Register(() => Dispatcher.UIThread.Post(dialog.Close));
            if (!await dialog.Present() || stop.IsCancellationRequested) return (1, []);
            var response = await ApplicationPipe.RequestAsync(ApplicationPipe.Name("desktop-settings"), ["wallpaper", uri.LocalPath]);
            return (response == "ok" ? 0u : 2u, []);
        }
        if (name == "Account")
        {
            var dialog = PortalDialog.Create(() => new PortalDialog("Share account information?", $"{app} wants your account name and profile image.", "Share", "Deny"));
            using var cancel = stop.Register(() => Dispatcher.UIThread.Post(dialog.Close));
            if (!await dialog.Present() || stop.IsCancellationRequested) return (1, []);
            return (0, new() { ["id"] = Environment.UserName, ["name"] = Environment.UserName, ["image"] = "" });
        }
        if (name == "AppChooser")
        {
            var ids = options["daisy_choices"].GetArray<string>();
            if (ids.Length == 0) return (2, []);
            var dialog = PortalDialog.Create(() => new PortalDialog("Open with", "Choose an application to open this item.", "Open"));
            var combo = new ComboBox { ItemsSource = ids, SelectedIndex = 0 };
            dialog.Fields.Children.Add(combo);
            using var cancel = stop.Register(() => Dispatcher.UIThread.Post(dialog.Close));
            return await dialog.Present() && !stop.IsCancellationRequested ? (0, new() { ["choice"] = ids[combo.SelectedIndex] }) : (1, []);
        }
        if (name == "Screenshot")
        {
            var dialog = PortalDialog.Create(() => new PortalDialog(method == "PickColor" ? "Choose a color" : "Take a screenshot?", $"{app} wants a screenshot of your screen.", "Continue", "Deny"));
            var mode = new ComboBox { ItemsSource = new[] { "Current screen", "Active window" }, SelectedIndex = 0 };
            if (method != "PickColor") dialog.Fields.Children.Add(mode);
            using var cancel = stop.Register(() => Dispatcher.UIThread.Post(dialog.Close));
            if (!await dialog.Present() || stop.IsCancellationRequested) return (1, []);
            var helper = global::System.IO.Path.Combine(AppContext.BaseDirectory, "daisy-screenshot-helper");
            if (!File.Exists(helper)) helper = "/usr/local/libexec/daisyos/daisy-screenshot-helper";
            if (!File.Exists(helper)) helper = "/usr/libexec/daisyos/daisy-screenshot-helper";
            if (!File.Exists(helper) || (helper.StartsWith("/usr/local/") && !File.Exists(helper + ".api-v2"))) throw new IOException("Install the current DaisyOS screenshot helper.");
            var service = new DaisyScreenshotService(helper, global::System.IO.Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? global::System.IO.Path.GetTempPath(), "daisyos-portal-screenshots"), TimeSpan.FromSeconds(10));
            var capture = await service.CaptureWithoutFallbackAsync(new ScreenshotRequest(mode.SelectedIndex == 1 && method != "PickColor" ? ScreenshotCaptureMode.Window : ScreenshotCaptureMode.Screen), stop);
            if (!capture.Succeeded || capture.Path is null) throw new IOException(capture.Error);
            if (method != "PickColor") return (0, new() { ["uri"] = new Uri(capture.Path).AbsoluteUri });
            try { return await PickColorAsync(capture.Path, stop); }
            finally { File.Delete(capture.Path); }
        }
        throw new NotSupportedException();
    }

    private static async Task<(uint Code, Dictionary<string, VariantValue> Results)> PickColorAsync(string path, CancellationToken stop)
    {
        using var bitmap = new Bitmap(path);
        using var pixels = SkiaSharp.SKBitmap.Decode(path);
        var window = PortalDialog.Create(() => new Window { Title = "Choose a color", Width = 960, Height = 620, WindowStartupLocation = WindowStartupLocation.CenterScreen });
        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform };
        var content = new DockPanel();
        var cancel = new Button { Content = Loc.Get("Cancel"), Classes = { "Secondary" }, IsCancel = true, Margin = new Avalonia.Thickness(12) };
        DockPanel.SetDock(cancel, Dock.Bottom); content.Children.Add(cancel); content.Children.Add(image); window.Content = content;
        cancel.Click += (_, _) => window.Close();
        var completion = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>(TaskCreationOptions.RunContinuationsAsynchronously);
        image.PointerPressed += (_, e) =>
        {
            var p = e.GetPosition(image);
            var factor = Math.Min(image.Bounds.Width / pixels.Width, image.Bounds.Height / pixels.Height);
            var x = (int)((p.X - (image.Bounds.Width - pixels.Width * factor) / 2) / factor);
            var y = (int)((p.Y - (image.Bounds.Height - pixels.Height * factor) / 2) / factor);
            if (x < 0 || x >= pixels.Width || y < 0 || y >= pixels.Height) return;
            var color = pixels.GetPixel(x, y);
            completion.TrySetResult((0, new() { ["color"] = VariantValue.Struct(color.Red / 255d, color.Green / 255d, color.Blue / 255d) })); window.Close();
        };
        window.Closed += (_, _) => completion.TrySetResult((1, []));
        using var registration = stop.Register(() => Dispatcher.UIThread.Post(window.Close));
        window.Show(); return await completion.Task;
    }
}
