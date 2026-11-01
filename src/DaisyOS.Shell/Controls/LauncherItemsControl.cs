using Avalonia.Controls;
using Avalonia.Controls.Presenters;

namespace DaisyOS.Shell.Controls;

/// <summary>Recycles complete launcher tiles instead of rebuilding their templates per page.</summary>
public sealed class LauncherItemsControl : ItemsControl
{
    protected override Type StyleKeyOverride => typeof(ItemsControl);

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) =>
        ItemTemplate?.Build(item) ?? base.CreateContainerForItemOverride(item, index, recycleKey);

    protected override void ClearContainerForItemOverride(Control container)
    {
        if (container is ContentPresenter) base.ClearContainerForItemOverride(container);
        else container.DataContext = null;
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        if (container is ContentPresenter) base.PrepareContainerForItemOverride(container, item, index);
        else container.DataContext = item; // Keep the template-built button content intact.
    }
}
