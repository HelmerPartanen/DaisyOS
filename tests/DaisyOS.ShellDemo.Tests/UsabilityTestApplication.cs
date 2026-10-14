using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(DaisyOS.ShellDemo.Tests.UsabilityTestApplication))]
// Avalonia's render loop and application resources are process-wide. Keep the
// two workflow classes on one headless application thread at a time.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace DaisyOS.ShellDemo.Tests;

public sealed class UsabilityTestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        DaisyOS.Shell.Services.Localization.Loc.Apply(Resources, "en-US");
        Resources["ShellFontFamily"] = new Avalonia.Media.FontFamily("avares://DaisyOS.Shell/Assets/fonts#Inter");
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://DaisyOS.Shell/"))
        {
            Source = new Uri("avares://DaisyOS.Shell/Themes/DaisyTheme.axaml")
        });
        var toggleTheme = new ResourceInclude(new Uri("avares://DaisyOS.Shell/"))
        {
            Source = new Uri("avares://DaisyOS.Shell/Themes/ToggleSwitchTheme.axaml")
        };
        Resources.MergedDictionaries.Add(toggleTheme);
        Resources[typeof(Avalonia.Controls.ToggleSwitch)] = toggleTheme.Loaded["DaisyToggleSwitchTheme"];
        var choices = new ResourceInclude(new Uri("avares://DaisyOS.Shell/"))
        {
            Source = new Uri("avares://DaisyOS.Shell/Themes/SelectionControlThemes.axaml")
        };
        Resources.MergedDictionaries.Add(choices);
        Resources[typeof(Avalonia.Controls.CheckBox)] = choices.Loaded["DaisyCheckBoxTheme"];
        Resources[typeof(Avalonia.Controls.RadioButton)] = choices.Loaded["DaisyRadioButtonTheme"];
        Styles.Add(new StyleInclude(new Uri("avares://DaisyOS.Shell/"))
        {
            Source = new Uri("avares://DaisyOS.Shell/Styles/ComponentStyles.axaml")
        });
        Styles.Add(new StyleInclude(new Uri("avares://DaisyOS.Shell/"))
        {
            Source = new Uri("avares://DaisyOS.Shell/Styles/DropdownMenuStyles.axaml")
        });
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<UsabilityTestApplication>()
        .UseSkia().UseHarfBuzz().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
