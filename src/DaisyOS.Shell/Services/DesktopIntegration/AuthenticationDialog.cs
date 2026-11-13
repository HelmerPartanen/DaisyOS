using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using DaisyOS.Shell.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Theming;

namespace DaisyOS.Shell.Services.DesktopIntegration;

/// <summary>A single-account PolicyKit conversation, with no ordinary window chrome.</summary>
internal sealed class AuthenticationDialog : Window
{
    internal SearchBar PasswordField { get; } = new() { ShowSearchIcon = false, ShowClearButton = false, PlaceholderText = "" };
    internal TextBox Response { get; }
    internal Button Submit { get; } = new() { Name = "AuthenticationSubmit", Content = Loc.Get("Submit"), Classes = { "Primary", "Small" }, IsEnabled = false, IsDefault = true };
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = false };
    private readonly TextBlock _prompt = new() { Text = Loc.Get("Password"), TextWrapping = TextWrapping.Wrap };
    private readonly Action<string, string> _send;
    private bool _completed;

    internal AuthenticationDialog(string message, string action, string account, Action<string, string> send, string program = "")
    {
        _send = send;
        Response = PasswordField.FindControl<TextBox>("SearchInput")!;
        // Keep secrets in the password textbox only, not SearchBar's query property.
        Avalonia.Data.BindingOperations.GetBindingExpressionBase(Response, TextBox.TextProperty)?.Dispose();
        Response.ClearValue(TextBox.TextProperty);
        Response.PasswordChar = '●';
        Response.IsEnabled = false;
        Title = Loc.Get("Authentication required");
        Width = 440;
        MaxHeight = 700;
        CanResize = CanMinimize = CanMaximize = false;
        ShowInTaskbar = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var title = TypographyPolicy.Bind(new TextBlock { Text = Title, FontWeight = FontWeight.SemiBold }, "Title");
        title.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        var summary = new TextBlock { Text = string.IsNullOrWhiteSpace(program) ? message : Loc.Format("Allow {0} to make changes?", global::System.IO.Path.GetFileName(program)), TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
        summary.Bind(TextBlock.ForegroundProperty, summary.GetResourceObservable("ContentSecondaryBrush"));
        var identity = new TextBlock { Text = account, FontWeight = FontWeight.Medium };
        var field = new StackPanel { Spacing = 8, Children = { identity, _prompt, PasswordField, Status } };
        var detailsContent = new ScrollViewer
        {
            Name = "AuthenticationDetails", IsVisible = false, MaxHeight = 180,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = TypographyPolicy.Bind(new TextBlock { Text = message + "\n\n" + action, TextWrapping = TextWrapping.Wrap }, "Caption")
        };
        var detailsIcon = new DaisyIcon { Icon = "caret-down", Size = 12 };
        var details = new ToggleButton
        {
            Name = "AuthenticationDetailsToggle", Classes = { "Ghost", "Small" },
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { new TextBlock { Text = Loc.Get("Details"), VerticalAlignment = VerticalAlignment.Center }, detailsIcon } }
        };
        details.PropertyChanged += (_, change) =>
        {
            if (change.Property != ToggleButton.IsCheckedProperty) return;
            detailsContent.IsVisible = details.IsChecked == true;
            detailsIcon.Icon = detailsContent.IsVisible ? "caret-up" : "caret-down";
        };
        var cancel = new Button { Content = Loc.Get("Cancel"), Classes = { "Secondary", "Small" }, IsCancel = true };
        cancel.Click += (_, _) => Close();
        Submit.Click += (_, _) =>
        {
            if (!Response.IsEnabled || _completed) return;
            var value = Response.Text ?? "";
            Response.Text = "";
            ShowStatus("");
            Response.IsEnabled = Submit.IsEnabled = false;
            _send("response", value);
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancel, Submit } };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        footer.Children.Add(details); Grid.SetColumn(actions, 2); footer.Children.Add(actions);
        var surface = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(24),
            Child = new StackPanel { Spacing = 18, Children = { title, summary, field, detailsContent, footer } } };
        surface.Bind(Border.BackgroundProperty, surface.GetResourceObservable("SurfaceBaseSolidBrush"));
        surface.Bind(Border.BorderBrushProperty, surface.GetResourceObservable("ShellSurfaceInnerBorderBrush"));
        Content = surface;
        Closed += (_, _) => { Response.Text = ""; if (!_completed) { _completed = true; _send("cancel", ""); } };
    }

    internal void Prompt(string text, bool echo)
    {
        _prompt.Text = text;
        Avalonia.Automation.AutomationProperties.SetName(Response, text);
        Response.Text = "";
        Response.PasswordChar = echo ? '\0' : '●';
        Response.IsEnabled = Submit.IsEnabled = true;
        Dispatcher.UIThread.Post(() => { if (IsVisible && Response.IsEnabled) Response.Focus(); }, DispatcherPriority.Input);
    }

    internal void ShowStatus(string text, bool error = false)
    {
        Status.Bind(TextBlock.ForegroundProperty, Status.GetResourceObservable(error ? "StatusDangerBrush" : "ContentSecondaryBrush"));
        Status.Text = text;
        Status.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    internal void Complete(bool success, bool cancelled = false)
    {
        _completed = true;
        Response.Text = "";
        Response.IsEnabled = Submit.IsEnabled = false;
        if (success || cancelled) Close();
        else ShowStatus(Loc.Get("Authentication failed. Please try the action again."), true);
    }
}
