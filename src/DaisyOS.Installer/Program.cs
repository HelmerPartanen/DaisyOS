using Avalonia;

namespace DaisyOS.Installer;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect()
            .With(new Avalonia.Media.FontManagerOptions { DefaultFamilyName = "Atkinson Hyperlegible" })
            .LogToTrace();
}
