using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Services.Security;

namespace Orkeon.Infrastructure.Security.Guards;

/// <summary>
/// Guardian of the <see cref="GuardPhase.Delegation"/> phase: blocks a delegation that would
/// nest deeper than <see cref="GuardianPolicy.MaxDelegationDepth"/>, an agent delegating to
/// itself, and a cycle — a target already in the chain of agents that delegated down to this
/// call. Stateless: the chain travels with the call (<see cref="GuardContext.DelegationChain"/>,
/// kept by the tool-invocation pipeline along the async flow), so a finished delegation leaves
/// nothing behind and the same pair of agents may delegate again later.
/// </summary>
public sealed class DelegationGuard : IGuardian
{
    private readonly GuardianPolicyEngine _policyEngine;

    /// <summary>Initializes a new instance of <see cref="DelegationGuard"/>.</summary>
    /// <param name="policyEngine">The policy engine the maximum depth is read from.</param>
    public DelegationGuard(GuardianPolicyEngine policyEngine)
    {
        ArgumentNullException.ThrowIfNull(policyEngine);
        _policyEngine = policyEngine;
    }

    /// <inheritdoc />
    public Task<GuardResult> CheckAsync(GuardContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Phase != GuardPhase.Delegation)
            return Task.FromResult(GuardResult.Allow());

        var policy = _policyEngine.GetPolicy(context.CrewId, context.AgentId);

        if (context.DelegationDepth >= policy.MaxDelegationDepth)
        {
            return Block(
                $"Maximum delegation depth ({policy.MaxDelegationDepth}) exceeded",
                $"Delegation depth {context.DelegationDepth} reaches the maximum {policy.MaxDelegationDepth}",
                GuardThreatSeverity.High);
        }

        var target = context.TargetAgentRole;
        if (string.IsNullOrWhiteSpace(target))
            return Task.FromResult(GuardResult.Allow());

        if (SameAgent(target, context.AgentRole))
        {
            return Block(
                "Self-delegation is not allowed",
                $"Agent '{context.AgentRole}' attempted to delegate to itself",
                GuardThreatSeverity.High);
        }

        if (context.DelegationChain.Any(role => SameAgent(role, target)))
        {
            return Block(
                "Circular delegation detected",
                $"Agent '{target}' is already in the delegation chain ({string.Join(" -> ", context.DelegationChain)} -> {context.AgentRole})",
                GuardThreatSeverity.Critical);
        }

        return Task.FromResult(GuardResult.Allow());
    }

    private static bool SameAgent(string left, string right)
        => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static Task<GuardResult> Block(string reason, string description, GuardThreatSeverity severity)
    {
        var violation = new GuardViolation(nameof(DelegationGuard), GuardPhase.Delegation, description, severity, DateTime.UtcNow);
        return Task.FromResult(GuardResult.Block(reason, [violation]));
    }
}
