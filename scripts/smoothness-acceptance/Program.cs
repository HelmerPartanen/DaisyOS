using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Wayland;
using DaisyOS.Core.Models;
using DaisyOS.Shell;
using DaisyOS.Shell.Apps.Notes;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Services.Diagnostics;
using DaisyOS.Shell.Services.Motion;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;
using DaisyOS.Shell.Views.Components.SystemBar;
using DaisyOS.Shell.Views.Components.Notifications;

if (args.Length != 1) throw new ArgumentException("Usage: smoothness-acceptance <output-directory>");
AcceptanceApp.Output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(AcceptanceApp.Output);
foreach (var name in new[] { "callbacks.jsonl", "acceptance.json", "failure.txt" })
    File.Delete(Path.Combine(AcceptanceApp.Output, name));
using var fixture = new Fixture();
AcceptanceApp.Files = Path.Combine(fixture.Path, "files");
Directory.CreateDirectory(AcceptanceApp.Files);
for (var i = 0; i < 300; i++) File.WriteAllText(Path.Combine(AcceptanceApp.Files, $"fixture-{i:D4}.txt"), "Fixture");
Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", fixture.Path);
Environment.SetEnvironmentVariable("DAISYOS_RESPONSIVENESS_TRACE", Path.Combine(AcceptanceApp.Output, "callbacks.jsonl"));
Environment.SetEnvironmentVariable("DAISYOS_RESPONSIVENESS_SECONDS", "120");
DaisyOS.Shell.Services.Theming.LinuxTextLibraryIsolation.EnsureLoaded();
if (Environment.GetEnvironmentVariable("DAISYOS_ACCEPTANCE_FRAME_TRACE") == "1" ||
    Environment.GetEnvironmentVariable("DAISYOS_ACCEPTANCE_REQUIRE_FRAMES") == "1")
    Avalonia.Logging.Logger.Sink = new FrameTrace();
var platformOptions = new WaylandPlatformOptions { UseDmabufSwapchain = Environment.GetEnvironmentVariable("DAISYOS_ACCEPTANCE_DMABUF") == "1" };
if (Environment.GetEnvironmentVariable("DAISYOS_ACCEPTANCE_SOFTWARE") == "1") platformOptions.GlProfiles.Clear();
AppBuilder.Configure<AcceptanceApp>().UseSkia().UseHarfBuzz()
    .With(new FontManagerOptions { DefaultFamilyName = "Noto Sans" })
    .With(platformOptions).UseWayland()
    .StartWithClassicDesktopLifetime([]);
return AcceptanceApp.ExitCode;

public sealed class AcceptanceApp : Application
{
    public static string Output = "";
    public static string Files = "";
    public static int ExitCode;
    public override void Initialize()
    {
        var shell = new App();
        shell.Initialize();
        var resources = shell.Resources;
        shell.Resources = new ResourceDictionary();
        Resources = resources;
        foreach (var style in shell.Styles.ToArray()) { shell.Styles.Remove(style); Styles.Add(style); }
        RequestedThemeVariant = ThemeVariant.Dark;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var stage = new Grid();
        var window = new Window { Title = "DaisyOS isolated smoothness acceptance", Width = 1100, Height = 700,
            Background = Brushes.DimGray, Content = stage };
        desktop.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            var checks = new List<string>();
            try
            {
                var launcher = new LauncherView(new LauncherViewModel(new DaisyOS.Shell.Services.ShellSessionState(),
                    Enumerable.Range(0, 2000).Select(i => new AppEntry($"qa-{i}", $"Application {i:D4}", "", "", false)).ToArray()))
                    { IsVisible = false };
                stage.Children.Add(launcher);
                for (var i = 0; i < 3; i++)
                {
                    launcher.AnimateIn(); await Task.Delay(300);
                    launcher.CloseImmediate(); await Task.Delay(100);
                }
                checks.Add("launcher repeated reveal");
                stage.Children.Clear();
                var bar = new SystemBarView(); stage.Children.Add(bar);
                await Task.Delay(300);
                foreach (var (show, hide, visible) in new (Action Show, Action Hide, Func<bool> Visible)[]
                {
                    (bar.ShowQuickSettingsPanel, () => bar.HideQuickSettingsPanel(), () => bar.IsQuickSettingsVisible),
                    (bar.ShowNotificationPanel, () => bar.HideNotificationPanel(), () => bar.IsNotificationPanelVisible),
                    (bar.ShowCalendarPanel, () => bar.HideCalendarPanel(), () => bar.IsCalendarPanelVisible),
                    (bar.ShowAppTrayPanel, () => bar.HideAppTrayPanel(), () => bar.IsAppTrayVisible)
                })
                {
                    show(); await Task.Delay(300);
                    Require(visible(), "Flyout did not open");
                    hide(); await Task.Delay(30); show(); await Task.Delay(300);
                    Require(visible(), "Stale dismissal hid reopened flyout");
                    hide(); await Task.Delay(250);
                    Require(!visible(), "Flyout did not close");
                }
                checks.Add("all system flyouts and rapid reversals");
                stage.Children.Clear();
                var toasts = new NotificationToastHost(); stage.Children.Add(toasts);
                await Task.Delay(100);
                var notification = new NotificationItem("Fixture", "Native toast", DateTimeOffset.Now);
                Require(toasts.Present(notification), "Toast did not appear");
                await Task.Delay(300);
                Require(toasts.Present(notification with { Body = "Updated fixture" }), "Toast replacement failed");
                Require(toasts.HasActiveToasts, "Toast replacement lost active card");
                stage.Children.Clear(); await Task.Delay(100);
                Require(!toasts.HasActiveToasts, "Detached toast kept timers/content alive");
                checks.Add("toast arrival, replacement and detachment");
                var files = new FilesWindow(Files, false); files.Show();
                try
                {
                    var tab = ((FilesViewModel)files.DataContext!).ActiveTab!;
                    await tab.NavigationTask.WaitAsync(TimeSpan.FromSeconds(10));
                    Require(tab.Entries.Count == 300, "Files fixture did not load");
                    foreach (var grid in new[] { false, true })
                    {
                        tab.IsGridView = grid; await Task.Delay(200);
                        var list = files.FindControl<ListBox>(grid ? "EntriesGrid" : "EntriesList")!;
                        list.ScrollIntoView(tab.FilteredEntries[150]); await Task.Delay(200);
                        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                        Require(scroll.Offset.Y > 0, "Files viewport did not scroll");
                        Require(list.GetRealizedContainers().Count() < 300, "Files view realized entire directory");
                    }
                }
                finally { files.Close(); }
                checks.Add("Files loading, list/grid scrolling and virtualization");
                var settings = new SettingsView(); stage.Children.Add(settings);
                foreach (var category in new[] { "System", "About", "Accessibility", "System" })
                { settings.SelectCategory(category); await Task.Delay(300); }
                checks.Add("settings navigation");
                stage.Children.Clear();
                using var notes = new NotesViewModel(Path.Combine(Path.GetDirectoryName(Files)!, "notes"));
                // No files are created or saved by this fixture.
                notes.Notes.Add(new NoteItem { Content = "First fixture", IsDirty = false });
                notes.Notes.Add(new NoteItem { Content = "Second fixture", IsDirty = false });
                var notesView = new NotesView { DataContext = notes }; stage.Children.Add(notesView);
                foreach (var note in notes.Notes) { notes.SelectedNote = note; await Task.Delay(300); }
                checks.Add("notes selection");
                stage.Children.Clear();
                var content = new Border { Width = 200, Height = 120, Background = Brushes.SlateGray };
                var host = new Border { Child = content, IsVisible = false }; stage.Children.Add(host);
                for (var i = 0; i < 10; i++)
                {
                    var reveal = GuiMotion.SetScaleVisibleAsync(host, content, true);
                    await Task.Delay(20);
                    var hide = GuiMotion.SetScaleVisibleAsync(host, content, false);
                    await Task.Delay(20);
                    var reopen = GuiMotion.SetScaleVisibleAsync(host, content, true);
                    await Task.WhenAll(reveal, hide, reopen).WaitAsync(TimeSpan.FromSeconds(5));
                    Require(host.IsVisible && host.IsHitTestVisible && content.RenderTransform is null, "Scale reversal failed");
                }
                var exit = GuiMotion.SetVisibleAsync(host, content, false);
                GuiMotion.SettleActiveAnimations(); await exit;
                Require(!host.IsVisible, "Reduced motion did not finish dismissal");
                checks.Add("scale reversals and active-motion settlement");
                await ResponsivenessTrace.FlushAsync();
                var frames = (Avalonia.Logging.Logger.Sink as FrameTrace)?.CompletedFrames;
                if (Environment.GetEnvironmentVariable("DAISYOS_ACCEPTANCE_REQUIRE_FRAMES") == "1")
                {
                    Require(frames >= 10, "Compositor supplied too few frame callbacks; active pacing was not validated");
                    var intervals = File.ReadLines(Path.Combine(Output, "callbacks.jsonl"))
                        .Select(line => JsonDocument.Parse(line))
                        .Select(document => { using (document) { var root = document.RootElement;
                            return root.GetProperty("stage").GetString() == "motion.frame.interval" ? root.GetProperty("durationMs").GetDouble() : (double?)null; } })
                        .Where(value => value.HasValue).Select(value => value!.Value).ToArray();
                    Require(intervals.Length > 20 && intervals.Length < frames * 8,
                        "Animation callbacks were absent or spun independently of platform frames");
                    checks.Add("platform frame permission and bounded animation callback work");
                }
                File.WriteAllText(Path.Combine(Output, "acceptance.json"), JsonSerializer.Serialize(new
                {
                    passed = true, checks, platformFrameCallbacks = frames, presentationMeasured = false,
                    boundary = "Scripted controls in disposable native Wayland window; does not replace installed shell or validate layer-shell blur/physical input"
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                ExitCode = 1;
                File.WriteAllText(Path.Combine(Output, "failure.txt"), ex.ToString());
                Console.Error.WriteLine(ex);
            }
            finally { window.Close(); }
        };
        base.OnFrameworkInitializationCompleted();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
public sealed class Fixture : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("daisy-smoothness-config-").FullName;
    public void Dispose() => Directory.Delete(Path, true);
}

public sealed class FrameTrace : Avalonia.Logging.ILogSink
{
    private int _completedFrames;
    public int CompletedFrames => Volatile.Read(ref _completedFrames);
    private readonly global::System.Diagnostics.Stopwatch _clock = global::System.Diagnostics.Stopwatch.StartNew();
    public bool IsEnabled(Avalonia.Logging.LogEventLevel level, string area) => area == "WaylandProtocol" || level >= Avalonia.Logging.LogEventLevel.Warning;
    public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string template) { Console.Error.WriteLine(template); }
    public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string template, params object?[] values)
    {
        if (level >= Avalonia.Logging.LogEventLevel.Warning)
            Console.Error.WriteLine(template + " " + string.Join(" | ", values.Select(value => value?.ToString())));
        if (values.FirstOrDefault() is string message)
        {
            var completed = message.Contains("WlCallback") && message.Contains("-done(");
            if (completed) Interlocked.Increment(ref _completedFrames);
            if (Environment.GetEnvironmentVariable("DAISYOS_ACCEPTANCE_FRAME_TRACE") == "1" && (message.Contains("-frame(") || completed))
                Console.Error.WriteLine($"{_clock.Elapsed.TotalMilliseconds:F2} {message}");
        }
    }
}
