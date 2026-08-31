namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Records a single LLM usage event for cost tracking.
/// </summary>
public record CostUsageEvent
{
    /// <summary>Crew identifier.</summary>
    public string CrewId { get; init; } = string.Empty;

    /// <summary>Agent identifier.</summary>
    public string AgentId { get; init; } = string.Empty;

    /// <summary>Model used (e.g., "gpt-4o").</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Provider name (e.g., "openai", "anthropic").</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>Number of prompt (input) tokens.</summary>
    public int PromptTokens { get; init; }

    /// <summary>Number of completion (output) tokens.</summary>
    public int CompletionTokens { get; init; }

    /// <summary>
    /// Prompt tokens served from the provider's cache. The hit/miss pair is a PARTITION
    /// of <see cref="PromptTokens"/> — never additive to it: cost and budget consumers
    /// must not count cache tokens twice. Null when the provider reported no cache
    /// telemetry (W-08).
    /// </summary>
    public long? CacheHitTokens { get; init; }

    /// <summary>Prompt tokens the provider had to compute; null when unmeasured.</summary>
    public long? CacheMissTokens { get; init; }

    /// <summary>
    /// True when the token counts are an APPROXIMATION the runtime computed itself,
    /// because the provider returned no usage at all. Callers that display a figure must
    /// say so (Studio prefixes it with «≈»); callers that bill or budget on it are
    /// deliberately biased high — an estimate trips a budget early, never late.
    /// </summary>
    public bool Estimated { get; init; }

    /// <summary>
    /// Pre-calculated cost in USD. If zero, the manager will auto-calculate from the pricing registry.
    /// </summary>
    public decimal Cost { get; init; }

    /// <summary>Type of operation (e.g., "llm_call", "embedding").</summary>
    public string OperationType { get; init; } = "llm_call";

    /// <summary>Timestamp of the event.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Defines budget limits for a crew or agent.
/// </summary>
public record BudgetLimit
{
    /// <summary>Maximum total cost in USD (null = unlimited).</summary>
    public decimal? MaxCostUsd { get; init; }

    /// <summary>Maximum total tokens (null = unlimited).</summary>
    public int? MaxTokens { get; init; }

    /// <summary>Maximum LLM calls per minute (null = unlimited).</summary>
    public int? MaxCallsPerMinute { get; init; }

    /// <summary>Percentage of budget at which a warning alert is fired (default: 80%).</summary>
    public decimal? AlertThresholdPercent { get; init; } = 80m;
}

/// <summary>
/// Types of budget alerts.
/// </summary>
public enum BudgetAlertType
{
    /// <summary>Usage has reached the warning threshold.</summary>
    CostThresholdWarning,

    /// <summary>Cost budget has been exceeded.</summary>
    CostBudgetExceeded,

    /// <summary>Token budget has been exceeded.</summary>
    TokenBudgetExceeded,

    /// <summary>Rate limit has been exceeded.</summary>
    RateLimitExceeded
}

/// <summary>
/// Represents a budget alert raised when usage approaches or exceeds limits.
/// </summary>
public record BudgetAlert
{
    /// <summary>Crew identifier.</summary>
    public string CrewId { get; init; } = string.Empty;

    /// <summary>Agent identifier (null for crew-level alerts).</summary>
    public string? AgentId { get; init; }

    /// <summary>Type of alert.</summary>
    public BudgetAlertType Type { get; init; }

    /// <summary>Current usage value.</summary>
    public decimal CurrentUsage { get; init; }

    /// <summary>Budget limit value.</summary>
    public decimal Limit { get; init; }

    /// <summary>Current usage as a percentage of the limit.</summary>
    public decimal UsagePercent { get; init; }

    /// <summary>Timestamp of the alert.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Event args for budget alert events.
/// </summary>
public class BudgetAlertEventArgs : EventArgs
{
    /// <summary>The budget alert that was raised.</summary>
    public BudgetAlert Alert { get; init; } = null!;
}

/// <summary>
/// Summary of costs for a single agent.
/// </summary>
public record AgentCostSummary(
    string AgentId,
    decimal TotalCost,
    int TotalTokens,
    int TotalCalls,
    decimal AverageCostPerCall);

/// <summary>
/// Aggregated cost report for a crew, agent, or time period.
/// </summary>
public record CostReport
{
    /// <summary>Total cost in USD.</summary>
    public decimal TotalCost { get; init; }

    /// <summary>Total tokens consumed.</summary>
    public int TotalTokens { get; init; }

    /// <summary>Total number of LLM calls.</summary>
    public int TotalCalls { get; init; }

    /// <summary>Cost breakdown by agent.</summary>
    public IReadOnlyDictionary<string, AgentCostSummary> ByAgent { get; init; } = new Dictionary<string, AgentCostSummary>();

    /// <summary>Cost breakdown by model.</summary>
    public IReadOnlyDictionary<string, decimal> ByModel { get; init; } = new Dictionary<string, decimal>();

    /// <summary>Cost breakdown by operation type.</summary>
    public IReadOnlyDictionary<string, decimal> ByOperationType { get; init; } = new Dictionary<string, decimal>();

    /// <summary>Start of the reporting period.</summary>
    public DateTime PeriodStart { get; init; }

    /// <summary>End of the reporting period.</summary>
    public DateTime PeriodEnd { get; init; }
}

/// <summary>
/// Result of a budget check after recording usage.
/// </summary>
public record CostBudgetCheckResult
{
    /// <summary>Whether the usage is within all budget limits.</summary>
    public bool IsWithinBudget { get; init; }

    /// <summary>Descriptive message when budget is exceeded.</summary>
    public string? Message { get; init; }

    /// <summary>Associated alert if budget was exceeded or threshold reached.</summary>
    public BudgetAlert? Alert { get; init; }

    /// <summary>Creates a result indicating usage is within budget.</summary>
    public static CostBudgetCheckResult WithinBudget() => new() { IsWithinBudget = true };

    /// <summary>Creates a result indicating the budget has been exceeded.</summary>
    public static CostBudgetCheckResult BudgetExceeded(string message, BudgetAlert alert)
        => new() { IsWithinBudget = false, Message = message, Alert = alert };
}

/// <summary>
/// Manages cost tracking, budget enforcement, and reporting for LLM usage.
/// </summary>
public interface ICostBudgetManager
{
    /// <summary>
    /// Records a usage event, auto-calculates cost if not provided, and checks budgets.
    /// </summary>
    CostBudgetCheckResult RecordUsage(CostUsageEvent usageEvent);

    /// <summary>
    /// Gets a cost report, optionally filtered by crew and/or agent.
    /// </summary>
    CostReport GetReport(string? crewId = null, string? agentId = null);

    /// <summary>
    /// Gets a cost report for a specific time period.
    /// </summary>
    CostReport GetReportForPeriod(DateTime from, DateTime toDate, string? crewId = null);

    /// <summary>
    /// Sets a budget limit for a crew.
    /// </summary>
    void SetCrewBudget(string crewId, BudgetLimit budget);

    /// <summary>
    /// Sets a budget limit for an agent within a crew.
    /// </summary>
    void SetAgentBudget(string crewId, string agentId, BudgetLimit budget);

    /// <summary>
    /// Gets all active (uncleared) budget alerts.
    /// </summary>
    IReadOnlyList<BudgetAlert> GetActiveAlerts();

    /// <summary>
    /// Raised when a budget alert is triggered.
    /// </summary>
    event EventHandler<BudgetAlertEventArgs>? OnBudgetAlert;
}
