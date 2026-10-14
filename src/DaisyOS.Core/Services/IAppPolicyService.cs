using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IAppPolicyService
{
    Task<AppPolicyStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    AppPolicyDecision Evaluate(AppEntry app, AppPolicyStatus status);
}
