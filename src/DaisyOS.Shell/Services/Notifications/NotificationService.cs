using System;
using System.Collections.Generic;
using System.Linq;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;

namespace DaisyOS.Shell.Services.Notifications;

public sealed class NotificationService : INotificationService
{
    private const int MaxNotifications = 50;
    private readonly object _lock = new();
    private readonly List<NotificationItem> _notifications = new();
    private bool _isDoNotDisturb;

    public event EventHandler? NotificationsChanged;
    public event EventHandler<NotificationItem>? NotificationPosted;
    public event EventHandler<int>? UnreadCountChanged;
    public event EventHandler<bool>? DoNotDisturbChanged;
    public event EventHandler<NotificationItem>? NotificationActionTriggered;

    public bool IsDoNotDisturb
    {
        get
        {
            lock (_lock) return _isDoNotDisturb;
        }
    }

    public int UnreadCount
    {
        get
        {
            lock (_lock) return _notifications.Count(n => !n.IsRead);
        }
    }

    public IReadOnlyList<NotificationItem> GetNotifications()
    {
        lock (_lock)
        {
            return _notifications.ToList().AsReadOnly();
        }
    }

    public void Add(NotificationItem notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        bool posted = false;
        int newUnreadCount = 0;

        lock (_lock)
        {
            _notifications.RemoveAll(item => item.Id == notification.Id);
            _notifications.Insert(0, notification);
            while (_notifications.Count > MaxNotifications)
            {
                _notifications.RemoveAt(_notifications.Count - 1);
            }

            if (!_isDoNotDisturb)
            {
                posted = true;
            }

            newUnreadCount = _notifications.Count(n => !n.IsRead);
        }

        NotificationsChanged?.Invoke(this, EventArgs.Empty);
        UnreadCountChanged?.Invoke(this, newUnreadCount);

        if (posted)
        {
            NotificationPosted?.Invoke(this, notification);
        }
    }

    public void Dismiss(NotificationItem notification)
    {
        if (notification == null) return;
        DismissById(notification.Id);
    }

    public void DismissById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        int newUnreadCount = 0;
        bool removed = false;

        lock (_lock)
        {
            int index = _notifications.FindIndex(n => n.Id == id);
            if (index >= 0)
            {
                _notifications.RemoveAt(index);
                removed = true;
                newUnreadCount = _notifications.Count(n => !n.IsRead);
            }
        }

        if (removed)
        {
            NotificationsChanged?.Invoke(this, EventArgs.Empty);
            UnreadCountChanged?.Invoke(this, newUnreadCount);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _notifications.Clear();
        }

        NotificationsChanged?.Invoke(this, EventArgs.Empty);
        UnreadCountChanged?.Invoke(this, 0);
    }

    public void MarkAllAsRead()
    {
        lock (_lock)
        {
            for (int i = 0; i < _notifications.Count; i++)
            {
                if (!_notifications[i].IsRead)
                {
                    _notifications[i] = _notifications[i] with { IsRead = true };
                }
            }
        }

        NotificationsChanged?.Invoke(this, EventArgs.Empty);
        UnreadCountChanged?.Invoke(this, 0);
    }

    public void SetDoNotDisturb(bool enabled)
    {
        lock (_lock)
        {
            if (_isDoNotDisturb == enabled) return;
            _isDoNotDisturb = enabled;
        }

        DoNotDisturbChanged?.Invoke(this, enabled);
    }

    public void TriggerAction(NotificationItem notification)
    {
        if (notification == null) return;

        if (string.IsNullOrWhiteSpace(notification.ActionId))
        {
            NotificationActionTriggered?.Invoke(this, notification);
            Dismiss(notification);
            return;
        }

        TriggerAction(notification, notification.ActionId);
    }

    public void TriggerAction(NotificationItem notification, string actionId)
    {
        if (notification == null || string.IsNullOrWhiteSpace(actionId)) return;

        NotificationActionTriggered?.Invoke(this, notification with { ActionId = actionId });
        Dismiss(notification);
    }
}
