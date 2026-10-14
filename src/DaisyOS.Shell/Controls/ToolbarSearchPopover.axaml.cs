using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace DaisyOS.Shell.Controls;

public partial class ToolbarSearchPopover : UserControl
{
    private Button? _openButton;
    private Button? _clearButton;
    private Popup? _popup;
    private TextBox? _input;
    private bool _syncingText;

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<ToolbarSearchPopover, string>(nameof(Text), string.Empty, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<ToolbarSearchPopover, string>(nameof(PlaceholderText), "Search");
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ToolbarSearchPopover, bool>(nameof(IsOpen));

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string PlaceholderText { get => GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }
    public bool IsOpen { get => GetValue(IsOpenProperty); private set => SetValue(IsOpenProperty, value); }

    static ToolbarSearchPopover()
    {
        TextProperty.Changed.AddClassHandler<ToolbarSearchPopover>((control, _) => control.SyncTextToInput());
        PlaceholderTextProperty.Changed.AddClassHandler<ToolbarSearchPopover>((control, _) => control.SyncPlaceholder());
    }

    public ToolbarSearchPopover()
    {
        InitializeComponent();
        _openButton = this.FindControl<Button>("OpenButton");
        _clearButton = this.FindControl<Button>("ClearButton");
        _popup = this.FindControl<Popup>("SearchPopup");
        _input = this.FindControl<TextBox>("SearchInput");

        if (_openButton is not null) _openButton.Click += (_, _) => Open();
        if (_clearButton is not null) _clearButton.Click += (_, _) => Clear();
        if (_popup is not null) _popup.Closed += (_, _) => SetOpenState(false);
        if (_input is not null)
        {
            _input.TextChanged += (_, _) => SyncTextFromInput();
            _input.KeyDown += OnInputKeyDown;
        }
        SyncPlaceholder();
        SyncTextToInput();
    }

    public void Open()
    {
        if (_popup is null) return;
        _popup.IsOpen = true;
        SetOpenState(true);
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsOpen) return;
            _input?.Focus();
            _input?.SelectAll();
        }, DispatcherPriority.Input);
    }

    public void Close(bool clear = true, bool restoreFocus = true)
    {
        var wasOpen = IsOpen;
        if (clear) Text = string.Empty;
        if (_popup is not null) _popup.IsOpen = false;
        SetOpenState(false);
        if (wasOpen && restoreFocus) _openButton?.Focus();
    }

    private void Clear()
    {
        Text = string.Empty;
        _input?.Focus();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close(clear: true);
    }

    private void SyncTextFromInput()
    {
        if (_syncingText || _input is null) return;
        _syncingText = true;
        Text = _input.Text ?? string.Empty;
        _syncingText = false;
        UpdateClearButton();
        UpdateOpenButton();
    }

    private void SyncTextToInput()
    {
        if (_syncingText || _input is null) return;
        _syncingText = true;
        _input.Text = Text;
        _syncingText = false;
        UpdateClearButton();
        UpdateOpenButton();
    }

    private void SyncPlaceholder()
    {
        if (_input is not null) _input.PlaceholderText = PlaceholderText;
        if (_openButton is not null) ToolTip.SetTip(_openButton, PlaceholderText);
    }

    private void UpdateClearButton()
    {
        if (_clearButton is not null) _clearButton.IsVisible = !string.IsNullOrEmpty(Text);
    }

    private void SetOpenState(bool isOpen)
    {
        IsOpen = isOpen;
        UpdateOpenButton();
    }

    private void UpdateOpenButton()
    {
        _openButton?.Classes.Set("Active", IsOpen || !string.IsNullOrEmpty(Text));
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
