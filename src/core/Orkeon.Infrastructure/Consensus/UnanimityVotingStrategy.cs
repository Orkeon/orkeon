using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Consensus;

/// <summary>
/// Voting strategy requiring unanimity: consensus is reached only when every cast vote
/// designates the same choice. Abstentions (empty choices) are skipped by the tally and
/// therefore do not count as dissent; an election with no cast vote never reaches
/// consensus.
/// </summary>
public sealed class UnanimityVotingStrategy : IVotingStrategy
{
    private readonly MajorityVotingStrategy _tally = new();

    /// <inheritdoc />
    public Task<VoteResult> TallyVotesAsync(
        IReadOnlyList<Vote> votes,
        VotingOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(votes);
        ArgumentNullException.ThrowIfNull(options);

        // Delegate to the majority tally pinned on the unanimity consensus rules: the
        // winner is computed the same way and consensus requires a single distinct
        // choice across all cast votes.
        var unanimityOptions = new VotingOptions
        {
            ConsensusType = ConsensusType.Unanimity,
            QuorumPercent = options.QuorumPercent,
            ConsensusThreshold = options.ConsensusThreshold,
            UseWeightedVotes = options.UseWeightedVotes,
            MaxVotingRounds = options.MaxVotingRounds,
            AllowAbstention = options.AllowAbstention
        };

        return _tally.TallyVotesAsync(votes, unanimityOptions, ct);
    }
}
