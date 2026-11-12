using Orkeon.Application.Common.DTOs;

namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Tracks token usage and budget limits for crews and agents.
/// </summary>
public interface ITokenBudgetTracker
{
    /// <summary>
    /// Records token usage for an agent within a crew and checks budget limits.
    /// </summary>
    BudgetCheckResult RecordUsage(string crewId, string agentRole, int promptTokens, int completionTokens, string model);

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
