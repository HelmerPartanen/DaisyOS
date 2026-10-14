using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaisyOS.Shell.Controls;

public partial class ShellListItem : UserControl
{
    public static readonly StyledProperty<string> IconGlyphProperty =
        AvaloniaProperty.Register<ShellListItem, string>(nameof(IconGlyph), string.Empty);

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ShellListItem, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<ShellListItem, string>(nameof(Subtitle), string.Empty);

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<ShellListItem, bool>(nameof(IsSelected), false);

    public static readonly StyledProperty<object?> RightContentProperty =
        AvaloniaProperty.Register<ShellListItem, object?>(nameof(RightContent));

    public event EventHandler<RoutedEventArgs>? ItemClicked;

    public ShellListItem()
    {
        InitializeComponent();
    }

    public string IconGlyph
    {
        get => GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        ItemClicked?.Invoke(this, e);
    }
}
