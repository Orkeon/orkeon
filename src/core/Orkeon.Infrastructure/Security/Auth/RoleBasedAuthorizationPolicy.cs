using System.Security.Claims;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Auth;

/// <summary>
/// Maps a role to the agent and crew IDs it is allowed to access.
/// </summary>
public record RoleMapping(
    string Role,
    IReadOnlyList<string> AllowedAgentIds,
    IReadOnlyList<string> AllowedCrewIds);

/// <summary>
/// Authorization policy that maps roles to allowed agents and crews.
/// A principal must have at least one role that permits access to the requested agent/crew.
/// </summary>
public class RoleBasedAuthorizationPolicy : IAuthorizationPolicy
{
    private readonly IReadOnlyList<RoleMapping> _roleMappings;

    /// <inheritdoc />
    public string PolicyName => "RoleBased";

    /// <summary>
    /// Initializes a new instance of <see cref="RoleBasedAuthorizationPolicy"/>.
    /// </summary>
    public RoleBasedAuthorizationPolicy(IEnumerable<RoleMapping> roleMappings)
    {
        ArgumentNullException.ThrowIfNull(roleMappings);
        _roleMappings = roleMappings.ToList();
    }

    /// <inheritdoc />
    public Task<AuthorizationResult> EvaluateAsync(AuthorizationContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var principalRoles = context.Principal.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Find all role mappings that match the principal's roles
        var matchingMappings = _roleMappings
            .Where(m => principalRoles.Contains(m.Role))
            .ToList();

        if (matchingMappings.Count == 0)
        {
            return Task.FromResult(new AuthorizationResult(
                false,
                $"No role mapping found for the principal's roles"));
        }

        // Check agent access
        if (!string.IsNullOrEmpty(context.AgentId))
        {
            bool agentAllowed = matchingMappings.Any(m =>
                m.AllowedAgentIds.Count == 0 || // empty = allow all
                m.AllowedAgentIds.Contains(context.AgentId, StringComparer.OrdinalIgnoreCase));

            if (!agentAllowed)
            {
                return Task.FromResult(new AuthorizationResult(
                    false,
                    $"None of the principal's roles grant access to agent '{context.AgentId}'"));
            }
        }

        // Check crew access
        if (!string.IsNullOrEmpty(context.CrewId))
        {
            bool crewAllowed = matchingMappings.Any(m =>
                m.AllowedCrewIds.Count == 0 || // empty = allow all
                m.AllowedCrewIds.Contains(context.CrewId, StringComparer.OrdinalIgnoreCase));

            if (!crewAllowed)
            {
                return Task.FromResult(new AuthorizationResult(
                    false,
                    $"None of the principal's roles grant access to crew '{context.CrewId}'"));
            }
        }

        return Task.FromResult(new AuthorizationResult(true));
    }
}
