using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Motion;

namespace DaisyOS.Shell.Views.Components.Notifications;

public partial class NotificationToastHost : UserControl
{
    public static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(5);
    private const int MaximumVisible = 3;
    private sealed class Toast(NotificationItem item, NotificationToastView view)
    {
        public NotificationItem Item = item;
        public readonly NotificationToastView View = view;
        public DispatcherTimer? Timer;
        public bool Closing;
        public int Revision;
    }
    private readonly List<Toast> _toasts = [];
    private readonly List<NotificationItem> _pending = [];
    private App? _app;
    public event EventHandler<bool>? ActiveToastVisibilityChanged;
    public bool HasActiveToasts => _toasts.Any(toast => !toast.Closing);

    public NotificationToastHost()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _app = Application.Current as App;
        if (_app is null) return;
        _app.Notifications.NotificationPosted += OnNotificationPosted;
        _app.Notifications.NotificationsChanged += OnNotificationsChanged;
        _app.Notifications.DoNotDisturbChanged += OnDoNotDisturbChanged;
    }
    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_app is not null)
        {
            _app.Notifications.NotificationPosted -= OnNotificationPosted;
            _app.Notifications.NotificationsChanged -= OnNotificationsChanged;
            _app.Notifications.DoNotDisturbChanged -= OnDoNotDisturbChanged;
        }
        _pending.Clear();
        foreach (var toast in _toasts)
        {
            toast.Timer?.Stop();
            GuiMotion.Cancel(toast.View);
        }
        _toasts.Clear();
        ToastList.Children.Clear();
        RaiseVisibilityChanged();
        _app = null;
    }
    private void OnNotificationPosted(object? sender, NotificationItem item) => Dispatcher.UIThread.Post(() => Present(item));
    private void OnNotificationsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_app is null) return;
        var ids = _app.Notifications.GetNotifications().Select(item => item.Id).ToHashSet();
        _pending.RemoveAll(item => !ids.Contains(item.Id));
        foreach (var toast in _toasts.ToArray())
            if (!ids.Contains(toast.Item.Id)) _ = RemoveAsync(toast);
    });
    private void OnDoNotDisturbChanged(object? sender, bool enabled) => Dispatcher.UIThread.Post(() =>
    {
        if (!enabled) return;
        _pending.Clear();
        foreach (var toast in _toasts.ToArray()) _ = RemoveAsync(toast);
    });

    internal bool Present(NotificationItem item)
    {
        var existing = _toasts.FirstOrDefault(toast => toast.Item.Id == item.Id);
        if (existing is not null)
        {
            existing.Timer?.Stop();
            existing.Item = item;
            existing.View.DataContext = item;
            var wasClosing = existing.Closing;
            existing.Closing = false;
            existing.View.IsHitTestVisible = true;
            if (wasClosing) _ = RevealAndTimeAsync(existing);
            else { existing.Revision++; StartTimer(existing); }
            RaiseVisibilityChanged();
            return true;
        }
        _pending.RemoveAll(pending => pending.Id == item.Id);
        var view = new NotificationToastView { DataContext = item };
        ToastList.Children.Add(view); // Resolve inherited styles/resources before measuring text and images.
        view.Measure(new Size(ToastList.Width, double.PositiveInfinity));
        var availableHeight = (TopLevel.GetTopLevel(this)?.ClientSize.Height ?? 460) - Margin.Top - Margin.Bottom - 12;
        var occupiedHeight = _toasts.Sum(toast => toast.View.DesiredSize.Height) + ToastList.Spacing * _toasts.Count;
        if (_toasts.Count >= MaximumVisible || (_toasts.Count > 0 && occupiedHeight + view.DesiredSize.Height > availableHeight))
        {
            ToastList.Children.Remove(view);
            _pending.Add(item);
            return false;
        }
        var toast = new Toast(item, view);
        view.NotificationCard.DismissRequested += (_, notification) =>
        {
            _ = RemoveAsync(toast);
            _app?.Notifications.Dismiss(notification);
        };
        view.NotificationCard.PrimaryActionRequested += (_, notification) =>
        {
            _ = RemoveAsync(toast);
            _app?.Notifications.TriggerAction(notification);
        };
        view.NotificationCard.SecondaryActionRequested += (_, notification) =>
        {
            _ = RemoveAsync(toast);
            if (notification.SecondaryActionId is { } action) _app?.Notifications.TriggerAction(notification, action);
        };
        _toasts.Add(toast);
        // Existing material cards retain their position on arrival.
        RaiseVisibilityChanged();
        _ = RevealAndTimeAsync(toast);
        return true;
    }
    private async Task RevealAndTimeAsync(Toast toast)
    {
        var revision = ++toast.Revision;
        await GuiMotion.SetVisibleAsync(toast.View, toast.View, true, changed: null, immediate: false,
            offset: new Point(Math.Max(GuiMotion.ShellSlideDistance, toast.View.Bounds.Width + 24), 0),
            durationOverride: GuiMotion.ShellSlideDuration(this, true));
        if (toast.Closing || revision != toast.Revision || !_toasts.Contains(toast)) return;
        StartTimer(toast);
    }
    private void StartTimer(Toast toast)
    {
        var timer = new DispatcherTimer { Interval = DisplayDuration };
        timer.Tick += (_, _) => { timer.Stop(); _ = RemoveAsync(toast); };
        toast.Timer = timer;
        timer.Start();
    }
    private async Task RemoveAsync(Toast toast)
    {
        if (toast.Closing || !_toasts.Contains(toast)) return;
        toast.Closing = true;
        var revision = ++toast.Revision;
        toast.Timer?.Stop();
        toast.View.IsHitTestVisible = false;
        RaiseVisibilityChanged(); // Last dismissal releases native input before its visual exit.
        await GuiMotion.SetVisibleAsync(toast.View, toast.View, false, changed: null, immediate: false,
            offset: new Point(Math.Max(GuiMotion.ShellSlideDistance, toast.View.Bounds.Width + 24), 0),
            durationOverride: GuiMotion.ShellSlideDuration(this, false));
        if (revision != toast.Revision || !_toasts.Remove(toast)) return;
        ToastList.Children.Remove(toast.View);
        RaiseVisibilityChanged();
        while (_pending.Count > 0 && _toasts.Count < MaximumVisible)
        {
            var next = _pending[0];
            _pending.RemoveAt(0);
            if (!Present(next))
            {
                _pending.RemoveAll(item => item.Id == next.Id);
                _pending.Insert(0, next);
                break;
            }
        }
    }
    private void RaiseVisibilityChanged() => ActiveToastVisibilityChanged?.Invoke(this, HasActiveToasts);
}
