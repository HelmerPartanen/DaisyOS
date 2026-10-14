namespace DaisyOS.Shell.Services.Compositor;

/// <summary>
/// Compositor-neutral window lifecycle contract used by shell controls.
/// KWin is the first implementation; the UI must not depend on KWin APIs.
/// </summary>
public interface ICompositorWindowService : IAsyncDisposable
{
    IReadOnlyCollection<CompositorWindowRecord> Windows { get; }

    event EventHandler? WindowsChanged;

    bool IsAvailable { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task ActivateOrMinimizeAsync(string windowId, bool wasActive, CancellationToken cancellationToken = default);

    Task CloseAsync(string windowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates only a compositor-confirmed unresponsive application. Normal
    /// windows must use CloseAsync so they have a chance to save their work.
    /// </summary>
    Task<bool> ForceQuitAsync(string windowId, CancellationToken cancellationToken = default);

    Task SetMaximizedAsync(string windowId, bool maximized, CancellationToken cancellationToken = default);

    Task MoveToWorkspaceAsync(string windowId, int workspace, CancellationToken cancellationToken = default);

    Task ToggleOverviewEffectAsync(string effectName = "daisyos-task-overview-v15", CancellationToken cancellationToken = default);
}
