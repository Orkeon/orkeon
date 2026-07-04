namespace Orkeon.Infrastructure.Constants.Scoring;

/// <summary>
/// Default scoring values used during agent selection and task matching.
/// Centralises numeric literals to avoid duplication across selection strategies.
/// </summary>
public static class ScoringDefaults
{
    /// <summary>Score bonus applied when a task word exactly matches a role word.</summary>
    public const double ExactMatchBonus = 1.5;

    /// <summary>Score weight applied when a task word partially matches a goal word.</summary>
    public const double PartialMatchWeight = 0.75;

    /// <summary>Score multiplier applied per tool available on the agent (historical weighting).</summary>
    public const double HistoricalMultiplier = 2.0;

    /// <summary>Minimum word length (characters) required for partial match consideration.</summary>
    public const int MinWordLengthForMatching = 4;
}
