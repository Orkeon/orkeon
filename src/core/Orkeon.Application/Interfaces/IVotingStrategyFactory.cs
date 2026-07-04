namespace Orkeon.Application.Interfaces;

/// <summary>
/// Resolves the <see cref="IVotingStrategy"/> implementation matching a configured
/// <see cref="ConsensusType"/> (voting mechanism) for consensual crew execution.
/// </summary>
public interface IVotingStrategyFactory
{
    /// <summary>
    /// Creates the voting strategy implementing the given consensus mechanism.
    /// </summary>
    /// <param name="consensusType">The configured voting mechanism.</param>
    /// <returns>The matching voting strategy (never null).</returns>
    IVotingStrategy Create(ConsensusType consensusType);
}
