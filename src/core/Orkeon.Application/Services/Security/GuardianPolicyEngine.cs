using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Application.Services.Security;

/// <summary>
/// Policy controlling which guardian phases are enabled and what tools are allowed/blocked.
/// </summary>
public class GuardianPolicy
{
    /// <summary>
    /// Gets or sets a value indicating whether input guard enabled.
    /// </summary>
    public bool InputGuardEnabled { get; set; } = true;
    /// <summary>
    /// Gets or sets a value indicating whether output guard enabled.
    /// </summary>
    public bool OutputGuardEnabled { get; set; } = true;
    /// <summary>
    /// Gets or sets a value indicating whether tool guard enabled.
    /// </summary>
    public bool ToolGuardEnabled { get; set; } = true;
    /// <summary>
    /// Gets or sets a value indicating whether delegation guard enabled.
    /// </summary>
    public bool DelegationGuardEnabled { get; set; } = true;
    /// <summary>Gets or sets the max delegation depth.</summary>
    public int MaxDelegationDepth { get; set; } = 5;
    /// <summary>Gets or sets the allowed tools.</summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
    /// <summary>Gets or sets the blocked tools.</summary>
    public IReadOnlyList<string> BlockedTools { get; init; } = [];

    /// <summary>
    /// Checks whether a given guard phase is enabled in this policy.
    /// </summary>
    public bool IsGuardPhaseEnabled(GuardPhase phase) => phase switch
    {
        GuardPhase.Input => InputGuardEnabled,
        GuardPhase.Output => OutputGuardEnabled,
        GuardPhase.ToolExecution => ToolGuardEnabled,
        GuardPhase.Delegation => DelegationGuardEnabled,
        _ => true
    };
}

/// <summary>
/// Engine that resolves the effective guardian policy for a given crew and agent.
/// Priority: agent policy > crew policy > global default policy.
/// </summary>
public sealed class GuardianPolicyEngine
{
    private readonly GuardianPolicy _globalPolicy;
    private readonly Dictionary<string, GuardianPolicy> _crewPolicies = [];
    private readonly Dictionary<string, GuardianPolicy> _agentPolicies = [];
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of <see cref="GuardianPolicyEngine"/>.
    /// </summary>
    public GuardianPolicyEngine(GuardianPolicy globalPolicy)
    {
        ArgumentNullException.ThrowIfNull(globalPolicy);
        _globalPolicy = globalPolicy;
    }

    /// <summary>
    /// Gets the effective policy for the given crew and agent.
    /// Priority: agent > crew > global.
    /// </summary>
    public GuardianPolicy GetPolicy(string crewId, string agentId)
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(agentId) && _agentPolicies.TryGetValue(agentId, out var agentPolicy))
                return agentPolicy;

            if (!string.IsNullOrEmpty(crewId) && _crewPolicies.TryGetValue(crewId, out var crewPolicy))
                return crewPolicy;

            return _globalPolicy;
        }
    }

    /// <summary>
    /// Sets a policy override for a specific crew.
    /// </summary>
    public void SetCrewPolicy(string crewId, GuardianPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        ArgumentNullException.ThrowIfNull(policy);
        lock (_lock)
        {
            _crewPolicies[crewId] = policy;
        }
    }

    /// <summary>
    /// Sets a policy override for a specific agent.
    /// </summary>
    public void SetAgentPolicy(string agentId, GuardianPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentNullException.ThrowIfNull(policy);
        lock (_lock)
        {
            _agentPolicies[agentId] = policy;
        }
    }
}
