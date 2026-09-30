using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Consensus;

/// <summary>
/// Voting strategy that supports Majority, SuperMajority, and Unanimity consensus types.
/// Also handles weighted votes when enabled.
/// </summary>
/// <remarks>
/// <para>
/// A choice's share is its score divided by the weight of the ballots that could name it
/// (GAP-04): a ballot whose <see cref="Vote.OwnChoice"/> is that choice is left out, since a
/// voter may not vote for its own answer. Without <see cref="Vote.OwnChoice"/>, every ballot
/// could name every choice and the share is the plain share of the expressed score.
/// </para>
/// <para>
/// An empty <see cref="Vote.Choice"/> is an abstention. Consensus also requires the quorum
/// (<see cref="VotingOptions.QuorumPercent"/>, the share of expressed ballots) and a single
/// leading choice: a tie at the top is no consensus. With
/// <see cref="VotingOptions.AllowAbstention"/> false, an abstention counts as a vote against
/// every choice.
/// </para>
/// </remarks>
public sealed class MajorityVotingStrategy : IVotingStrategy
{
    /// <summary>
    /// Tolerance (in percentage points) absorbing the floating-point gap between an exact
    /// share (e.g. 66.666…% for 2 ballots out of 3) and a one-decimal threshold (66.7%).
    /// </summary>
    internal const float RoundingTolerancePercent = 0.05f;

    /// <inheritdoc />
    public Task<VoteResult> TallyVotesAsync(
        IReadOnlyList<Vote> votes,
        VotingOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(votes);
        ArgumentNullException.ThrowIfNull(options);

        var threshold = options.ConsensusType switch
        {
            ConsensusType.SuperMajority => options.ConsensusThreshold,
            ConsensusType.WeightedConsensus => options.ConsensusThreshold,
            _ => 50f
        };

        return Task.FromResult(Tally(votes, options, share => share > threshold));
    }

    /// <summary>
    /// The tally every majority-family strategy shares: scores, shares, quorum, abstention,
    /// unanimity and the single-leader rule. <paramref name="carries"/> decides whether the
    /// leading share passes the threshold of the calling strategy.
    /// </summary>
    internal static VoteResult Tally(IReadOnlyList<Vote> votes, VotingOptions options, Func<float, bool> carries)
    {
        if (votes.Count == 0)
            return NoConsensus(votes, new Dictionary<string, float>());

        var expressed = votes.Where(v => !string.IsNullOrWhiteSpace(v.Choice)).ToList();
        var abstentions = votes.Where(v => string.IsNullOrWhiteSpace(v.Choice)).ToList();

        float WeightOf(Vote vote) => options.UseWeightedVotes ? vote.Weight * vote.Confidence : 1f;

        // Calculate scores per choice
        var scores = new Dictionary<string, float>();
        var voteCounts = new Dictionary<string, int>();
        foreach (var vote in expressed)
        {
            scores[vote.Choice] = scores.GetValueOrDefault(vote.Choice) + WeightOf(vote);
            voteCounts[vote.Choice] = voteCounts.GetValueOrDefault(vote.Choice) + 1;
        }

        if (scores.Count == 0)
            return NoConsensus(votes, scores);

        // A choice's share counts only the ballots that could name it; a refused abstention
        // stays in every denominator, as a vote against every choice.
        float ShareOf(string choice)
        {
            var eligible = expressed.Where(v => v.OwnChoice != choice).Sum(WeightOf);
            if (!options.AllowAbstention)
                eligible += abstentions.Where(v => v.OwnChoice != choice).Sum(WeightOf);
            return eligible > 0 ? scores[choice] / eligible * 100f : 0f;
        }

        var shares = scores.Keys.ToDictionary(choice => choice, ShareOf);

        // Find the winner (the first met among equals, so a tie never depends on scoring order)
        var winner = shares.OrderByDescending(kvp => kvp.Value).First();
        var winnerChoice = winner.Key;
        var agreementScore = winner.Value;

        var singleLeader = !shares.Any(kvp => kvp.Key != winnerChoice
            && Math.Abs(kvp.Value - agreementScore) < RoundingTolerancePercent);
        var quorumMet = (float)expressed.Count / votes.Count * 100f + RoundingTolerancePercent >= options.QuorumPercent;

        var carried = options.ConsensusType == ConsensusType.Unanimity
            ? IsUnanimous(winnerChoice, expressed, abstentions, options.AllowAbstention)
            : carries(agreementScore);

        return new VoteResult
        {
            ConsensusReached = carried && singleLeader && quorumMet,
            WinningChoice = winnerChoice,
            AgreementScore = agreementScore,
            TotalVotes = votes.Count,
            VotesForWinner = voteCounts.GetValueOrDefault(winnerChoice, 0),
            Scores = scores,
            AllVotes = votes
        };
    }

    /// <summary>
    /// Unanimity: every expressed ballot that could name the winner named it, and — when
    /// abstention is refused — no ballot that could name it abstained.
    /// </summary>
    private static bool IsUnanimous(
        string winner, List<Vote> expressed, List<Vote> abstentions, bool allowAbstention)
        => expressed.Where(v => v.OwnChoice != winner).All(v => v.Choice == winner)
           && (allowAbstention || abstentions.TrueForAll(v => v.OwnChoice == winner));

    private static VoteResult NoConsensus(IReadOnlyList<Vote> votes, Dictionary<string, float> scores) => new()
    {
        ConsensusReached = false,
        WinningChoice = null,
        AgreementScore = 0f,
        TotalVotes = votes.Count,
        VotesForWinner = 0,
        Scores = scores,
        AllVotes = votes
    };
}
