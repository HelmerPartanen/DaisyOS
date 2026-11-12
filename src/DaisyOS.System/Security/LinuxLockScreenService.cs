using DaisyOS.Core.Services;
using DaisyOS.System.Authentication;

namespace DaisyOS.System.Security;

public sealed class LinuxLockScreenService : ILockScreenService
{
    private readonly IAuthenticationService _authService;
    private bool _isLocked;

    public bool IsLocked => _isLocked;

    public event EventHandler<bool>? LockStateChanged;

    public LinuxLockScreenService(IAuthenticationService? authService = null)
    {
        _authService = authService ?? new LinuxPamAuthenticationService();
    }

    public void Lock()
    {
        if (_isLocked) return;
        _isLocked = true;
        LockStateChanged?.Invoke(this, true);
    }

    public void Unlock()
    {
        if (!_isLocked) return;
        _isLocked = false;
        LockStateChanged?.Invoke(this, false);
    }

    public async Task<bool> AuthenticateAsync(string password, CancellationToken cancellationToken = default)
    {
#if DEBUG
        // Dev / mock bypass for testing
        if (string.Equals(Environment.GetEnvironmentVariable("DAISYOS_MOCK_AUTH"), "1", StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable("DAISYOS_DEV_MODE"), "1", StringComparison.Ordinal))
        {
            await Task.Delay(300, cancellationToken);
            return true;
        }
#endif

        try
        {
            return await Task.Run(() => _authService.Authenticate(password), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
