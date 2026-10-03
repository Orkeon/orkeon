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
/// Holds the host's guardian policy (<c>Orkeon:Guardian:DefaultPolicy</c>): one policy for every
/// crew and every agent the host runs. A policy per crew or per agent existed as two setters that
/// nothing called (GAP-26); the policy is set per host.
/// </summary>
public sealed class GuardianPolicyEngine
{
    /// <summary>
    /// Initializes a new instance of <see cref="GuardianPolicyEngine"/>.
    /// </summary>
    public GuardianPolicyEngine(GuardianPolicy globalPolicy)
    {
        ArgumentNullException.ThrowIfNull(globalPolicy);
        Policy = globalPolicy;
    }

    /// <summary>
    /// Gets the policy every guard phase reads.
    /// </summary>
    public GuardianPolicy Policy { get; }
}
