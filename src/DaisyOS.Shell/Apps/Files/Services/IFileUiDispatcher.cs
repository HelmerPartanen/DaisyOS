using Avalonia.Threading;

namespace DaisyOS.Shell.Apps.Files.Services;

public interface IFileUiDispatcher
{
    Task InvokeAsync(Action action);
}

internal sealed class AvaloniaFileUiDispatcher : IFileUiDispatcher
{
    public async Task InvokeAsync(Action action) => await Dispatcher.UIThread.InvokeAsync(action);
}
