using System;
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
        _fullscreenController = new SystemAppWindowChrome(this,
            MonitorContent.FindControl<Controls.AppWindowTitleBar>("AppTitleBar")!,
            MonitorContent.FindControl<Grid>("MainPaneLayout")!, normalRowDefinitions: "Auto,*");
    }

    protected override void OnClosed(EventArgs e)
    {
        _fullscreenController.Dispose();
        (DataContext as TaskManagerViewModel)?.Dispose();
        base.OnClosed(e);
    }
}
