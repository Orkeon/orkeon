using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Consensus;

/// <summary>
/// Voting strategy that applies role-based weights before delegating to majority logic.
/// Agents with specific roles can have higher or lower voting weight.
/// </summary>
public sealed class WeightedConsensusStrategy : IVotingStrategy
{
    private readonly IReadOnlyDictionary<string, float> _roleWeights;
    private readonly MajorityVotingStrategy _majorityStrategy;

    /// <summary>
    /// Creates a new weighted consensus strategy with the specified role weights.
    /// </summary>
    /// <param name="roleWeights">Dictionary mapping role names to weight multipliers.</param>
    public WeightedConsensusStrategy(IReadOnlyDictionary<string, float> roleWeights)
    {
        ArgumentNullException.ThrowIfNull(roleWeights);
        _roleWeights = roleWeights;
        _majorityStrategy = new MajorityVotingStrategy();
    }

    /// <inheritdoc />
    public Task<VoteResult> TallyVotesAsync(
        IReadOnlyList<Vote> votes,
        VotingOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(votes);
        ArgumentNullException.ThrowIfNull(options);

        // Apply role-based weights to votes
        var weightedVotes = votes.Select(vote =>
        {
            var roleWeight = _roleWeights.TryGetValue(vote.VoterRole, out var w) ? w : 1.0f;
            return vote with { Weight = vote.Weight * roleWeight };
        }).ToList();

        // Force weighted voting on
        var weightedOptions = new VotingOptions
        {
            ConsensusType = options.ConsensusType == ConsensusType.BordaCount
                ? ConsensusType.WeightedConsensus
                : options.ConsensusType,
            QuorumPercent = options.QuorumPercent,
            ConsensusThreshold = options.ConsensusThreshold,
            UseWeightedVotes = true,
            MaxVotingRounds = options.MaxVotingRounds,
            AllowAbstention = options.AllowAbstention
        };

        return _majorityStrategy.TallyVotesAsync(weightedVotes, weightedOptions, ct);
    }
}
