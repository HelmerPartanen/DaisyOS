using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace DaisyOS.Shell.Controls;

public partial class SearchBar : UserControl
{
    public static new readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<SearchBar, CornerRadius>(nameof(CornerRadius), new CornerRadius(999));

    public static readonly StyledProperty<bool> ShowSearchIconProperty =
        AvaloniaProperty.Register<SearchBar, bool>(nameof(ShowSearchIcon), true);
    public static readonly StyledProperty<bool> ShowClearButtonProperty =
        AvaloniaProperty.Register<SearchBar, bool>(nameof(ShowClearButton), true);

    public bool ShowSearchIcon { get => GetValue(ShowSearchIconProperty); set => SetValue(ShowSearchIconProperty, value); }
    public bool ShowClearButton { get => GetValue(ShowClearButtonProperty); set => SetValue(ShowClearButtonProperty, value); }

    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<SearchBar, string>(nameof(PlaceholderText), "Search");

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SearchBar, string>(nameof(Text), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    public new CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public string PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    static SearchBar()
    {
        TextProperty.Changed.AddClassHandler<SearchBar>((bar, _) => bar.UpdateClearVisibility());
        ShowClearButtonProperty.Changed.AddClassHandler<SearchBar>((bar, _) => bar.UpdateClearVisibility());
    }

    public SearchBar()
    {
        InitializeComponent();
        var clear = this.FindControl<Button>("ClearButton");
        if (clear is not null)
        {
            clear.Click += (_, _) =>
            {
                Text = string.Empty;
                this.FindControl<TextBox>("SearchInput")?.Focus();
            };
        }
        UpdateClearVisibility();

        var input = this.FindControl<TextBox>("SearchInput");
        if (input is not null)
        {
            PointerPressed += (_, _) => input.Focus();
        }
    }

    /// <summary>Focuses the search input and selects the current query.</summary>
    public void FocusInput()
    {
        var input = this.FindControl<TextBox>("SearchInput");
        if (input is not null)
        {
            input.Focus();
            input.SelectAll();
        }
    }

    private void UpdateClearVisibility()
    {
        if (this.FindControl<Button>("ClearButton") is { } clear)
            clear.IsVisible = ShowClearButton && !string.IsNullOrEmpty(Text);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
