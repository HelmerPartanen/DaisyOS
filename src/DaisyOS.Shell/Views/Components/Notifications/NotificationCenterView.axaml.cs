using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DaisyOS.Core.Models;

namespace DaisyOS.Shell.Views.Components.Notifications;

public partial class NotificationCenterView : UserControl
{
    private App? _app;
    public Border MaterialSurface => MaterialFrame;

    public NotificationCenterView()
    {
        InitializeComponent();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && IsVisible) _app?.Notifications.MarkAllAsRead();
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _app = Application.Current as App;
        if (_app is not null)
        {
            _app.Notifications.NotificationsChanged += OnNotificationsStateChanged;
            _app.Notifications.UnreadCountChanged += OnUnreadCountChanged;
            RefreshUI();
            if (IsEffectivelyVisible) _app.Notifications.MarkAllAsRead();
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_app is not null)
        {
            _app.Notifications.NotificationsChanged -= OnNotificationsStateChanged;
            _app.Notifications.UnreadCountChanged -= OnUnreadCountChanged;
        }
    }

    private void OnNotificationsStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshUI);
    }

    private void OnUnreadCountChanged(object? sender, int count)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UnreadCountText.Text = count.ToString();
            UnreadBadge.IsVisible = count > 0;
        });
    }

    private void RefreshUI()
    {
        if (_app is null) return;

        var items = _app.Notifications.GetNotifications();
        NotificationList.ItemsSource = items;
        bool hasItems = items.Count > 0;

        NotificationList.IsVisible = hasItems;
        EmptyStateHost.IsVisible = !hasItems;
        ClearAllButton.IsEnabled = hasItems;

        int unread = _app.Notifications.UnreadCount;
        UnreadCountText.Text = unread.ToString();
        UnreadBadge.IsVisible = unread > 0;
    }

    private void OnClearAllClicked(object? sender, RoutedEventArgs e)
    {
        _app?.Notifications.Clear();
    }

    private void OnDismissCard(object? sender, NotificationItem item) => _app?.Notifications.Dismiss(item);
    private void OnPrimaryCard(object? sender, NotificationItem item) => _app?.Notifications.TriggerAction(item);
    private void OnSecondaryCard(object? sender, NotificationItem item)
    {
        if (item.SecondaryActionId is { } action) _app?.Notifications.TriggerAction(item, action);
    }
}
