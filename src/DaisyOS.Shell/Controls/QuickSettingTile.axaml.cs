using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaisyOS.Shell.Controls;

public partial class QuickSettingTile : UserControl
{
    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<QuickSettingTile, string>(nameof(Icon), string.Empty);

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<QuickSettingTile, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<QuickSettingTile, bool>(nameof(IsChecked), false, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> HasDetailsProperty =
        AvaloniaProperty.Register<QuickSettingTile, bool>(nameof(HasDetails), false);

    public static readonly StyledProperty<string> ToolTipTextProperty =
        AvaloniaProperty.Register<QuickSettingTile, string>(nameof(ToolTipText), string.Empty);

    public static readonly StyledProperty<string> DetailsToolTipTextProperty =
        AvaloniaProperty.Register<QuickSettingTile, string>(nameof(DetailsToolTipText), string.Empty);

    public event EventHandler<RoutedEventArgs>? Toggled;
    public event EventHandler<RoutedEventArgs>? DetailsClicked;

    public QuickSettingTile()
    {
        InitializeComponent();
    }

    public string Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public bool HasDetails
    {
        get => GetValue(HasDetailsProperty);
        set => SetValue(HasDetailsProperty, value);
    }

    public string ToolTipText
    {
        get => GetValue(ToolTipTextProperty);
        set => SetValue(ToolTipTextProperty, value);
    }

    public string DetailsToolTipText
    {
        get => GetValue(DetailsToolTipTextProperty);
        set => SetValue(DetailsToolTipTextProperty, value);
    }

    /// <summary>Moves controller/keyboard focus to the primary action.</summary>
    public void FocusPrimary()
    {
        if (HasDetails)
        {
            SplitToggle.Focus();
        }
        else
        {
            SingleToggle.Focus();
        }
    }

    /// <summary>Moves controller/keyboard focus to the optional detail action.</summary>
    public void FocusDetails()
    {
        if (HasDetails)
        {
            DetailsButton.Focus();
        }
    }

    /// <summary>Activates the same state change and routed event as the primary tile action.</summary>
    public void ActivatePrimary()
    {
        IsChecked = !IsChecked;
        Toggled?.Invoke(this, new RoutedEventArgs());
    }

    /// <summary>Activates the optional detail action when one is available.</summary>
    public void ActivateDetails()
    {
        if (HasDetails)
        {
            DetailsClicked?.Invoke(this, new RoutedEventArgs());
        }
    }

    private void OnSingleToggleClick(object? sender, RoutedEventArgs e)
    {
        Toggled?.Invoke(this, e);
    }

    private void OnSplitToggleClick(object? sender, RoutedEventArgs e)
    {
        Toggled?.Invoke(this, e);
    }

    private void OnDetailsButtonClick(object? sender, RoutedEventArgs e)
    {
        DetailsClicked?.Invoke(this, e);
    }
}
