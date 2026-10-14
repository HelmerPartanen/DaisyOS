using System;
using System.Collections.Generic;
using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface INotificationService
{
    event EventHandler? NotificationsChanged;
    event EventHandler<NotificationItem>? NotificationPosted;
    event EventHandler<int>? UnreadCountChanged;
    event EventHandler<bool>? DoNotDisturbChanged;
    event EventHandler<NotificationItem>? NotificationActionTriggered;

    int UnreadCount { get; }
    bool IsDoNotDisturb { get; }

    IReadOnlyList<NotificationItem> GetNotifications();
    void Add(NotificationItem notification);
    void Dismiss(NotificationItem notification);
    void DismissById(string id);
    void Clear();
    void MarkAllAsRead();
    void SetDoNotDisturb(bool enabled);
    void TriggerAction(NotificationItem notification);
    void TriggerAction(NotificationItem notification, string actionId);
}
