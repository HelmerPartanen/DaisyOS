using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using Avalonia.Threading;
using DaisyOS.Shell.Apps;

namespace DaisyOS.Shell.Apps.Calendar;

public partial class CalendarWindow : Window
{
    private readonly CompositorWindowFullscreenController _fullscreenController;

    public CalendarWindow()
    {
        InitializeComponent();
        DataContext = new CalendarViewModel();
        _fullscreenController = new CompositorWindowFullscreenController(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _fullscreenController.Dispose();
        base.OnClosed(e);
        (DataContext as CalendarViewModel)?.Dispose();
    }

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
