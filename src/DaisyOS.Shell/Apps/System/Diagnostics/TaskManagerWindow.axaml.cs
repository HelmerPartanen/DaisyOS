using System;
using Avalonia;
using Avalonia.Controls;
using DaisyOS.Shell.Apps;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

public partial class TaskManagerWindow : Window
{
    private readonly SystemAppWindowChrome _fullscreenController;

    public TaskManagerWindow() : this(null) { }

    public TaskManagerWindow(Services.Compositor.ICompositorWindowService? windows)
    {
        InitializeComponent();

        DataContext = new TaskManagerViewModel(windows);
        ActualThemeVariantChanged += (_, _) => (DataContext as TaskManagerViewModel)?.RefreshAppearance();
        _fullscreenController = new SystemAppWindowChrome(this,
            MonitorContent.FindControl<Controls.AppWindowTitleBar>("AppTitleBar")!,
            MonitorContent.FindControl<Grid>("MainPaneLayout")!, normalRowDefinitions: "Auto,*");
    }

    public static TaskManagerWindow CreateStandalone()
    {
        var windows = new Services.Compositor.ShellWindowClient();
        var window = new TaskManagerWindow(windows);
        window.Opened += async (_, _) => await windows.StartAsync();
        window.Closed += async (_, _) => await windows.DisposeAsync();
        return window;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty || change.Property == IsVisibleProperty)
            (DataContext as TaskManagerViewModel)?.SetActive(IsVisible && WindowState != WindowState.Minimized);
    }

    protected override void OnClosed(EventArgs e)
    {
        _fullscreenController.Dispose();
        (DataContext as TaskManagerViewModel)?.Dispose();
        base.OnClosed(e);
    }
}
