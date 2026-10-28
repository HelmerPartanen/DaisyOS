namespace DaisyOS.Core.Continuity;

public interface IContinuityClient
{
    Task<ContinuityStatus> ExecuteAsync(ContinuityCommand command, CancellationToken token = default);
}
