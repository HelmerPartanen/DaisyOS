using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace DaisyOS.Shell.Apps.Calculator;

public partial class CalculatorView : UserControl
{
    private CalculatorViewModel? ViewModel => DataContext as CalculatorViewModel;

    public CalculatorView()
    {
        InitializeComponent();

        AddHandler(KeyDownEvent, OnViewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm) return;

        switch (e.Key)
        {
            case Key.D0 or Key.NumPad0: vm.InputDigit("0"); e.Handled = true; break;
            case Key.D1 or Key.NumPad1: vm.InputDigit("1"); e.Handled = true; break;
            case Key.D2 or Key.NumPad2: vm.InputDigit("2"); e.Handled = true; break;
            case Key.D3 or Key.NumPad3: vm.InputDigit("3"); e.Handled = true; break;
            case Key.D4 or Key.NumPad4: vm.InputDigit("4"); e.Handled = true; break;
            case Key.D5 or Key.NumPad5: vm.InputDigit("5"); e.Handled = true; break;
            case Key.D6 or Key.NumPad6: vm.InputDigit("6"); e.Handled = true; break;
            case Key.D7 or Key.NumPad7: vm.InputDigit("7"); e.Handled = true; break;
            case Key.D8 or Key.NumPad8: vm.InputDigit("8"); e.Handled = true; break;
            case Key.D9 or Key.NumPad9: vm.InputDigit("9"); e.Handled = true; break;
            case Key.Decimal or Key.OemPeriod: vm.InputDigit("."); e.Handled = true; break;

            case Key.Add or Key.OemPlus when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
            case Key.Add:
                vm.InputOperator("+"); e.Handled = true; break;

            case Key.Subtract or Key.OemMinus:
                vm.InputOperator("-"); e.Handled = true; break;

            case Key.Multiply:
                vm.InputOperator("×"); e.Handled = true; break;

            case Key.Divide or Key.OemQuestion:
                vm.InputOperator("÷"); e.Handled = true; break;

            case Key.Enter or Key.Return:
                vm.ExecuteEquals(); e.Handled = true; break;

            case Key.Back:
                vm.Backspace(); e.Handled = true; break;

            case Key.Escape:
                vm.AllClear(); e.Handled = true; break;

            case Key.Delete:
                vm.ClearEntry(); e.Handled = true; break;
        }
    }

    private async void OnToggleModeClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || !ModeToggleButton.IsEnabled)
        {
            return;
        }

        ModeToggleButton.IsEnabled = false;
        ModeToggleButton.Opacity = 0;
        ModeTitleText.Opacity = 0;

        await Task.Delay(120);
        viewModel.ToggleMode();

        ModeToggleButton.Opacity = 1;
        ModeTitleText.Opacity = 1;
        ModeToggleButton.IsEnabled = true;
    }

    private void OnDigitClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string digit })
        {
            ViewModel?.InputDigit(digit);
        }
    }

    private void OnOperatorClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string op })
        {
            ViewModel?.InputOperator(op);
        }
    }

    private void OnEqualsClicked(object? sender, RoutedEventArgs e) => ViewModel?.ExecuteEquals();

    private void OnClearClicked(object? sender, RoutedEventArgs e) => ViewModel?.ClearEntry();

    private void OnAllClearClicked(object? sender, RoutedEventArgs e) => ViewModel?.AllClear();

    private void OnBackspaceClicked(object? sender, RoutedEventArgs e) => ViewModel?.Backspace();

    private void OnToggleSignClicked(object? sender, RoutedEventArgs e) => ViewModel?.ToggleSign();

    private void OnPercentClicked(object? sender, RoutedEventArgs e) => ViewModel?.CalculatePercentage();

    private void OnInvClicked(object? sender, RoutedEventArgs e) => ViewModel?.ToggleInverse();

    private void OnHypClicked(object? sender, RoutedEventArgs e) => ViewModel?.ToggleHyp();

    private void OnModeClicked(object? sender, RoutedEventArgs e) => ViewModel?.CycleAngleUnit();

    private void OnSinClicked(object? sender, RoutedEventArgs e) => ViewModel?.ApplyScientificFunction("sin");

    private void OnCosClicked(object? sender, RoutedEventArgs e) => ViewModel?.ApplyScientificFunction("cos");

    private void OnTanClicked(object? sender, RoutedEventArgs e) => ViewModel?.ApplyScientificFunction("tan");

    private void OnSqrtClicked(object? sender, RoutedEventArgs e) => ViewModel?.ApplyScientificFunction("sqrt");

    private void OnSqrClicked(object? sender, RoutedEventArgs e) => ViewModel?.ApplyScientificFunction("sqr");

    private void OnLogClicked(object? sender, RoutedEventArgs e) => ViewModel?.ApplyScientificFunction("log");

    private void OnLnClicked(object? sender, RoutedEventArgs e) => ViewModel?.ApplyScientificFunction("ln");

    private void OnRecipClicked(object? sender, RoutedEventArgs e) => ViewModel?.ApplyScientificFunction("recip");

    private void OnFactClicked(object? sender, RoutedEventArgs e) => ViewModel?.ApplyScientificFunction("fact");

    private void OnPiClicked(object? sender, RoutedEventArgs e) => ViewModel?.ApplyScientificFunction("pi");

    private void OnMemoryClearClicked(object? sender, RoutedEventArgs e) => ViewModel?.MemoryClear();

    private void OnMemoryRecallClicked(object? sender, RoutedEventArgs e) => ViewModel?.MemoryRecall();

    private void OnMemoryAddClicked(object? sender, RoutedEventArgs e) => ViewModel?.MemoryAdd();

    private void OnMemorySubtractClicked(object? sender, RoutedEventArgs e) => ViewModel?.MemorySubtract();

    private void OnMemoryStoreClicked(object? sender, RoutedEventArgs e) => ViewModel?.MemoryStore();
}
