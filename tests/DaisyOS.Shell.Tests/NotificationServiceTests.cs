using System;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Notifications;
using Xunit;

namespace DaisyOS.Shell.Tests;

public class NotificationServiceTests
{
    [Fact]
    public void NotificationService_AddNotification_IncreasesUnreadCountAndRaisesEvents()
    {
        var service = new NotificationService();
        NotificationItem? postedItem = null;
        int unreadCount = -1;

        service.NotificationPosted += (_, item) => postedItem = item;
        service.UnreadCountChanged += (_, count) => unreadCount = count;

        var notification = new NotificationItem(
            Title: "Test Alert",
            Body: "This is a test notification.",
            CreatedAt: DateTimeOffset.Now,
            SourceAppId: "TestApp",
            Category: NotificationCategory.System,
            Urgency: NotificationUrgency.Normal
        );

        service.Add(notification);

        Assert.Equal(1, service.UnreadCount);
        Assert.Equal(1, unreadCount);
        Assert.NotNull(postedItem);
        Assert.Equal("Test Alert", postedItem!.Title);
    }

    [Fact]
    public void NotificationService_DismissById_RemovesNotificationAndUpdatesCount()
    {
        var service = new NotificationService();
        var item1 = new NotificationItem("Title 1", "Body 1", DateTimeOffset.Now);
        var item2 = new NotificationItem("Title 2", "Body 2", DateTimeOffset.Now);

        service.Add(item1);
        service.Add(item2);
        Assert.Equal(2, service.UnreadCount);

        service.DismissById(item1.Id);

        Assert.Equal(1, service.UnreadCount);
        var remaining = service.GetNotifications();
        Assert.Single(remaining);
        Assert.Equal(item2.Id, remaining[0].Id);
    }

    [Fact]
    public void NotificationService_Clear_RemovesAllNotifications()
    {
        var service = new NotificationService();
        service.Add(new NotificationItem("Title 1", "Body 1", DateTimeOffset.Now));
        service.Add(new NotificationItem("Title 2", "Body 2", DateTimeOffset.Now));

        service.Clear();

        Assert.Equal(0, service.UnreadCount);
        Assert.Empty(service.GetNotifications());
    }

    [Fact]
    public void NotificationService_MarkAllAsRead_ResetsUnreadCountToZero()
    {
        var service = new NotificationService();
        service.Add(new NotificationItem("Title 1", "Body 1", DateTimeOffset.Now));
        service.Add(new NotificationItem("Title 2", "Body 2", DateTimeOffset.Now));

        Assert.Equal(2, service.UnreadCount);
        service.MarkAllAsRead();

        Assert.Equal(0, service.UnreadCount);
        Assert.Equal(2, service.GetNotifications().Count);
    }

    [Fact]
    public void NotificationService_DoNotDisturb_SuppressesNotificationPostedEvent()
    {
        var service = new NotificationService();
        bool posted = false;
        service.NotificationPosted += (_, _) => posted = true;

        service.SetDoNotDisturb(true);
        Assert.True(service.IsDoNotDisturb);

        service.Add(new NotificationItem("Quiet Title", "Quiet Body", DateTimeOffset.Now));

        Assert.False(posted);
        Assert.Equal(1, service.UnreadCount);
    }

    [Fact]
    public void NotificationService_TriggerAction_RaisesActionTriggeredEventAndDismissesNotification()
    {
        var service = new NotificationService();
        NotificationItem? triggeredItem = null;
        service.NotificationActionTriggered += (_, item) => triggeredItem = item;

        var notification = new NotificationItem("Action Title", "Action Body", DateTimeOffset.Now, ActionLabel: "View");
        service.Add(notification);

        service.TriggerAction(notification);

        Assert.NotNull(triggeredItem);
        Assert.Equal(notification.Id, triggeredItem!.Id);
        Assert.Empty(service.GetNotifications());
    }

    [Fact]
    public void NotificationService_TriggerSecondaryAction_UsesRequestedActionIdAndDismissesNotification()
    {
        var service = new NotificationService();
        NotificationItem? triggeredItem = null;
        service.NotificationActionTriggered += (_, item) => triggeredItem = item;

        var notification = new NotificationItem(
            "Incoming file", "A phone wants to send a file.", DateTimeOffset.Now,
            ActionLabel: "Accept", ActionId: "localsend-accept:1",
            SecondaryActionLabel: "Decline", SecondaryActionId: "localsend-decline:1");
        service.Add(notification);

        service.TriggerAction(notification, notification.SecondaryActionId!);

        Assert.NotNull(triggeredItem);
        Assert.Equal("localsend-decline:1", triggeredItem!.ActionId);
        Assert.Empty(service.GetNotifications());
    }
    [Fact]
    public void ReplacementRetainsOnlyOneNotificationAndUpdatesUnreadState()
    {
        var service = new NotificationService();
        var item = new NotificationItem("Before", "Body", DateTimeOffset.Now, Id: "same");
        service.Add(item);
        service.MarkAllAsRead();
        service.Add(item with { Title = "After" });
        Assert.Equal("After", Assert.Single(service.GetNotifications()).Title);
        Assert.Equal(1, service.UnreadCount);
    }
}
