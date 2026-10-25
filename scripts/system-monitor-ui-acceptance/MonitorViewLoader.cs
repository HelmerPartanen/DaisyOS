global using Loc = DaisyOS.Shell.Services.Localization.Loc;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

// This harness compiles views alongside the existing shell assembly, which also
// contains their old types. Explicit loaders avoid ambiguous generator lookup.
namespace DaisyOS.Shell.Apps.System.Diagnostics;
public partial class TaskManagerView
{
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
public partial class TaskManagerWindow
{
    private TaskManagerView MonitorContent => this.FindControl<TaskManagerView>("MonitorContent")!;
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
