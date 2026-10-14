using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps;

namespace DaisyOS.Shell.Apps.Calculator;

public partial class CalculatorWindow : Window
{
    private readonly CompositorWindowFullscreenController _fullscreenController;

    public CalculatorWindow()
    {
        InitializeComponent();

        DataContext = new CalculatorViewModel();
        _fullscreenController = new CompositorWindowFullscreenController(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _fullscreenController.Dispose();
        (DataContext as CalculatorViewModel)?.Dispose();

        base.OnClosed(e);
    }

    /// <summary>
    /// Removes keyboard focus from text/search controls when
    /// clicking elsewhere in the calculator window.
    /// </summary>
    private void OnWindowPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (e.Source is Visual source &&
            source is not TextBox &&
            source is not Controls.SearchBar &&
            !source.GetVisualAncestors().Any(
                v => v is Controls.SearchBar ||
                     v is TextBox))
        {
            FocusManager?.Focus(null);
        }
    }

}
