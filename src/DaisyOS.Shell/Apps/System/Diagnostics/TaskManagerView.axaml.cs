using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

public partial class TaskManagerView : UserControl
{
    public TaskManagerView()
    {
        InitializeComponent();
    }

    private async void OnEndSelectedProcessClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TaskManagerViewModel vm && vm.SelectedProcess is { } process)
        {
            await vm.EndProcessAsync(process.Pid);
        }
    }

    private async void OnEndProcessItemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int pid } && DataContext is TaskManagerViewModel vm)
        {
            await vm.EndProcessAsync(pid);
        }
    }
}
