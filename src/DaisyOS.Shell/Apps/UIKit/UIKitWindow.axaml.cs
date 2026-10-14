using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DaisyOS.Shell.Apps.UIKit;

public partial class UIKitWindow : Window
{
    public UIKitWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            CatalogRoot.Focus();
            Dispatcher.UIThread.Post(() => CatalogScroll.Offset = default, DispatcherPriority.Background);
        }, DispatcherPriority.Background);
    }
}
