using Orkeon.Application.Interfaces;
using Orkeon.Infrastructure.Consensus;

namespace Orkeon.Infrastructure.Tests.Process;

/// <summary>
/// R3.3 — Tests for <see cref="UnanimityVotingStrategy"/>: consensus requires every
/// cast vote to designate the same choice.
/// </summary>
public class UnanimityVotingStrategyTests
{
    private static Vote MakeVote(
        string voterId,
        string choice,
        float confidence = 1.0f,
        float weight = 1.0f,
        string role = "worker")
        => new()
        {
            VoterId = voterId,
            VoterRole = role,
            Choice = choice,
            Confidence = confidence,
            Weight = weight,
            Justification = $"From {voterId}",
            Timestamp = DateTime.UtcNow
        };

    #region Argument guards

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenVotesNull()
    {
        var strategy = new UnanimityVotingStrategy();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => strategy.TallyVotesAsync(null!, new VotingOptions(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenOptionsNull()
    {
        var strategy = new UnanimityVotingStrategy();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => strategy.TallyVotesAsync([MakeVote("a", "A")], null!, TestContext.Current.CancellationToken));
    }

    #endregion

    #region Unanimity semantics

    [Fact]
    public async Task ShouldReachConsensus_WhenAllVotesDesignateSameChoice()
    {
        var strategy = new UnanimityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "A")
        };

        var result = await strategy.TallyVotesAsync(votes, new VotingOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(3, result.TotalVotes);
        Assert.Equal(3, result.VotesForWinner);
        Assert.Equal(100f, result.AgreementScore, 0.1f);
    }

    [Fact]
    public async Task ShouldNotReachConsensus_WhenSingleDissenterEvenWithLargeMajority()
    {
        var strategy = new UnanimityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "A"),
            MakeVote("agent4", "A"), MakeVote("agent5", "B")
        };

        var result = await strategy.TallyVotesAsync(votes, new VotingOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice); // The leading choice is still reported.
    }

    [Fact]
    public async Task ShouldReachConsensus_WhenSingleVote()
    {
        var strategy = new UnanimityVotingStrategy();
        var votes = new List<Vote> { MakeVote("agent1", "A") };

        var result = await strategy.TallyVotesAsync(votes, new VotingOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
    }

    [Fact]
    public async Task ShouldReachConsensus_WhenAbstentionsDoNotCountAsDissent()
    {
        // Empty choices are skipped by the tally — unanimity is over cast votes.
        var strategy = new UnanimityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "")
        };

        var result = await strategy.TallyVotesAsync(votes, new VotingOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
    }

    [Fact]
    public async Task ShouldNotReachConsensus_WhenNoVotes()
    {
        var strategy = new UnanimityVotingStrategy();

        var result = await strategy.TallyVotesAsync([], new VotingOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.ConsensusReached);
        Assert.Null(result.WinningChoice);
        Assert.Equal(0, result.TotalVotes);
    }

    [Fact]
    public async Task ShouldNotReachConsensus_WhenAllVotesAreAbstentions()
    {
        var strategy = new UnanimityVotingStrategy();
        var votes = new List<Vote> { MakeVote("agent1", ""), MakeVote("agent2", "") };

        var result = await strategy.TallyVotesAsync(votes, new VotingOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.ConsensusReached);
        Assert.Null(result.WinningChoice);
    }

    [Fact]
    public async Task ShouldEnforceUnanimity_WhenOptionsCarryAnotherConsensusType()
    {
        // The strategy pins unanimity rules regardless of the options' ConsensusType.
        var strategy = new UnanimityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "B")
        };
        var options = new VotingOptions { ConsensusType = ConsensusType.Majority };

        var result = await strategy.TallyVotesAsync(votes, options, TestContext.Current.CancellationToken);

        Assert.False(result.ConsensusReached);
    }

    #endregion
}
