using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using DaisyOS.Shell;
using DaisyOS.Shell.Apps.Calculator;
var output=args.FirstOrDefault()??"artifacts/rust-ui/reference";Directory.CreateDirectory(output);
AppBuilder.Configure<ReferenceApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).UseSkia().UseHarfBuzz().SetupWithoutStarting();
DaisyOS.Shell.Services.Localization.Loc.Apply(Application.Current!.Resources,"en-US");
foreach(var dark in new[]{false,true})foreach(var scientific in new[]{false,true})
{
    var theme=dark?ThemeVariant.Dark:ThemeVariant.Light;
    Application.Current!.RequestedThemeVariant=theme;
    Application.Current.Resources["ShellFontFamily"]=new FontFamily("avares://DaisyOS.Shell/Assets/fonts/AtkinsonHyperlegible#Atkinson Hyperlegible");
    var tokens=System.Text.Json.JsonDocument.Parse(File.ReadAllText("crates/daisy-ui/themes/source-tokens.json"));
    var rgba=tokens.RootElement.GetProperty("accents").GetProperty(dark?"Dark":"Light")[8].GetString()!;
    var accent=Color.Parse("#"+rgba[7..9]+rgba[1..7]);
    var ink=Color.Parse("#111113");var white=Colors.White;
    var content=Contrast(accent,ink)>=Contrast(accent,white)?ink:white;
    foreach(var pair in new[]{("ActionPrimaryBrush",accent),("ContentOnActionBrush",content),("FocusRingBrush",accent)})Application.Current.Resources[pair.Item1]=new SolidColorBrush(pair.Item2);
    var window=new CalculatorWindow();if(scientific)((CalculatorViewModel)window.DataContext!).ToggleMode();window.Show();
    for(var i=0;i<8;i++)Dispatcher.UIThread.RunJobs();
    using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(360,560),new Vector(96,96));bitmap.Render(window);bitmap.Save(Path.Combine(output,$"calculator-{(dark?"dark":"light")}-{(scientific?"scientific":"standard")}.png"));window.Close();
}
Console.WriteLine("PASS: captured existing Avalonia Calculator standard/scientific light/dark references");
static double Contrast(Color a,Color b){var x=Luma(a);var y=Luma(b);return(Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
static double Luma(Color c){static double L(byte c){double v=c/255d;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}return .2126*L(c.R)+.7152*L(c.G)+.0722*L(c.B);}
public sealed class ReferenceApp:Application
{
 public override void Initialize(){var original=new App();original.Initialize();var resources=original.Resources;original.Resources=new ResourceDictionary();Resources=resources;foreach(var style in original.Styles.ToArray()){original.Styles.Remove(style);Styles.Add(style);}}
}
