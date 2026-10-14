using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;
using DaisyOS.Core.Models;

var output = args[0];
AppBuilder.Configure<QaApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia().UseHarfBuzz().With(new FontManagerOptions { DefaultFamilyName = "avares://DaisyOS.Shell/Assets/fonts#Inter" }).SetupWithoutStarting();
var cold = Stopwatch.StartNew();
var view = new LauncherView();
var window = new Window { Width=720, Height=600, Content=view, Background=Brushes.Gray };
window.Show();
Tick();
var coldMs=cold.Elapsed.TotalMilliseconds;
// Stop the initial asynchronous discovery before injecting a deterministic 2,000-app catalog.
var wait=Stopwatch.StartNew();
while(view.ViewModel.AllApps.Count==0 && wait.Elapsed.TotalSeconds<10) { Tick(); Thread.Sleep(5); }
var entries=Enumerable.Range(0,2000).Select(i=>new AppEntry($"qa-{i}",$"Application {i:D4}","Test application","",false)).ToArray();
var apply=typeof(LauncherViewModel).GetMethod("ApplyApplicationCatalog",BindingFlags.Instance|BindingFlags.NonPublic)!;
apply.Invoke(view.ViewModel,[entries]);Tick();
var grid=view.FindControl<ListBox>("ApplicationGrid");
var realized=grid?.GetRealizedContainers().Count() ?? view.GetVisualDescendants().OfType<Button>().Count(b=>b.Classes.Contains("LauncherTile"));
if(realized==0 || realized>150) throw new Exception($"Grid failed virtualization: {realized} containers");
var original=view.ViewModel.AllApps[100];apply.Invoke(view.ViewModel,[entries]);
if(!ReferenceEquals(original,view.ViewModel.AllApps[100])) throw new Exception("Catalog lost stable identity");
var search=new List<double>();var toggle=new List<double>();
for(int i=0;i<30;i++)
{
    var timer=Stopwatch.StartNew();view.ViewModel.SearchText=$"{i:D2}";Tick();search.Add(timer.Elapsed.TotalMilliseconds);
    if(view.ViewModel.FilteredApps.Any(item=>!item.SearchKey.Contains($"{i:D2}"))) throw new Exception("Stale search projection");
    view.ViewModel.SearchText="obsolete";view.ViewModel.SearchText="Application";Tick();
    if(view.ViewModel.FilteredApps.Count!=2000) throw new Exception("Coalesced query lost last result");
    timer.Restart();view.IsVisible=false;Tick();view.IsVisible=true;Tick();toggle.Add(timer.Elapsed.TotalMilliseconds);
}
if(grid is not null)
{
    grid.ScrollIntoView(view.ViewModel.FilteredApps[1500]);Tick();Tick();
    if(grid.ContainerFromItem(view.ViewModel.FilteredApps[1500]) is null) throw new Exception("Grid scroll target missing");
}
using(var frame=window.CaptureRenderedFrame()!) frame.Save(output+".png");
var process=Process.GetCurrentProcess();
File.WriteAllText(output,JsonSerializer.Serialize(new {mode="headless Skia; CPU render pass, not KWin presentation",coldConstructionRenderMs=coldMs,catalogSize=2000,realizedContainers=realized,search=Summary(search),warmVisibility=Summary(toggle),rssBytes=process.WorkingSet64,managedBytes=GC.GetTotalMemory(false)},new JsonSerializerOptions{WriteIndented=true}));
window.Close();
static object Summary(List<double> values){values.Sort();return new {samples=values.Count,p50Ms=values[14],p95Ms=values[28],worstMs=values[^1]};}
static void Tick(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
public sealed class QaApp:Application
{
    public override void Initialize()
    {
        var shell=new App();shell.Initialize();Resources=shell.Resources;shell.Resources=new ResourceDictionary();
        foreach(var style in shell.Styles.ToArray()){shell.Styles.Remove(style);Styles.Add(style);}
        RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;
    }
}
