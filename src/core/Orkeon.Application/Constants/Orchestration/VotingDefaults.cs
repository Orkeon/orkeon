namespace Orkeon.Application.Constants.Orchestration;

/// <summary>
/// Default values for voting configuration parameters.
/// Centralizes magic numbers used in voting strategies and consensus building.
/// </summary>
public static class VotingDefaults
{
    /// <summary>
    /// Default consensus threshold percentage for super-majority and weighted consensus voting.
    /// </summary>
    public const float DefaultConsensusThreshold = 66.7f;

    /// <summary>
    /// Default confidence level for a vote (0.0 to 1.0).
    /// </summary>
    public const float DefaultConfidence = 1.0f;

    /// <summary>
    /// Default weight for a vote in weighted voting strategies.
    /// </summary>
    public const float DefaultWeight = 1.0f;
}
