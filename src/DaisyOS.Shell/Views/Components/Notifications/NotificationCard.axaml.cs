using Avalonia.Controls;
using Avalonia.Interactivity;
using DaisyOS.Core.Models;

namespace DaisyOS.Shell.Views.Components.Notifications;

public partial class NotificationCard : UserControl
{
    public event EventHandler<NotificationItem>? DismissRequested;
    public event EventHandler<NotificationItem>? PrimaryActionRequested;
    public event EventHandler<NotificationItem>? SecondaryActionRequested;
    public Control AnimatedContent => MotionContent;

    public NotificationCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Grid.SetColumnSpan(PrimaryAction,
            DataContext is NotificationItem item && !string.IsNullOrWhiteSpace(item.SecondaryActionLabel) ? 1 : 2);
    }
    private void OnDismissClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationItem item) DismissRequested?.Invoke(this, item);
    }
    private void OnPrimaryClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationItem item) PrimaryActionRequested?.Invoke(this, item);
    }
    private void OnSecondaryClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationItem item) SecondaryActionRequested?.Invoke(this, item);
    }
}
