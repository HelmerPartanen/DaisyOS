using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MimeKit;

namespace DaisyOS.Shell.Apps.Mail;

// The browser remains in a separate ephemeral process. Native controls own
// scrolling, focus and link activation, including keyboard/screen-reader links.
public sealed class MailHtmlView : Grid, IDisposable
{
    private readonly Viewbox _viewbox = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly DispatcherTimer _resize = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private CancellationTokenSource? _render;
    private Bitmap? _bitmap;
    private string? _document;
    private int _width;
    private int _version;
    public event Action<bool>? Rendered;
    public event Action<string>? LinkClicked;

    public MailHtmlView()
    {
        Children.Add(_viewbox);
        SizeChanged += (_, _) =>
        {
            if (_document is null || Math.Abs(RenderWidth() - _width) < 32) return;
            _resize.Stop(); _resize.Start();
        };
        _resize.Tick += (_, _) => { _resize.Stop(); _ = RenderAsync(); };
    }
    private int RenderWidth() => Math.Clamp((int)Math.Floor(Bounds.Width > 0 ? Bounds.Width : 680), 240, 1800);
    public void Show(MimeMessage message)
    {
        Clear();
        try { _document = MailHtmlDocument.Create(message); _ = RenderAsync(); }
        catch { Rendered?.Invoke(false); }
    }
    public void Clear()
    {
        _version++; _render?.Cancel(); _resize.Stop(); _document = null;
        _viewbox.Child = null; _bitmap?.Dispose(); _bitmap = null;
    }
    private async Task RenderAsync()
    {
        if (_document is null) return;
        _render?.Cancel();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(25)); _render = stop;
        var version = ++_version; _width = RenderWidth();
        var document = _document;
        var root = Path.Combine(Path.GetTempPath(), "daisy-mail-view-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var html = Path.Combine(root, "body.html"); var png = Path.Combine(root, "body.png"); var map = Path.Combine(root, "layout.json");
            await File.WriteAllTextAsync(html, document, stop.Token);
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "daisy-mail-renderer"))
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { html, png, map, _width.ToString(global::System.Globalization.CultureInfo.InvariantCulture), Math.Clamp((int)Math.Ceiling(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1), 1, 3).ToString(global::System.Globalization.CultureInfo.InvariantCulture) }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("HTML renderer unavailable.");
            // Provider HTML, URLs and helper diagnostics are never logged.
            var output = process.StandardOutput.ReadToEndAsync(stop.Token); var error = process.StandardError.ReadToEndAsync(stop.Token);
            try { await process.WaitForExitAsync(stop.Token); }
            catch { if (!process.HasExited) process.Kill(true); throw; }
            await output; await error;
            if (process.ExitCode != 0) throw new IOException("HTML rendering failed.");
            var layout = JsonSerializer.Deserialize(await File.ReadAllTextAsync(map, stop.Token), MailHtmlJsonContext.Default.MailHtmlLayout) ?? throw new IOException("Missing layout.");
            if (layout.Width is < 1 or > 4096 || layout.Height is < 1 or > 30000) throw new IOException("Invalid layout.");
            if (stop.IsCancellationRequested || version != _version) return;
            var bitmap = new Bitmap(png);
            var canvas = new Canvas { Width = layout.Width, Height = layout.Height, ClipToBounds = true, Background = Brushes.White };
            canvas.Children.Add(new Image { Source = bitmap, Width = layout.Width, Height = layout.Height, Stretch = Stretch.Fill });
            foreach (var link in layout.Links.Take(2000))
            {
                if (!(MailHtmlDocument.IsExternalLink(link.Uri) || link.Uri.StartsWith('#') && link.TargetY is >= 0) || !double.IsFinite(link.X + link.Y + link.Width + link.Height) || link.Width <= 0 || link.Height <= 0) continue;
                var button = new Button { Width = Math.Min(link.Width, layout.Width), Height = Math.Min(link.Height, layout.Height), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand) };
                // A dedicated template avoids painting system button chrome over sender artwork.
                var surface = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(3) };
                button.Template = new Avalonia.Controls.Templates.FuncControlTemplate<Button>((_, _) => surface);
                button.GotFocus += (_, _) =>
                {
                    surface.BorderBrush = this.TryFindResource("ActionPrimaryBrush", out var brush) && brush is IBrush accent ? accent : Brushes.DodgerBlue;
                    surface.BorderThickness = new Thickness(2);
                };
                button.LostFocus += (_, _) => surface.BorderThickness = new Thickness(0);
                AutomationProperties.SetName(button, string.IsNullOrWhiteSpace(link.Label) ? link.Uri : link.Label);
                ToolTip.SetTip(button, link.Uri);
                button.Click += (_, _) =>
                {
                    if (link.Uri.StartsWith('#') && link.TargetY is { } target)
                        this.BringIntoView(new Rect(0, target * Math.Min(1, Bounds.Width / layout.Width), 1, 24));
                    else LinkClicked?.Invoke(link.Uri);
                };
                Canvas.SetLeft(button, link.X); Canvas.SetTop(button, link.Y); canvas.Children.Add(button);
            }
            _viewbox.Child = canvas;
            var old = _bitmap; _bitmap = bitmap; old?.Dispose();
            Rendered?.Invoke(true);
        }
        catch (OperationCanceledException) { if (version == _version) Rendered?.Invoke(false); }
        catch { if (version == _version) Rendered?.Invoke(false); }
        finally
        {
            if (ReferenceEquals(_render, stop)) _render = null;
            try { Directory.Delete(root, true); } catch { }
        }
    }
    public void Dispose() => Clear();
}

internal sealed class MailHtmlLayout
{
    public double Width { get; set; }
    public double Height { get; set; }
    public List<MailHtmlLink> Links { get; set; } = [];
}
internal sealed class MailHtmlLink
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double? TargetY { get; set; }
    public string Uri { get; set; } = "";
    public string Label { get; set; } = "";
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MailHtmlLayout))]
internal partial class MailHtmlJsonContext : JsonSerializerContext;
