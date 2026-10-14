using System;
using Avalonia.Controls;
using DaisyOS.Shell.Apps;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

public partial class TaskManagerWindow : Window
{
    private readonly CompositorWindowFullscreenController _fullscreenController;

    public TaskManagerWindow()
    {
        InitializeComponent();

        DataContext = new TaskManagerViewModel();
        _fullscreenController = new CompositorWindowFullscreenController(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _fullscreenController.Dispose();
        (DataContext as TaskManagerViewModel)?.Dispose();
        base.OnClosed(e);
    }
}
