using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

/// <summary>Streams navigation intent from the controller already detected by the shell.</summary>
public interface IControllerInputService : IAsyncDisposable
{
    event EventHandler<ControllerNavigationAction>? NavigationRequested;
    event EventHandler<RawControllerInputEventArgs>? RawInputReceived;

    bool IsRebinding { get; set; }

    void Start(ControllerConnectionStatus controller);
    void Stop();
    void ReloadKeybindings();
}
