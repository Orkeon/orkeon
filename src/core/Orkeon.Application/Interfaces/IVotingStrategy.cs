using Orkeon.Application.Constants.Orchestration;

namespace Orkeon.Application.Interfaces;

/// <summary>
/// Represents a single vote cast by an agent during consensual process execution.
/// </summary>
public record Vote
{
    /// <summary>
    /// Gets the identifier of the voter (agent).
    /// </summary>
    public string VoterId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the role of the voter.
    /// </summary>
    public string VoterRole { get; init; } = string.Empty;

    /// <summary>
    /// Gets the choice made by the voter.
    /// For Borda count, this is a comma-separated ranking of choices.
    /// An empty choice is an abstention: the ballot was not expressed.
    /// </summary>
    public string Choice { get; init; } = string.Empty;

    /// <summary>
    /// Gets the choice this voter authored and therefore may not vote for (GAP-04), or null
    /// when the voter authored none. A choice's share is counted among the ballots that could
    /// name it: the author's ballot is left out of its own candidate's count, so three agents
    /// that give the same answer can still reach unanimity without voting for themselves.
    /// </summary>
    public string? OwnChoice { get; init; }

    /// <summary>
    /// Gets the confidence level of the vote (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; init; } = VotingDefaults.DefaultConfidence;

    /// <summary>
    /// Gets the weight of this vote.
    /// </summary>
    public float Weight { get; init; } = VotingDefaults.DefaultWeight;

    /// <summary>
    /// Gets an optional justification for the vote.
    /// </summary>
    public string? Justification { get; init; }

    /// <summary>
    /// Gets the timestamp when the vote was cast.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents the result of tallying votes.
/// </summary>
public record VoteResult
{
    /// <summary>
    /// Gets whether consensus was reached.
    /// </summary>
    public bool ConsensusReached { get; init; }

    /// <summary>
    /// Gets the winning choice, if any.
    /// </summary>
    public string? WinningChoice { get; init; }

    /// <summary>
    /// Gets the agreement score (percentage of votes for the winner).
    /// </summary>
    public float AgreementScore { get; init; }

    /// <summary>
    /// Gets the total number of votes cast.
    /// </summary>
    public int TotalVotes { get; init; }

    /// <summary>
    /// Gets the number of votes for the winning choice.
    /// </summary>
    public int VotesForWinner { get; init; }

    /// <summary>
    /// Gets the scores for each choice.
    /// </summary>
    public IReadOnlyDictionary<string, float> Scores { get; init; } = new Dictionary<string, float>();

    /// <summary>
    /// Gets all votes that were tallied.
    /// </summary>
    public IReadOnlyList<Vote> AllVotes { get; init; } = Array.Empty<Vote>();
}

/// <summary>
/// Options for configuring the voting process.
/// </summary>
public class VotingOptions
{
    /// <summary>
    /// Gets or sets the consensus type to use.
    /// </summary>
    public ConsensusType ConsensusType { get; set; } = ConsensusType.Majority;

    /// <summary>
    /// Gets or sets the quorum, in percent: the minimum share of expressed ballots among all
    /// ballots. Below it, no consensus is reached, whatever the expressed ballots say.
    /// </summary>
    public float QuorumPercent { get; set; } = 50f;

    /// <summary>
    /// Gets or sets the consensus threshold percentage.
    /// Used by SuperMajority and WeightedConsensus types.
    /// </summary>
    public float ConsensusThreshold { get; set; } = VotingDefaults.DefaultConsensusThreshold;

    /// <summary>
    /// Gets or sets whether to use weighted votes.
    /// </summary>
    public bool UseWeightedVotes { get; set; }

    /// <summary>
    /// Gets or sets whether abstention is allowed. When true (the default), an abstention only
    /// counts toward the quorum. When false, an abstention counts as a vote against every
    /// choice: it stays in the denominator of each share, and it breaks unanimity. A Borda
    /// count, which has no share threshold, then reaches no consensus while anyone abstains.
    /// </summary>
    public bool AllowAbstention { get; set; } = true;
}

/// <summary>
/// Defines the type of consensus required.
/// </summary>
public enum ConsensusType
{
    /// <summary>
    /// Simple majority (more than 50%).
    /// </summary>
    Majority,

    /// <summary>
    /// Super majority (configurable threshold, default 66.7%).
    /// </summary>
    SuperMajority,

    /// <summary>
    /// All voters must agree.
    /// </summary>
    Unanimity,

    /// <summary>
    /// Weighted consensus using vote weights.
    /// </summary>
    WeightedConsensus,

    /// <summary>
    /// Borda count ranking method.
    /// </summary>
    BordaCount
}

/// <summary>
/// Strategy interface for tallying votes and determining consensus.
/// </summary>
public interface IVotingStrategy
{
    /// <summary>
    /// Tallies the provided votes according to the given options.
    /// </summary>
    /// <param name="votes">The votes to tally.</param>
    /// <param name="options">Voting options controlling consensus rules.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the vote tally.</returns>
    System.Threading.Tasks.Task<VoteResult> TallyVotesAsync(
        IReadOnlyList<Vote> votes,
        VotingOptions options,
        CancellationToken ct = default);
}
