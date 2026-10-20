using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Shell;
using DaisyOS.Shell.Apps.AppStore;
using DaisyOS.Shell.Controls;
using DaisyOS.System.AppStore;
using DaisyOS.System.Processes;

var output = args.FirstOrDefault() ?? "artifacts/app-store";
Directory.CreateDirectory(output);
AppBuilder.Configure<StoreQaApp>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia().UseHarfBuzz()
    .With(new FontManagerOptions { DefaultFamilyName = "avares://DaisyOS.Shell/Assets/fonts/AtkinsonHyperlegible#Atkinson Hyperlegible" })
    .SetupWithoutStarting();
var service = new LinuxAppStoreService(new SafeCommandRunner());
if (args.Contains("--refresh")) Pump(service.RefreshCatalogAsync());
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
foreach (var width in new[] { 800, 1120, 2300 })
{
    using var vm = new AppStoreViewModel(service);
    var window = (AppStoreWindow)Activator.CreateInstance(typeof(AppStoreWindow), BindingFlags.Instance | BindingFlags.NonPublic, null, [vm], null)!;
    window.Width = width; window.Height = 760; window.RequestedThemeVariant = theme; window.Show();
    Pump(vm.LoadCategoryAppsAsync(AppCategory.Discover)); Tick(40);
    var sidebar = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NavigationSidebar");
    if (Math.Abs(sidebar.Bounds.Height - ((Grid)sidebar.Parent!).Bounds.Height) > 1) throw new Exception("Sidebar is not full height.");
    if (window.WindowDecorations != WindowDecorations.None) throw new Exception("Duplicate titlebar.");
    var view = window.GetVisualDescendants().OfType<AppStoreView>().Single();
    Console.WriteLine($"Width {width}: view={view.Bounds.Width}, cards={view.CardColumns}, featured={view.FeaturedColumns}, grids={string.Join(',', view.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>().Select(g => g.Columns))}");
    var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().Single(v => v.Name == "AppsScroller");
    foreach (var renderedCard in scroller.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("StoreCard")))
    {
        var point = renderedCard.TranslatePoint(default, scroller)!.Value;
        if (point.X < -1 || point.X + renderedCard.Bounds.Width > scroller.Viewport.Width + 1)
            throw new Exception($"Card horizontal overflow: {point.X} + {renderedCard.Bounds.Width} > {scroller.Viewport.Width}");
    }
    Save("discover");
    var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("StoreCard"));
    var cardPoint = card.TranslatePoint(new Point(20, 20), window) ?? throw new Exception("Card has no window position.");
    window.MouseMove(cardPoint); Tick(20); Save("discover-hover");
    if (card.BorderThickness != new Thickness(0)) throw new Exception("Card unexpectedly has a border.");
    window.MouseMove(new Point(1, 1));
    var firefox = vm.FeaturedApps.FirstOrDefault(c => c.Id == "org.mozilla.firefox") ?? vm.Apps[0];
    if (args.Contains("--screenshots-only"))
    {
        vm.SearchQuery = "Blender"; Pump(vm.RefreshAsync());
        firefox = vm.Apps.First(c => c.Id == "org.blender.Blender");
    }
    vm.ShowDetails(firefox); Tick(150); Save("detail");
    foreach (var thumbnail in view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ScreenshotThumbnail")))
        if (Math.Abs(thumbnail.Bounds.Height - 280) > 1) throw new Exception("Screenshot thumbnail collapsed.");
    if (firefox.Screenshots.Count > 0)
    {
        var screenshot = view.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is string url && firefox.Screenshots.Contains(url));
        screenshot.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Tick(150); Save("screenshot-viewer");
        var dialog = view.FindControl<Grid>("ScreenshotDialog")!;
        if (dialog.Bounds.Width > 960 || dialog.Bounds.Height > 600) throw new Exception("Screenshot viewer exceeds its size limit.");
        if (!view.FindControl<Grid>("StorePages")!.IsVisible) throw new Exception("Screenshot viewer replaced the app page.");
        if (view.FindControl<Border>("ScreenshotImageFrame")!.Background is not null) throw new Exception("Screenshot viewer has a background box.");
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.ArrowRight, Avalonia.Input.RawInputModifiers.None);
        Tick(150); Save("screenshot-viewer-next");
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None);
        if (!vm.IsDetailMode || view.FindControl<Grid>("ScreenshotOverlay")!.IsVisible) throw new Exception("Screenshot viewer failed to close to details.");
    }
    if (args.Contains("--screenshots-only")) { window.Close(); continue; }
    vm.BackCommand.Execute(null); vm.SearchQuery = "video editor"; Pump(vm.RefreshAsync()); Tick(20); Save("search");
    vm.SelectedCategory = vm.LibraryCategories[0]; Pump(vm.RefreshAsync()); Tick(20); Save("installed");
    vm.SelectedCategory = vm.LibraryCategories[1]; Pump(vm.RefreshAsync()); Tick(20); Save("updates");
    window.Close();
    void Save(string page)
    {
        Tick(4);
        using var bitmap = window.CaptureRenderedFrame() ?? throw new Exception("No rendered frame.");
        bitmap.Save(Path.Combine(output, $"{page}-{theme.Key}-{width}.png"));
    }
}
Console.WriteLine(args.Contains("--screenshots-only") ? "PASS: real Blender thumbnails and compact viewer in both themes at 800, 1120 and 2300 px." : "PASS: real Flathub catalog, detail, search, installed and updates in both themes and widths");
static void Tick(int count)
{
    for (var i = 0; i < count; i++)
    { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); }
}
static void Pump(Task task)
{
    var timer = Stopwatch.StartNew();
    while (!task.IsCompleted) { Tick(1); if (timer.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException(); }
    task.GetAwaiter().GetResult();
}
public sealed class StoreQaApp : Application
{
    public override void Initialize()
    {
        var shell = new App(); shell.Initialize();
        var resources = shell.Resources; shell.Resources = new ResourceDictionary(); Resources = resources;
        foreach (var style in shell.Styles.ToArray()) { shell.Styles.Remove(style); Styles.Add(style); }
    }
}
