using Avalonia.Controls;

namespace DaisyOS.Shell.Views.Components.Notifications;

public partial class NotificationToastView : UserControl
{
    public NotificationCard NotificationCard => Card;
    public NotificationToastView() => InitializeComponent();
}
