using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps;

namespace DaisyOS.Shell.Apps.System.Settings;

public partial class SettingsWindow : Window
{
    private readonly SystemAppWindowChrome _fullscreenController;

    public SettingsWindow()
    {
        InitializeComponent();
        _fullscreenController = new SystemAppWindowChrome(this,
            SettingsContent.FindControl<Controls.AppWindowTitleBar>("AppTitleBar")!,
            SettingsContent.FindControl<Grid>("MainPaneLayout")!, normalRowDefinitions: "Auto,*");
    }

    protected override void OnClosed(EventArgs e)
    {
        _fullscreenController.Dispose();
        base.OnClosed(e);
    }

    public void PrepareContinuitySend(string path) => SettingsContent.PrepareContinuitySend(path);

    public void SelectCategory(string category) => SettingsContent.SelectCategory(category);

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source &&
            source is not TextBox &&
            source is not Controls.SearchBar &&
            !source.GetVisualAncestors().Any(v => v is Controls.SearchBar || v is TextBox))
        {
            FocusManager?.Focus(null);
        }
    }

}
