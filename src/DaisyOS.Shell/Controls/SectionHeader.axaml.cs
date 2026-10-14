using Avalonia;
using Avalonia.Controls;

namespace DaisyOS.Shell.Controls;

public partial class SectionHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SectionHeader, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<SectionHeader, object?>(nameof(ActionContent));

    public SectionHeader()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }
}
