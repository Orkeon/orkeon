using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Agent.ValueObjects;

namespace Orkeon.Application.Services.Security;

/// <summary>
/// Guardian implementation that enforces tool access policies defined in the Domain layer.
/// Resolves the effective per-crew/agent <see cref="GuardianPolicy"/> from the
/// <see cref="GuardianPolicyEngine"/>, projects it onto a Domain <see cref="ToolAccessPolicy"/>
/// (whitelist from <see cref="GuardianPolicy.AllowedTools"/>, blacklist from
/// <see cref="GuardianPolicy.BlockedTools"/>), and delegates the allow/deny decision to
/// <see cref="ToolAccessPolicy.IsToolAllowed(string)"/>.
/// </summary>
public sealed class ToolAccessGuard : IGuardian
{
    private readonly GuardianPolicyEngine _policyEngine;

    /// <summary>
    /// Initializes a new instance of <see cref="ToolAccessGuard"/>.
    /// </summary>
    /// <param name="policyEngine">The engine resolving per-crew/agent guardian policies.</param>
    public ToolAccessGuard(GuardianPolicyEngine policyEngine)
    {
        ArgumentNullException.ThrowIfNull(policyEngine);
        _policyEngine = policyEngine;
    }

    /// <summary>
    /// Checks if a tool access request is allowed based on the agent's policy.
    /// Only processes requests in the ToolExecution phase with a defined ToolName.
    /// </summary>
    /// <param name="context">The guard context containing agent, tool, and phase information.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A GuardResult indicating whether the tool access is allowed or blocked.
    /// If the tool is not allowed by policy, returns a Block result with appropriate violation details.
    /// </returns>
    public System.Threading.Tasks.Task<GuardResult> CheckAsync(GuardContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Only check tool execution requests.
        if (context.Phase != GuardPhase.ToolExecution || string.IsNullOrEmpty(context.ToolName))
            return System.Threading.Tasks.Task.FromResult(GuardResult.Allow());

        var guardianPolicy = _policyEngine.GetPolicy(context.CrewId, context.AgentId);
        var toolAccessPolicy = BuildToolAccessPolicy(guardianPolicy);

        // Delegate the allow/deny decision to the Domain policy logic.
        var result = toolAccessPolicy.CheckToolAccess(context.ToolName);
        return System.Threading.Tasks.Task.FromResult(result);
    }

    /// <summary>
    /// Projects a <see cref="GuardianPolicy"/> onto a Domain <see cref="ToolAccessPolicy"/>.
    /// A non-empty <see cref="GuardianPolicy.AllowedTools"/> yields a whitelist; a non-empty
    /// <see cref="GuardianPolicy.BlockedTools"/> yields a blacklist intersected with the whitelist
    /// (least-privilege). When neither is set, access is unrestricted.
    /// </summary>
    private static ToolAccessPolicy BuildToolAccessPolicy(GuardianPolicy guardianPolicy)
    {
        var hasAllowList = guardianPolicy.AllowedTools.Count > 0;
        var hasBlockList = guardianPolicy.BlockedTools.Count > 0;

        if (hasAllowList && hasBlockList)
        {
            // Both restrictions apply: a tool must be whitelisted AND not blacklisted.
            return ToolAccessPolicy
                .CreateWhitelist(guardianPolicy.AllowedTools)
                .MergeWithCrewOverride(ToolAccessPolicy.CreateBlacklist(guardianPolicy.BlockedTools));
        }

        if (hasAllowList)
            return ToolAccessPolicy.CreateWhitelist(guardianPolicy.AllowedTools);

        if (hasBlockList)
            return ToolAccessPolicy.CreateBlacklist(guardianPolicy.BlockedTools);

        return ToolAccessPolicy.CreateUnrestricted();
    }
}

/// <summary>
/// Extension methods for evaluating tool access policies in the context of tool execution.
/// </summary>
public static class ToolAccessPolicyExtensions
{
    /// <summary>
    /// Checks if a tool is allowed by the policy and returns a GuardResult.
    /// </summary>
    /// <param name="policy">The tool access policy to check against.</param>
    /// <param name="toolName">The name of the tool to check access for.</param>
    /// <returns>
    /// A GuardResult allowing the operation if the tool is allowed, or blocking it with a violation if not.
    /// </returns>
    public static GuardResult CheckToolAccess(this ToolAccessPolicy? policy, string toolName)
    {
        // If no policy is defined, allow access
        if (policy == null)
            return GuardResult.Allow();

        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        // Check if the tool is allowed by the policy
        if (!policy.IsToolAllowed(toolName))
        {
            var modeDescription = policy.Mode switch
            {
                ToolAccessMode.Whitelist => "not in the whitelist",
                ToolAccessMode.Blacklist => "in the blacklist",
                _ => "denied by policy"
            };

            var violation = new GuardViolation(
                nameof(ToolAccessGuard),
                GuardPhase.ToolExecution,
                $"Tool '{toolName}' is {modeDescription}",
                GuardThreatSeverity.High,
                DateTime.UtcNow);

            return GuardResult.Block(
                $"Tool '{toolName}' is {modeDescription}",
                [violation]);
        }

        return GuardResult.Allow();
    }

    /// <summary>
    /// Merges an agent policy with an optional crew-level override policy using least-privilege semantics.
    /// A tool is only allowed if BOTH the agent policy and the crew policy allow it.
    /// The crew override can only restrict access, never expand it.
    /// </summary>
    /// <param name="agentPolicy">The agent's tool access policy.</param>
    /// <param name="crewOverride">The optional crew-level policy override.</param>
    /// <returns>
    /// The effective policy to use for the agent in this crew context (intersection of both).
    /// </returns>
    public static ToolAccessPolicy GetEffectivePolicy(this ToolAccessPolicy agentPolicy, ToolAccessPolicy? crewOverride)
    {
        ArgumentNullException.ThrowIfNull(agentPolicy);
        return agentPolicy.MergeWithCrewOverride(crewOverride);
    }
}
