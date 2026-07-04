using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Consensus;

/// <summary>
/// Voting strategy using the Borda count method.
/// Rankings are encoded in the Choice field as comma-separated values: "choice1,choice2,choice3"
/// where the first item is ranked highest. Points are assigned as N-1 for 1st, N-2 for 2nd, etc.
/// Borda count always produces a winner.
/// </summary>
public sealed class BordaCountStrategy : IVotingStrategy
{
    /// <inheritdoc />
    public Task<VoteResult> TallyVotesAsync(
        IReadOnlyList<Vote> votes,
        VotingOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(votes);
        ArgumentNullException.ThrowIfNull(options);

        if (votes.Count == 0)
            return Task.FromResult(CreateNoConsensusResult(votes, []));

        var allRankings = ParseAllRankings(votes);
        var maxRankedItems = allRankings.Count > 0 ? allRankings.Max(r => r.Length) : 0;

        if (maxRankedItems == 0)
            return Task.FromResult(CreateNoConsensusResult(votes, []));

        var scores = CalculateBordaScores(votes, allRankings, options);

        if (scores.Count == 0)
            return Task.FromResult(CreateNoConsensusResult(votes, scores));

        return Task.FromResult(BuildWinnerResult(votes, allRankings, scores));
    }

    private static VoteResult CreateNoConsensusResult(IReadOnlyList<Vote> votes, Dictionary<string, float> scores)
    {
        return new VoteResult
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

    private static List<string[]> ParseAllRankings(IReadOnlyList<Vote> votes)
    {
        return votes
            .Select(v => v.Choice.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
    }

    private static Dictionary<string, float> CalculateBordaScores(
        IReadOnlyList<Vote> votes, List<string[]> allRankings, VotingOptions options)
    {
        var scores = new Dictionary<string, float>();

        for (int i = 0; i < votes.Count; i++)
        {
            var rankings = allRankings[i];
            var voterWeight = options.UseWeightedVotes ? votes[i].Weight : 1f;
            AccumulateVoterScores(scores, rankings, voterWeight);
        }

        return scores;
    }

    private static void AccumulateVoterScores(
        Dictionary<string, float> scores, string[] rankings, float voterWeight)
    {
        var n = rankings.Length;
        for (int rank = 0; rank < n; rank++)
        {
            var choice = rankings[rank];
            if (string.IsNullOrWhiteSpace(choice))
                continue;

            var points = (n - 1 - rank) * voterWeight;

            if (!scores.TryGetValue(choice, out var currentScore))
                currentScore = 0f;

            scores[choice] = currentScore + points;
        }
    }

    private static VoteResult BuildWinnerResult(
        IReadOnlyList<Vote> votes, List<string[]> allRankings, Dictionary<string, float> scores)
    {
        var winner = scores.OrderByDescending(kvp => kvp.Value).First();
        var winnerChoice = winner.Key;
        var totalScore = scores.Values.Sum();
        var agreementScore = totalScore > 0 ? (winner.Value / totalScore) * 100f : 0f;
        var votesForWinner = allRankings.Count(r => r.Length > 0 && r[0] == winnerChoice);

        return new VoteResult
        {
            ConsensusReached = true,
            WinningChoice = winnerChoice,
            AgreementScore = agreementScore,
            TotalVotes = votes.Count,
            VotesForWinner = votesForWinner,
            Scores = scores,
            AllVotes = votes
        };
    }
}
