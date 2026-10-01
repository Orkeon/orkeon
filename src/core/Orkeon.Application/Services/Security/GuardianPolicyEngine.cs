using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Application.Services.Security;

/// <summary>
/// Policy controlling which guardian phases are enabled. Which tools an agent may call is
/// not the guardian's business: that is <c>ToolAccessPolicy</c>, applied when the agent's
/// tools are resolved.
/// </summary>
public class GuardianPolicy
{
    /// <summary>
    /// Gets or sets a value indicating whether the input phase (prompt-injection screening of
    /// the composed user prompt) runs.
    /// </summary>
    public bool InputGuardEnabled { get; set; } = true;
    /// <summary>
    /// Gets or sets a value indicating whether the tool phase (path traversal, SSRF and SQL
    /// injection in tool arguments) runs.
    /// </summary>
    public bool ToolGuardEnabled { get; set; } = true;
    /// <summary>
    /// Gets or sets a value indicating whether the delegation phase (depth, self-delegation,
    /// cycles) runs.
    /// </summary>
    public bool DelegationGuardEnabled { get; set; } = true;
    /// <summary>Gets or sets how many synchronous delegations may nest before the next one is blocked.</summary>
    public int MaxDelegationDepth { get; set; } = 5;

    /// <summary>
    /// Checks whether a given guard phase is enabled in this policy.
    /// </summary>
    public bool IsGuardPhaseEnabled(GuardPhase phase) => phase switch
    {
        GuardPhase.Input => InputGuardEnabled,
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
