using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Consensus;

/// <summary>
/// Default <see cref="IVotingStrategyFactory"/>: maps each <see cref="ConsensusType"/>
/// (the configured voting mechanism) to its dedicated <see cref="IVotingStrategy"/>
/// implementation.
/// </summary>
/// <remarks>
/// The mechanism is typically configured via
/// <c>Orkeon:Consensus:VotingOptions:ConsensusType</c>
/// (<see cref="ConsensualProcessOptions.VotingOptions"/>). <see cref="ConsensusType.Majority"/>
/// is the backward-compatible default.
/// </remarks>
public sealed class VotingStrategyFactory : IVotingStrategyFactory
{
    private readonly ConsensualProcessOptions _options;

    /// <summary>Initializes a new instance of <see cref="VotingStrategyFactory"/>.</summary>
    /// <param name="options">
    /// Consensual process options; supplies the role weights used by
    /// <see cref="WeightedConsensusStrategy"/>.
    /// </param>
    public VotingStrategyFactory(IOptions<ConsensualProcessOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public IVotingStrategy Create(ConsensusType consensusType) => consensusType switch
    {
        ConsensusType.SuperMajority => new SuperMajorityVotingStrategy(),
        ConsensusType.Unanimity => new UnanimityVotingStrategy(),
        ConsensusType.WeightedConsensus => new WeightedConsensusStrategy(_options.RoleWeights),
        ConsensusType.BordaCount => new BordaCountStrategy(),
        // Majority is both the explicit mechanism and the fallback (backward-compatible default).
        _ => new MajorityVotingStrategy()
    };
}
