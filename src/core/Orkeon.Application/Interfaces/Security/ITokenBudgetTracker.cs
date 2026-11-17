using Orkeon.Application.Common.DTOs;

namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Tracks token usage and budget limits for crews and agents.
/// </summary>
public interface ITokenBudgetTracker
{
    /// <summary>
    /// Records token usage for an agent within a crew and checks budget limits. The provider is not known:
    /// the price rows scoped to a provider are not read.
    /// </summary>
    BudgetCheckResult RecordUsage(string crewId, string agentRole, int promptTokens, int completionTokens, string model);

    /// <summary>
    /// Records token usage for an agent within a crew and checks budget limits, pricing the call with the
    /// rows of the provider that served it. A tracker that does not implement this member ignores the provider.
    /// </summary>
    /// <param name="crewId">The crew the usage belongs to.</param>
    /// <param name="agentRole">The role of the agent that made the call.</param>
    /// <param name="promptTokens">The prompt tokens of the call.</param>
    /// <param name="completionTokens">The completion tokens of the call.</param>
    /// <param name="model">The model that served the call.</param>
    /// <param name="provider">The id of the provider that served the call, such as <c>ollama</c> or <c>deepseek</c> (case is ignored), or null when it is not known. An id no price row names is priced as a call without a provider.</param>
    /// <returns>The budget check after the usage is recorded.</returns>
    BudgetCheckResult RecordUsage(string crewId, string agentRole, int promptTokens, int completionTokens, string model, string? provider)
        => RecordUsage(crewId, agentRole, promptTokens, completionTokens, model);

    /// <summary>
    /// Gets a usage report for a crew.
    /// </summary>
    TokenBudgetReport GetReport(string crewId);
}

/// <summary>
/// Result of a budget check after recording usage.
/// </summary>
public sealed record BudgetCheckResult(bool IsWithinBudget, string? DenialReason, TokenBudgetUsage? AgentUsage, TokenBudgetUsage? CrewUsage)
{
    /// <summary>
    /// Creates a result indicating usage is within budget.
    /// </summary>
    public static BudgetCheckResult WithinBudget(TokenBudgetUsage agent, TokenBudgetUsage crew) => new(true, null, agent, crew);

    /// <summary>
    /// Creates a result indicating the budget has been exceeded.
    /// </summary>
    public static BudgetCheckResult BudgetExceeded(string reason, TokenBudgetUsage usage) => new(false, reason, usage, null);
}

/// <summary>
/// Token usage report for a crew, including per-agent breakdown.
/// </summary>
public sealed record TokenBudgetReport(string CrewId, TokenBudgetUsage TotalUsage, IReadOnlyDictionary<string, TokenBudgetUsage> UsageByAgent);
