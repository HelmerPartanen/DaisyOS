using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.Views;
using DaisyOS.System.Processes;

if (args.Length != 1) throw new ArgumentException("Usage: multi-monitor-acceptance <output-directory>");
if (Environment.GetEnvironmentVariable("DAISYOS_MULTI_MONITOR_ISOLATED") != "1" ||
    !(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "").StartsWith("daisy-multimonitor-"))
    throw new InvalidOperationException("Use run-multi-monitor-acceptance.sh; this program must not run on the physical desktop.");
var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var runner = new SafeCommandRunner();
var exitCode = 1;
Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", "1");
DaisyOS.Shell.Program.BuildAvaloniaApp(() => new App((app, desktop) =>
{
    Method("StartNativeShellSession").Invoke(app, [desktop, Task.FromResult(true)]);
    Dispatcher.UIThread.Post(async () =>
    {
        var checks = new List<string>();
        IReadOnlyList<DetectedDisplay>? original = null;
        Window? settings = null;
        void Check(string name) { checks.Add(name); Console.WriteLine("[multi-monitor] " + name); }
        try
        {
            var rawOutputs = await runner.RunAsync("kscreen-doctor", ["-j"], TimeSpan.FromSeconds(5));
            File.WriteAllText(Path.Combine(output, "initial-outputs.json"), rawOutputs.StandardOutput);
            original = await DisplaySettingsHelper.DetectAllDisplaysAsync();
            Require(original.Count == 2, $"Expected two active virtual outputs, got {original.Count}: {rawOutputs.StandardError}");
            await Wait(() => Wallpapers(app).Count == 1, "secondary wallpaper did not map");
            Require(Surfaces(app).Count(w => w.Content is DaisyOS.Shell.Views.Components.Wallpaper.WallpaperLayer) == 2,
                "Expected one wallpaper per output");
            var layers = Surfaces(app).Select(w => w.Content).OfType<DaisyOS.Shell.Views.Components.Wallpaper.WallpaperLayer>().ToArray();
            await Wait(() => layers.All(layer => layer.PresentationState == DaisyOS.Shell.Views.Components.Wallpaper.WallpaperLayer.WallpaperPresentationState.Presented),
                "wallpaper content did not finish presenting on both outputs");
            var wallpaperService = (DaisyOS.Shell.Services.Wallpaper.WallpaperService)Field("_wallpaperService").GetValue(app)!;
            await wallpaperService.SetWallpaperAsync("solid:#305070");
            await Wait(() => layers.All(layer => layer.FindControl<Border>("SolidColorBackground")!.Background is Avalonia.Media.ISolidColorBrush brush &&
                brush.Color.R == 48 && brush.Color.G == 80 && brush.Color.B == 112), "wallpaper change did not reach every output");
            Check("two wallpaper surfaces with presented content and synchronized changes");
            var other = original.Single(d => !d.IsPrimary);
            Require(await DisplaySettingsHelper.SetPrimaryDisplayAsync(other.Name), "primary change failed");
            await Wait(() => (string?)Field("_nativePrimaryOutputName").GetValue(app) == other.Name, "primary identity did not migrate");
            await Task.Delay(500);
            var panel = (Window)Field("_nativePanelSurface").GetValue(app)!;
            Require(panel.Screens.ScreenFromWindow(panel)?.DisplayName == other.Name, "panel native surface did not migrate");
            Require(Wallpapers(app).Count == 1, "primary migration duplicated wallpaper");
            Check("primary-output migration preserves native shell surfaces");
            var lazyOverlay = (Window)Field("_nativeScreenshotSurface").GetValue(app)!;
            lazyOverlay.Show();
            await Wait(() => lazyOverlay.Screens.ScreenFromWindow(lazyOverlay)?.DisplayName == other.Name,
                "unmapped overlay did not follow the primary output");
            lazyOverlay.Hide(); lazyOverlay.Show();
            await Wait(() => lazyOverlay.Screens.ScreenFromWindow(lazyOverlay)?.DisplayName == other.Name,
                "remapping an overlay lost its output selection");
            lazyOverlay.Hide();
            Check("unmapped overlays retain their output across hide/show");
            var secondary = original.Single(d => d.Name != other.Name);
            await Command([$"output.{secondary.Name}.scale.1.25"]);
            await Task.Delay(2500);
            var current = await DisplaySettingsHelper.DetectAllDisplaysAsync();
            Require(Math.Abs(current.Single(d => d.Name == secondary.Name).Scale - 1.25) < .001, "mixed scale did not apply");
            Check("mixed per-output scaling");
            var view = new SettingsView();
            settings = new Window { Title = "Display acceptance", Width = 1000, Height = 780, Content = view };
            settings.Show(); view.SelectCategory("Displays"); await view.DisplayPageReady;
            for (var i = 0; i < 4; i++)
            {
                var target = i % 2 == 0 ? secondary.Name : other.Name;
                view.GetVisualDescendants().OfType<Button>().Single(b => b.Content is string text && text.StartsWith(target))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.DisplayPageReady;
                Require(view.GetVisualDescendants().OfType<ComboBox>().Count(p => p.Name == "DisplayLayoutMode") == 1,
                    "output switch duplicated settings");
            }
            Check("repeated output selection renders exactly one settings page");
            async Task Layout(bool mirror, bool keep)
            {
                var picker = view.GetVisualDescendants().OfType<ComboBox>().Single(p => p.Name == "DisplayLayoutMode");
                picker.SelectedIndex = mirror ? 1 : 0;
                await Wait(() => settings.OwnedWindows.Any(w => w.IsVisible), "display confirmation did not appear");
                var change = (Task)typeof(SettingsView).GetField("_pendingDisplayChange", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
                var confirmation = settings.OwnedWindows.Single(w => w.IsVisible);
                var button = confirmation.GetVisualDescendants().OfType<Button>().Single(b =>
                    Equals(b.Content, DaisyOS.Shell.Services.Localization.Loc.Get(keep ? "Keep changes" : "Revert")));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await change.WaitAsync(TimeSpan.FromSeconds(15));
                await Task.Delay(150);
                await view.DisplayPageReady;
            }
            var extended = await DisplaySettingsHelper.DetectAllDisplaysAsync();
            await Layout(true, false);
            current = await DisplaySettingsHelper.DetectAllDisplaysAsync();
            Require(current.All(d => d.MirrorSource is null), "reverting mirror did not restore extend");
            Require(current.All(d => extended.Any(old => old.Name == d.Name && old.PositionX == d.PositionX && old.PositionY == d.PositionY)),
                "revert lost original positions");
            await Layout(true, true);
            current = await DisplaySettingsHelper.DetectAllDisplaysAsync();
            Require(current.Any(d => d.MirrorSource is not null), "mirror metadata was not read back");
            await Layout(false, true);
            current = await DisplaySettingsHelper.DetectAllDisplaysAsync();
            Require(current.All(d => d.MirrorSource is null), "extend did not clear mirroring");
            Require(current.All(d => extended.Any(old => old.Name == d.Name && old.PositionX == d.PositionX && old.PositionY == d.PositionY)),
                "switching back to extend did not restore the remembered arrangement");
            Check("mirror/extend keep and full-layout revert");
            await Command([$"output.{secondary.Name}.position.0,200", $"output.{other.Name}.position.{secondary.LogicalWidth},0"]);
            await Task.Delay(2500);
            current = await DisplaySettingsHelper.DetectAllDisplaysAsync();
            Require(current.Single(d => d.Name == secondary.Name).PositionY == 200, "vertical arrangement did not apply");
            Check("horizontal and vertical display arrangement");
            settings.Close(); settings = null;
            var videoScript = Path.Combine(output, "video-fixture.py");
            File.WriteAllText(videoScript, "from PySide6.QtWidgets import QApplication,QWidget\na=QApplication([])\nw=QWidget()\nw.setWindowTitle('Multi Monitor Video Fixture')\nw.showFullScreen()\na.exec()\n");
            var videoStart = new ProcessStartInfo("python3") { UseShellExecute = false }; videoStart.ArgumentList.Add(videoScript);
            using (var video = Process.Start(videoStart)!)
            {
                try
                {
                    await Task.Delay(700);
                    var shell = (ShellView)Field("_shellView").GetValue(app)!;
                    foreach (var target in new[] { secondary.Name, other.Name })
                    {
                        await RunScript(output, "var w=workspace.windowList().find(w=>w.caption==='Multi Monitor Video Fixture');" +
                            "var o=workspace.screens.find(o=>o.name===" + JsonSerializer.Serialize(target) + ");" +
                            "if(w&&o){workspace.sendClientToScreen(w,o);w.fullScreen=true;}");
                        await Task.Delay(500);
                        await new KWinCommandDispatcher().RequestSnapshotAsync(default);
                        await Task.Delay(400);
                        Require(shell.IsExternalFullscreenActive == (target == other.Name),
                            "fullscreen on one output changed the other output's bottom chrome");
                    }
                }
                finally { if (!video.HasExited) video.Kill(true); await video.WaitForExitAsync(); }
            }
            Check("real fullscreen client on secondary leaves primary bars available");
            await Command([$"output.{secondary.Name}.disable"]);
            await Wait(() => Wallpapers(app).Count == 0, "unplug left a wallpaper surface behind");
            await Command([$"output.{secondary.Name}.enable"]);
            await Wait(() => Wallpapers(app).Count == 1, "reconnect did not recreate wallpaper");
            Check("disable/re-enable reconciles wallpaper surfaces");
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = true, checks,
                boundary = "Isolated KWin virtual outputs; real layer surfaces and commands, no physical display certification" }, new JsonSerializerOptions { WriteIndented = true }));
            exitCode = 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); Console.Error.WriteLine(ex); }
        finally
        {
            settings?.Close();
            if (original is not null) await DisplaySettingsHelper.RestoreConfigurationAsync(original, runner);
            Method("StopNativeDisplayTracking").Invoke(app, null);
            foreach (var window in desktop.Windows.ToArray()) window.Close();
            desktop.Shutdown(exitCode);
        }
    });
})).StartWithClassicDesktopLifetime([]);
return exitCode;

static MethodInfo Method(string name) => typeof(App).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
static FieldInfo Field(string name) => typeof(App).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
static Dictionary<string, Window> Wallpapers(App app) => (Dictionary<string, Window>)Field("_secondaryWallpaperSurfaces").GetValue(app)!;
static List<Window> Surfaces(App app) => (List<Window>)Field("_nativeShellSurfaces").GetValue(app)!;
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static async Task Wait(Func<bool> predicate, string message)
{
    var clock = Stopwatch.StartNew();
    while (!predicate()) { if (clock.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException(message); await Task.Delay(100); }
}
static async Task Command(string[] arguments)
{
    var result = await new SafeCommandRunner().RunAsync("kscreen-doctor", arguments, TimeSpan.FromSeconds(5));
    Require(result.Succeeded, result.StandardError + result.StandardOutput);
}
static async Task RunScript(string output, string source)
{
    var path = Path.Combine(output, "window-command.js"); File.WriteAllText(path, source);
    var runner = new SafeCommandRunner();
    var loaded = await runner.RunAsync("qdbus6", ["org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting.loadScript", path, "daisy-multimonitor-fixture"], TimeSpan.FromSeconds(3));
    Require(loaded.Succeeded, "Could not load fixture window command");
    await runner.RunAsync("qdbus6", ["org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting.start"], TimeSpan.FromSeconds(3));
    await runner.RunAsync("qdbus6", ["org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting.unloadScript", "daisy-multimonitor-fixture"], TimeSpan.FromSeconds(3));
}
