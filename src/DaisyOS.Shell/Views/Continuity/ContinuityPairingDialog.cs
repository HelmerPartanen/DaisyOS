using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DaisyOS.Core.Continuity;
using DaisyOS.Shell.Services.Localization;

namespace DaisyOS.Shell.Views.Continuity;

internal sealed class ContinuityPairingDialog : Window
{
    private readonly TextBox _input;
    private readonly TextBlock[] _digits;
    private readonly TextBlock _status;
    private readonly Button _action;
    private readonly bool _receiving;
    private bool _finished, _submitted;
    internal string Code => _input.Text ?? "";
    internal bool Finished => _finished;
    internal event Action<string>? Submitted;
    internal event Action? Cancelled;

    public ContinuityPairingDialog(ContinuityPrompt prompt)
    {
        _receiving = prompt.Kind == "pair-display";
        Title = Loc.Format("Pair with {0}", prompt.Name);
        Width = 480; Height = 350;
        CanResize = CanMinimize = CanMaximize = false;
        ShowInTaskbar = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var title = new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var hint = new TextBlock { Text = Loc.Get(_receiving ? "Enter this code on the other computer." : "Enter the code shown on the other computer."),
            FontSize = 14, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        hint.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentSecondaryBrush"));
        var numbers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        _digits = Enumerable.Range(0, 6).Select(i => new TextBlock { Text = _receiving && prompt.Detail.Length == 6 ? prompt.Detail[i].ToString() : "_",
            FontSize = 42, Width = 32, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center }).ToArray();
        _digits[2].Margin = new Thickness(0, 0, 8, 0);
        foreach (var digit in _digits) numbers.Children.Add(digit);
        _input = new TextBox { Name = "PairingCodeInput", MaxLength = 32, Opacity = 0, MinWidth = 300, Height = 70,
            HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = !_receiving };
        Avalonia.Automation.AutomationProperties.SetName(_input, Loc.Get("Six-digit pairing code"));
        var codeArea = new Grid { Height = 82, VerticalAlignment = VerticalAlignment.Center, Children = { numbers, _input } };
        _status = new TextBlock { Text = Loc.Get(_receiving ? "Waiting for the other computer…" : "Pairing enables all Continuity features."),
            FontSize = 13, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        _status.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentSecondaryBrush"));
        _action = new Button { Content = Loc.Get(_receiving ? "Done" : "Cancel"),
            HorizontalAlignment = HorizontalAlignment.Center, Classes = { _receiving ? "Primary" : "Subtle", "Small", "Flexible" } };
        var footer = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Children = { _action } };
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(32, 28), RowSpacing = 10 };
        body.Children.Add(title); Grid.SetRow(hint, 1); body.Children.Add(hint);
        Grid.SetRow(codeArea, 2); body.Children.Add(codeArea);
        Grid.SetRow(_status, 3); body.Children.Add(_status);
        Grid.SetRow(footer, 4); body.Children.Add(footer);
        var frame = new Border { CornerRadius = new CornerRadius(36), BorderThickness = new Thickness(1), Child = body };
        frame.Bind(Border.BackgroundProperty, this.GetResourceObservable("SurfaceBaseSolidBrush"));
        frame.Bind(Border.BorderBrushProperty, this.GetResourceObservable("ShellSurfaceInnerBorderBrush"));
        this.Bind(ForegroundProperty, this.GetResourceObservable("ContentPrimaryBrush"));
        Content = frame;
        _input.TextChanged += (_, _) => UpdateDigits();
        _input.PropertyChanged += (_, e) => { if (e.Property == TextBox.CaretIndexProperty) UpdateCaret(); };
        _input.GotFocus += (_, _) => UpdateCaret();
        _input.LostFocus += (_, _) => UpdateCaret();
        _action.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } else if (e.Key == Key.Enter) { Confirm(); e.Handled = true; } };
        Opened += (_, _) => { if (!_receiving) _input.Focus(); };
        Closed += (_, _) => { if (!_finished && !_receiving) Cancelled?.Invoke(); };
    }

    private void UpdateDigits()
    {
        var text = new string((_input.Text ?? "").Where(char.IsAsciiDigit).Take(6).ToArray());
        if (_input.Text != text) { _input.Text = text; _input.CaretIndex = text.Length; }
        for (var i = 0; i < 6; i++) _digits[i].Text = i < text.Length ? text[i].ToString() : "_";
        UpdateCaret();
        if (text.Length == 6) Confirm();
    }

    private void UpdateCaret()
    {
        for (var i = 0; i < _digits.Length; i++)
            _digits[i].Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(
                !_receiving && _input.IsFocused && !_submitted && i == Math.Min(_input.CaretIndex, 5) ? "ActionPrimaryBrush" : "ContentPrimaryBrush"));
    }

    private void Confirm()
    {
        if (_finished || _receiving) { Close(); return; }
        if (_receiving || _submitted || Code.Length != 6) return;
        _submitted = true; _input.IsEnabled = false;
        _status.Text = Loc.Get("Pairing…");
        Submitted?.Invoke(Code);
    }

    internal void Complete(bool paired, string? detail = null)
    {
        if (_finished) return;
        _finished = true;
        _input.IsEnabled = false;
        _status.Text = paired ? Loc.Get("Paired. You’re ready to share.") : Loc.Get(detail ?? "Pairing ended. Check the code and try again.");
        if (!_receiving && paired) Close();
        else _action.Focus();
    }
}
