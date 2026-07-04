using Orkeon.Domain.Common;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Represents a delegation decision made by an agent.
/// </summary>
public sealed record DelegationDecision
{
    /// <summary>
    /// Gets whether delegation should occur.
    /// </summary>
    public bool ShouldDelegate { get; init; }

    /// <summary>
    /// Gets the agent ID to delegate to, if delegation should occur.
    /// </summary>
    public AgentId? DelegateToAgentId { get; init; }

    /// <summary>
    /// Gets the reason for the delegation decision.
    /// </summary>
    public string? Reason { get; init; }

    private DelegationDecision(bool shouldDelegate, AgentId? delegateToAgentId, string? reason)
    {
        ShouldDelegate = shouldDelegate;
        DelegateToAgentId = delegateToAgentId;
        Reason = reason;
    }

    /// <summary>
    /// Creates a decision not to delegate.
    /// </summary>
    public static DelegationDecision NoDelegation(string? reason = null)
    {
        return new DelegationDecision(false, null, reason ?? "No delegation needed");
    }

    /// <summary>
    /// Creates a decision to delegate to a specific agent.
    /// </summary>
    public static DelegationDecision DelegateTo(AgentId agentId, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        return new DelegationDecision(
            true,
            agentId,
            reason ?? $"Delegating to agent {agentId} for optimal task execution");
    }
}
