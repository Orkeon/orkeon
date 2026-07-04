namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Rate limiter for tool invocations, supporting global and per-tool limits.
/// </summary>
public interface IToolRateLimiter
{
    /// <summary>
    /// Attempts to acquire a rate limit lease for the given tool and agent.
    /// </summary>
    System.Threading.Tasks.Task<RateLimitAcquisition> AcquireAsync(string toolName, string agentRole, CancellationToken ct = default);
}
