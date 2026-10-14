using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaisyOS.Shell.Controls;

public partial class FlyoutPageHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<FlyoutPageHeader, string>(nameof(Title), string.Empty);

    public event EventHandler<RoutedEventArgs>? BackClicked;

    public FlyoutPageHeader()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private void OnBackButtonClick(object? sender, RoutedEventArgs e)
    {
        BackClicked?.Invoke(this, e);
    }
}
