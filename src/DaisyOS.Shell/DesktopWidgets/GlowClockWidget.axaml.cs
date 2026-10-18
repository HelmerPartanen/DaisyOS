using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DaisyOS.Shell.Controls;

namespace DaisyOS.Shell.DesktopWidgets;

public partial class GlowClockWidget : UserControl
{
    public GlowClockWidget() => AvaloniaXamlLoader.Load(this);

    public void SetGridSize(int columnSpan, int rowSpan)
    {
        var container = this.FindControl<WidgetContainer>("Container")!;
        container.ColumnSpan = columnSpan;
        container.RowSpan = rowSpan;
    }
}

