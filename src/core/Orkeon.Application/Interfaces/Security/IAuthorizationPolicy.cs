using System.Security.Claims;

namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Context for an authorization evaluation.
/// </summary>
public record AuthorizationContext(
    ClaimsPrincipal Principal,
    string AgentId,
    string? CrewId = null,
    string? Action = null);

/// <summary>
/// Result of an authorization evaluation.
/// </summary>
public record AuthorizationResult(bool IsAuthorized, string? Reason = null);

/// <summary>
/// Evaluates whether a principal is authorized to perform an action on an agent or crew.
/// </summary>
public interface IAuthorizationPolicy
{
    /// <summary>
    /// The name of this authorization policy.
    /// </summary>
    string PolicyName { get; }

    /// <summary>
    /// Evaluates the authorization context and returns whether the action is authorized.
    /// </summary>
    System.Threading.Tasks.Task<AuthorizationResult> EvaluateAsync(AuthorizationContext context, CancellationToken ct = default);
}
