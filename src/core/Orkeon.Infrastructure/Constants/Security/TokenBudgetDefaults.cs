namespace Orkeon.Infrastructure.Constants.Security;

/// <summary>
/// Default values for token budget configuration.
/// Centralises magic numbers used across the token budget tracking system.
/// </summary>
public static class TokenBudgetDefaults
{
    /// <summary>Default maximum tokens per agent (100,000). 0 means unlimited.</summary>
    public const int PerAgentTokenBudget = 100_000;

    /// <summary>Default maximum tokens per crew (500,000). 0 means unlimited.</summary>
    public const int PerCrewTokenBudget = 500_000;

    /// <summary>Default maximum estimated cost per crew in USD (10.00). 0 means unlimited.</summary>
    public const decimal MaxCostPerCrew = 10.0m;
}
