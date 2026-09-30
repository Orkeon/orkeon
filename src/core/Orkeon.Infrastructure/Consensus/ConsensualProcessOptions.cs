using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Consensus;

/// <summary>
/// Options for configuring the consensual process strategy.
/// </summary>
public class ConsensualProcessOptions
{
    /// <summary>
    /// Gets or sets the voting options used for consensus: the consensus type, its threshold,
    /// the quorum and whether abstention is allowed.
    /// </summary>
    public VotingOptions VotingOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets the maximum number of voting rounds before applying fallback. A round runs
    /// every agent on the task, then collects one ballot per agent.
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
    /// Keep the answer the last round's count put first, without running anything again. The
    /// task fails when no ballot of that round named an answer.
    /// </summary>
    AcceptBestScore,

    /// <summary>
    /// Fail the task execution.
    /// </summary>
    Fail,

    /// <summary>
    /// The crew's manager agent ranks the last round's anonymised answers and its first choice
    /// is kept. A consensual crew without a manager agent is refused before any agent runs.
    /// </summary>
    ManagerDecision
}
