namespace DaisyOS.Core.Services;

public interface ILockScreenService
{
    bool IsLocked { get; }

    event EventHandler<bool>? LockStateChanged;

    void Lock();

    void Unlock();

    Task<bool> AuthenticateAsync(string password, CancellationToken cancellationToken = default);
}

