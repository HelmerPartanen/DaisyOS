using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaisyOS.Installer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is IDisposable disposable) disposable.Dispose();
        DataContext = null;
        Closed -= OnClosed;
    }
}
