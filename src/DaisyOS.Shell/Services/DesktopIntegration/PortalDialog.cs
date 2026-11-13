using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Wayland;
using Tmds.DBus.Protocol;
using DaisyOS.Shell.Services.Compositor;

namespace DaisyOS.Shell.Services.DesktopIntegration;

/// <summary>Shared DaisyOS surface for requests from external applications.</summary>
internal sealed class PortalDialog : Window
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public StackPanel Fields { get; } = new() { Spacing = 12 };
    public Button Confirm { get; }
    public bool Accepted { get; private set; }

    public PortalDialog(string title, string message, string confirm = "Allow", string cancel = "Cancel")
    {
        Title = title;
        Width = 480;
        MaxHeight = 720;
        SizeToContent = SizeToContent.Height;
        CanResize = CanMinimize = CanMaximize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.Bind(BackgroundProperty, this.GetResourceObservable("SurfaceBaseSolidBrush"));
        var deny = new Button { Content = Loc.Get(cancel), Classes = { "Secondary" }, IsCancel = true };
        Confirm = new Button { Content = Loc.Get(confirm), Classes = { "Primary" } };
        deny.Click += (_, _) => Close();
        Confirm.Click += (_, _) => { Accepted = true; Close(); };
        Fields.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        Fields.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var content = new StackPanel { Margin = new Thickness(24), Spacing = 20 };
        content.Children.Add(Fields);
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { deny, Confirm } });
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Opened += (_, _) => deny.Focus();
        Closed += (_, _) => _completion.TrySetResult(Accepted);
    }

    public Task<bool> Present()
    {
        Show();
        return _completion.Task;
    }

    public static T Create<T>(Func<T> factory, bool useWindowChrome = true) where T : Window
    {
        var window = useWindowChrome ? ClientSideDecorationsWindow.Create(factory) : factory();
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            WaylandWindow.SetAppId(window, "org.daisyos.DesktopServices");
        window.Opened += (_, _) => { if (window.ShowActivated) _ = FocusAsync(window); };
        return window;
    }

    private static async Task FocusAsync(Window window)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) return;
        try
        {
            using var bus = new DBusConnection(DBusAddress.Session ?? throw new IOException("No session bus."));
            await bus.ConnectAsync();
            foreach (var delay in new[] { 100, 250, 500 })
            {
                await Task.Delay(delay);
                if (!window.IsVisible) return;
                MessageBuffer call;
                using (var writer = bus.GetMessageWriter())
                {
                    writer.WriteMethodCallHeader(destination: KWinWindowBridge.BusName, path: KWinWindowBridge.ObjectPath, @interface: KWinWindowBridge.InterfaceName, member: "GetCaptureWindows");
                    call = writer.CreateMessage();
                }
                var json = await bus.CallMethodAsync(call, (reply, _) => reply.GetBodyReader().ReadString(), (object?)null).WaitAsync(TimeSpan.FromSeconds(2));
                if (!KWinWindowSnapshotParser.TryParse(json, out var windows)) continue;
                var target = windows.LastOrDefault(candidate => candidate.ProcessId == Environment.ProcessId && candidate.Title == window.Title);
                if (target is null) continue;
                if (window.IsVisible) await new KWinCommandDispatcher().DispatchAsync(target.Id, KWinWindowCommand.ActivateOrMinimize, CancellationToken.None);
                return;
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[portal] Could not request dialog focus: {ex.Message}"); }
    }
}
