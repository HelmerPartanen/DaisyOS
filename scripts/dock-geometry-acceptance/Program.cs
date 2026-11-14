using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Wayland;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Shell.Services.Shell;

if (args.Length != 1) throw new ArgumentException("Usage: DockGeometryAcceptance <artifact-directory>");
ProbeApplication.Artifacts = Path.GetFullPath(args[0]);
AppBuilder.Configure<ProbeApplication>().UseWayland().UseSkia().UseHarfBuzz().StartWithClassicDesktopLifetime([]);

public sealed class ProbeApplication : Application
{
    public static string Artifacts = "";
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var options = LayerShellSurfaceOptions.PrimaryPanel(-1) with
        { Namespace = "daisyos.probe.dock-geometry", InputPassthrough = true, KeyboardInteractivity = LayerShellKeyboardInteractivity.None };
        var window = new AvaloniaLayerShellWindowBackend().CreateWindow(DockGeometry.AtEdge(options, DockEdge.Bottom, 82), new Border { Background = Brushes.Transparent });
        window.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        lifetime.MainWindow = window;
        window.Show();
        Dispatcher.UIThread.Post(async () =>
        {
            var results = new List<object>();
            var exitCode = 1;
            try
            {
                foreach (var edge in new[] { DockEdge.Bottom, DockEdge.Left, DockEdge.Top, DockEdge.Right, DockEdge.Bottom })
                {
                    var thickness = (uint)(ShellSurfacePlan.PanelExtent(edge, 1, true) + ShellSurfacePlan.PrimaryPanelShadowBleed);
                    AvaloniaLayerShellWindowBackend.SetGeometry(window, DockGeometry.AtEdge(options, edge, thickness));
                    var matched = false;
                    for (var attempt = 0; attempt < 100; attempt++)
                    {
                        await Task.Delay(50);
                        var size = window.ClientSize;
                        if (DockGeometry.IsVertical(edge) ? Math.Abs(size.Width - thickness) <= 1 / window.RenderScaling && size.Height > 300 : Math.Abs(size.Height - thickness) <= 1 / window.RenderScaling && size.Width > 300)
                        { matched = true; break; }
                    }
                    if (!matched) throw new Exception($"{edge}: unexpected native configure {window.ClientSize}");
                    // Exercise explicit regions while keeping the probe entirely click-through:
                    // this tiny region is outside the surface's bounds.
                    LayerShellWindow.SetInputRectangles(window, [new Rect(-1000, -1000, 1, 1)]);
                    LayerShellWindow.SetInputPassthrough(window, false);
                    await Task.Delay(100);
                    results.Add(new { Edge = edge.ToString(), window.ClientSize.Width, window.ClientSize.Height });
                }
                Directory.CreateDirectory(Artifacts);
                await File.WriteAllTextAsync(Path.Combine(Artifacts, "native-geometry.json"), System.Text.Json.JsonSerializer.Serialize(results));
                Console.WriteLine("PASS: persistent native surface configured on all four edges; input regions accepted.");
                exitCode = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { window.Close(); lifetime.Shutdown(exitCode); }
        });
    }
}
