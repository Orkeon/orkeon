namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// The running total a token budget tracker keeps for an agent or a crew: the tokens spent so far, their
/// estimated cost and the number of LLM calls they came from. Not the usage of one call — that is
/// <see cref="TokenUsage"/>.
/// </summary>
public sealed record TokenBudgetUsage
{
    /// <summary>The tokens spent so far (prompt + completion).</summary>
    public required int TotalTokens { get; init; }

    /// <summary>The estimated cost of those tokens.</summary>
    public required decimal EstimatedCost { get; init; }

    /// <summary>The number of LLM calls recorded.</summary>
    public required int CallCount { get; init; }

    /// <summary>A total with nothing recorded.</summary>
    public static TokenBudgetUsage None { get; } = new() { TotalTokens = 0, EstimatedCost = 0m, CallCount = 0 };
}
