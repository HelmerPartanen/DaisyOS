using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IMediaSessionService
{
    event EventHandler? MediaChanged;

    Task<MediaSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default);

    Task PreviousAsync(CancellationToken cancellationToken = default);

    Task PlayPauseAsync(CancellationToken cancellationToken = default);

    Task NextAsync(CancellationToken cancellationToken = default);
}
