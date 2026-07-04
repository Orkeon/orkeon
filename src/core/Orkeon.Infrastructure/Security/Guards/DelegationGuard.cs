using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Services.Security;

namespace Orkeon.Infrastructure.Security.Guards;

/// <summary>
/// Guardian that checks delegation requests for depth limits, self-delegation,
/// and circular delegation chains.
/// Uses a single global lock to prevent TOCTOU race conditions between
/// dictionary access and circularity checks.
/// </summary>
public sealed class DelegationGuard : IGuardian
{
    private readonly GuardianPolicyEngine _policyEngine;
    private readonly Dictionary<string, HashSet<string>> _activeChains = [];

    /// <summary>Single global lock for all delegation chain operations (no TOCTOU gap).</summary>
    private readonly object _globalLock = new();

    /// <summary>Initializes a new instance of <see cref="DelegationGuard"/>.</summary>
    /// <param name="policyEngine">The policy engine used to retrieve per-crew/agent security policies.</param>
    /// <param name="logger">The logger.</param>
    public DelegationGuard(GuardianPolicyEngine policyEngine, ILogger<DelegationGuard> logger)
    {
        ArgumentNullException.ThrowIfNull(policyEngine);
        _policyEngine = policyEngine;
        ArgumentNullException.ThrowIfNull(logger);
        _ = logger;
    }

    /// <inheritdoc />
    public Task<GuardResult> CheckAsync(GuardContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Phase != GuardPhase.Delegation)
            return Task.FromResult(GuardResult.Allow());

        var policy = _policyEngine.GetPolicy(context.CrewId, context.AgentId);
        var violations = new List<GuardViolation>();

        // Check delegation depth
        if (context.DelegationDepth >= policy.MaxDelegationDepth)
        {
            violations.Add(new GuardViolation(
                nameof(DelegationGuard), GuardPhase.Delegation,
                $"Delegation depth {context.DelegationDepth} exceeds maximum {policy.MaxDelegationDepth}",
                GuardThreatSeverity.High, DateTime.UtcNow));
            return Task.FromResult(GuardResult.Block(
                $"Maximum delegation depth ({policy.MaxDelegationDepth}) exceeded", violations));
        }

        // Check self-delegation
        if (!string.IsNullOrEmpty(context.TargetAgentId) &&
            string.Equals(context.AgentId, context.TargetAgentId, StringComparison.Ordinal))
        {
            violations.Add(new GuardViolation(
                nameof(DelegationGuard), GuardPhase.Delegation,
                $"Agent '{context.AgentId}' attempted self-delegation",
                GuardThreatSeverity.High, DateTime.UtcNow));
            return Task.FromResult(GuardResult.Block(
                "Self-delegation is not allowed", violations));
        }

        // Check circular delegation — global lock acquired BEFORE any dictionary access
        if (!string.IsNullOrEmpty(context.TargetAgentId) && !string.IsNullOrEmpty(context.AgentId))
        {
            lock (_globalLock)
            {
                var chainKey = BuildChainKey(context.CrewId, context.AgentId);

                if (!_activeChains.TryGetValue(chainKey, out var chain))
                {
                    chain = [];
                    _activeChains[chainKey] = chain;
                }

                // Add current agent to the chain
                chain.Add(context.AgentId);

                // Check direct circularity
                if (chain.Contains(context.TargetAgentId))
                {
                    violations.Add(new GuardViolation(
                        nameof(DelegationGuard), GuardPhase.Delegation,
                        $"Circular delegation detected: agent '{context.TargetAgentId}' is already in the delegation chain",
                        GuardThreatSeverity.Critical, DateTime.UtcNow));
                    return Task.FromResult(GuardResult.Block(
                        "Circular delegation detected", violations));
                }

                // Transitive circularity detection using BFS:
                // Check if targetAgentId can reach agentId through existing delegation chains
                if (WouldCreateCycle(context.AgentId, context.TargetAgentId, context.CrewId))
                {
                    violations.Add(new GuardViolation(
                        nameof(DelegationGuard), GuardPhase.Delegation,
                        $"Transitive circular delegation detected: agent '{context.TargetAgentId}' can reach '{context.AgentId}' through existing chains",
                        GuardThreatSeverity.Critical, DateTime.UtcNow));
                    return Task.FromResult(GuardResult.Block(
                        "Circular delegation detected", violations));
                }

                // Track target as part of the chain for future checks
                chain.Add(context.TargetAgentId);
            }
        }

        return Task.FromResult(GuardResult.Allow());
    }

    /// <summary>
    /// Clears the delegation chain for the given key, typically called when a delegation completes.
    /// </summary>
    public void ClearChain(string chainKey)
    {
        lock (_globalLock)
        {
            _activeChains.Remove(chainKey);
        }
    }

    /// <summary>
    /// Checks whether a delegation exists for the given chain key and target agent.
    /// </summary>
    public bool HasDelegation(string chainKey, string targetAgentId)
    {
        lock (_globalLock)
        {
            return _activeChains.TryGetValue(chainKey, out var chain) &&
                   chain.Contains(targetAgentId);
        }
    }

    /// <summary>
    /// Uses BFS to detect if adding a delegation from agentId to targetAgentId
    /// would create a transitive cycle through existing chains.
    /// Must be called while holding <see cref="_globalLock"/>.
    /// </summary>
    private bool WouldCreateCycle(string agentId, string targetAgentId, string crewId)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(targetAgentId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!visited.Add(current))
                continue;

            if (string.Equals(current, agentId, StringComparison.Ordinal))
                return true;

            var currentChainKey = BuildChainKey(crewId, current);
            if (_activeChains.TryGetValue(currentChainKey, out var delegations))
            {
                foreach (var delegation in delegations)
                {
                    if (!visited.Contains(delegation))
                    {
                        queue.Enqueue(delegation);
                    }
                }
            }
        }

        return false;
    }

    private static string BuildChainKey(string crewId, string agentId)
    {
        return $"{crewId}:{agentId}";
    }
}
