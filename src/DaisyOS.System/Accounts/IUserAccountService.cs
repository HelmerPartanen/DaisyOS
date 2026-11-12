namespace DaisyOS.System.Accounts;

public sealed record UserAccount(string UserName, string FullName, bool IsAdministrator, int PasswordMode,
    bool AutomaticLogin, bool IsLocalAccount);
public sealed record AccountReadResult(UserAccount? Account, string? Error = null);
public sealed record AccountChangeResult(bool Succeeded, string? Error = null);

public interface IUserAccountService
{
    Task<AccountReadResult> GetCurrentAccountAsync(CancellationToken cancellationToken = default);
    Task<AccountChangeResult> SetFullNameAsync(string fullName, CancellationToken cancellationToken = default);
    Task<AccountChangeResult> SetPasswordAsync(string password, string hint, CancellationToken cancellationToken = default);
    Task<AccountChangeResult> RemovePasswordAsync(CancellationToken cancellationToken = default);
    Task<AccountChangeResult> SetAutomaticLoginAsync(bool enabled, CancellationToken cancellationToken = default);
}
