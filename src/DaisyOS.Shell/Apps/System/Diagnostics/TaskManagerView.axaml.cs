using Avalonia.Controls;
using Avalonia.Interactivity;
using DaisyOS.Shell.Views.Dialogs;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

public partial class TaskManagerView : UserControl
{
    public TaskManagerView() => InitializeComponent();
    private void OnNavigate(object? sender, RoutedEventArgs e) { if (DataContext is TaskManagerViewModel vm && sender is Button { Tag: string tag } && int.TryParse(tag, out var section)) vm.Navigate(section); }
    private void OnPause(object? sender, RoutedEventArgs e) { if (DataContext is TaskManagerViewModel vm) vm.TogglePause(); }
    private async void OnRefresh(object? sender, RoutedEventArgs e) { if (DataContext is TaskManagerViewModel vm) await vm.RefreshAsync(); }
    private async void OnEndSelectedProcessClicked(object? sender, RoutedEventArgs e) => await EndSelectedAsync(false);
    private async void OnForceQuit(object? sender, RoutedEventArgs e) => await EndSelectedAsync(true);
    private async Task EndSelectedAsync(bool force)
    {
        if (DataContext is not TaskManagerViewModel vm || vm.SelectedProcess is not { } process || !vm.CanEnd) return;
        if (process.WindowId is null)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var dialog = new ConfirmationDialog(force ? "Force quit process" : "End process", $"{(force ? "Force quit" : "End")} {process.Name}?",
                $"PID {process.Pid}. {(force ? "The process will stop immediately." : "A termination request will be sent.")} Unsaved work may be lost.", force ? "Force quit" : "End process", "Cancel");
            if (!await dialog.ShowDialog<bool>(owner)) return;
        }
        await vm.EndProcessAsync(process, force);
    }
}
