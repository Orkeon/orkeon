using Orkeon.Infrastructure.Constants.Security;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for token budget tracking and limits.
/// </summary>
public class TokenBudgetOptions
{
    /// <summary>
    /// Maximum tokens per agent. 0 means unlimited.
    /// </summary>
    public int MaxTokensPerAgent { get; set; } = TokenBudgetDefaults.PerAgentTokenBudget;

    /// <summary>
    /// Maximum tokens per crew. 0 means unlimited.
    /// </summary>
    public int MaxTokensPerCrew { get; set; } = TokenBudgetDefaults.PerCrewTokenBudget;

    /// <summary>
    /// Maximum estimated cost per crew in USD. 0 means unlimited.
    /// </summary>
    public decimal MaxCostPerCrew { get; set; } = TokenBudgetDefaults.MaxCostPerCrew;
}
