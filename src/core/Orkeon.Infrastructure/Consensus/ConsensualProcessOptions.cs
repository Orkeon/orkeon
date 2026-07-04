using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Consensus;

/// <summary>
/// Options for configuring the consensual process strategy.
/// </summary>
public class ConsensualProcessOptions
{
    /// <summary>
    /// Gets or sets the voting options used for consensus.
    /// </summary>
    public VotingOptions VotingOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets the maximum number of voting rounds before applying fallback.
    /// </summary>
    public int MaxVotingRounds { get; set; } = 3;

    /// <summary>
    /// Gets or sets whether discussion rounds are enabled when consensus is not reached.
    /// During discussion, agents receive the context of other agents' results.
    /// </summary>
    public bool EnableDiscussion { get; set; } = true;

    /// <summary>
    /// Gets or sets the fallback strategy when consensus cannot be reached.
    /// </summary>
    public ConsensusFallback FallbackStrategy { get; set; } = ConsensusFallback.AcceptBestScore;

    /// <summary>
    /// Gets or sets role-based weights for weighted consensus voting.
    /// Key is the agent role, value is the weight multiplier.
    /// </summary>
    public Dictionary<string, float> RoleWeights { get; } = [];
}

/// <summary>
/// Defines the fallback behavior when consensus cannot be reached.
/// </summary>
public enum ConsensusFallback
{
    /// <summary>
    /// Accept the choice with the highest score.
    /// </summary>
    AcceptBestScore,

    /// <summary>
    /// Fail the task execution.
    /// </summary>
    Fail,

    /// <summary>
    /// Let the manager agent make the final decision.
    /// </summary>
    ManagerDecision
}
