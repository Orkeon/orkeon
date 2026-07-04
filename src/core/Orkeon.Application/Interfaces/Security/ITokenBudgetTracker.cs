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
    TokenUsageReport GetReport(string crewId);
}

/// <summary>
/// Aggregated token usage information.
/// </summary>
public record TokenUsage(int TotalTokens, decimal EstimatedCost, int CallCount);

/// <summary>
/// Result of a budget check after recording usage.
/// </summary>
public record BudgetCheckResult(bool IsWithinBudget, string? DenialReason, TokenUsage? AgentUsage, TokenUsage? CrewUsage)
{
    /// <summary>
    /// Creates a result indicating usage is within budget.
    /// </summary>
    public static BudgetCheckResult WithinBudget(TokenUsage agent, TokenUsage crew) => new(true, null, agent, crew);

    /// <summary>
    /// Creates a result indicating the budget has been exceeded.
    /// </summary>
    public static BudgetCheckResult BudgetExceeded(string reason, TokenUsage usage) => new(false, reason, usage, null);
}

/// <summary>
/// Token usage report for a crew, including per-agent breakdown.
/// </summary>
public record TokenUsageReport(string CrewId, TokenUsage TotalUsage, IReadOnlyDictionary<string, TokenUsage> UsageByAgent);
