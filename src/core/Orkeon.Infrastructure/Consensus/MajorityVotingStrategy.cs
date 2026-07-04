using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Consensus;

/// <summary>
/// Voting strategy that supports Majority, SuperMajority, and Unanimity consensus types.
/// Also handles weighted votes when enabled.
/// </summary>
public sealed class MajorityVotingStrategy : IVotingStrategy
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
        {
            return Task.FromResult(new VoteResult
            {
                ConsensusReached = false,
                WinningChoice = null,
                AgreementScore = 0f,
                TotalVotes = 0,
                VotesForWinner = 0,
                Scores = new Dictionary<string, float>(),
                AllVotes = votes
            });
        }

        // Calculate scores per choice
        var scores = new Dictionary<string, float>();
        var voteCounts = new Dictionary<string, int>();

        foreach (var vote in votes)
        {
            var choice = vote.Choice;
            if (string.IsNullOrWhiteSpace(choice))
                continue;

            var weight = options.UseWeightedVotes ? vote.Weight * vote.Confidence : 1f;

            if (!scores.TryGetValue(choice, out var currentScore))
            {
                currentScore = 0f;
                voteCounts[choice] = 0;
            }

            scores[choice] = currentScore + weight;
            voteCounts[choice]++;
        }

        if (scores.Count == 0)
        {
            return Task.FromResult(new VoteResult
            {
                ConsensusReached = false,
                WinningChoice = null,
                AgreementScore = 0f,
                TotalVotes = votes.Count,
                VotesForWinner = 0,
                Scores = scores,
                AllVotes = votes
            });
        }

        // Find the winner
        var totalScore = scores.Values.Sum();
        var winner = scores.OrderByDescending(kvp => kvp.Value).First();
        var winnerChoice = winner.Key;
        var winnerScore = winner.Value;
        var winnerVoteCount = voteCounts.GetValueOrDefault(winnerChoice, 0);

        // Calculate agreement as percentage of total score
        var agreementScore = totalScore > 0 ? (winnerScore / totalScore) * 100f : 0f;

        // Determine threshold based on consensus type
        var threshold = options.ConsensusType switch
        {
            ConsensusType.Majority => 50f,
            ConsensusType.SuperMajority => options.ConsensusThreshold,
            ConsensusType.Unanimity => 100f,
            ConsensusType.WeightedConsensus => options.ConsensusThreshold,
            _ => 50f
        };

        var consensusReached = agreementScore > threshold;

        // For unanimity, also check that there is only one choice
        if (options.ConsensusType == ConsensusType.Unanimity)
        {
            consensusReached = scores.Count == 1;
        }

        return Task.FromResult(new VoteResult
        {
            ConsensusReached = consensusReached,
            WinningChoice = winnerChoice,
            AgreementScore = agreementScore,
            TotalVotes = votes.Count,
            VotesForWinner = winnerVoteCount,
            Scores = scores,
            AllVotes = votes
        });
    }
}
