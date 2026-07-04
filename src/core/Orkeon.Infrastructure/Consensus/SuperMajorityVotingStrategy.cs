using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Consensus;

/// <summary>
/// Voting strategy requiring a super-majority: consensus is reached when the winning
/// choice gathers at least the configured share of the total vote score
/// (two thirds by default).
/// </summary>
/// <remarks>
/// The effective threshold is, in order of precedence: the value supplied to the
/// constructor, otherwise <see cref="VotingOptions.ConsensusThreshold"/> (whose default
/// of 66.7% is the one-decimal representation of the canonical two-thirds
/// super-majority). A small rounding tolerance ensures that an exact two-thirds share
/// (e.g. 2 votes out of 3, computed as 66.666…%) reaches consensus against that 66.7
/// representation.
/// </remarks>
public sealed class SuperMajorityVotingStrategy : IVotingStrategy
{
    /// <summary>
    /// Tolerance (in percentage points) absorbing the floating-point rounding gap between
    /// an exact vote share (e.g. 66.666…% for 2 votes out of 3) and the one-decimal
    /// representation of the two-thirds threshold (66.7%).
    /// </summary>
    public const float RoundingTolerancePercent = 0.05f;

    private readonly float? _thresholdPercent;
    private readonly MajorityVotingStrategy _tally = new();

    /// <summary>
    /// Creates a strategy whose threshold comes from
    /// <see cref="VotingOptions.ConsensusThreshold"/> at tally time
    /// (defaults to the two-thirds super-majority).
    /// </summary>
    public SuperMajorityVotingStrategy()
        : this(thresholdPercent: null)
    {
    }

    /// <summary>
    /// Creates a strategy with an explicit threshold.
    /// </summary>
    /// <param name="thresholdPercent">
    /// The share of the total vote score, in percent (exclusive 0 to inclusive 100), that
    /// the winning choice must reach. When null,
    /// <see cref="VotingOptions.ConsensusThreshold"/> applies at tally time.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="thresholdPercent"/> is outside (0, 100].
    /// </exception>
    public SuperMajorityVotingStrategy(float? thresholdPercent)
    {
        if (thresholdPercent is { } threshold && (threshold <= 0f || threshold > 100f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(thresholdPercent),
                threshold,
                "Super-majority threshold must be within (0, 100].");
        }

        _thresholdPercent = thresholdPercent;
    }

    /// <inheritdoc />
    public Task<VoteResult> TallyVotesAsync(
        IReadOnlyList<Vote> votes,
        VotingOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(votes);
        ArgumentNullException.ThrowIfNull(options);
        return TallyVotesCoreAsync();

        async Task<VoteResult> TallyVotesCoreAsync()
        {
            // Reuse the proven majority tally for score/winner computation, then apply
            // the super-majority threshold on the winning share.
            var tallyOptions = new VotingOptions
            {
                ConsensusType = ConsensusType.Majority,
                QuorumPercent = options.QuorumPercent,
                ConsensusThreshold = options.ConsensusThreshold,
                UseWeightedVotes = options.UseWeightedVotes,
                MaxVotingRounds = options.MaxVotingRounds,
                AllowAbstention = options.AllowAbstention
            };

            var result = await _tally.TallyVotesAsync(votes, tallyOptions, ct).ConfigureAwait(false);

            var effectiveThreshold = _thresholdPercent ?? options.ConsensusThreshold;
            var consensusReached = result.WinningChoice is not null
                && result.AgreementScore + RoundingTolerancePercent >= effectiveThreshold;

            return result with { ConsensusReached = consensusReached };
        }
    }
}
