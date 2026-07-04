using Orkeon.Application.Interfaces;
using Orkeon.Infrastructure.Consensus;

namespace Orkeon.Infrastructure.Tests.Process;

/// <summary>
/// R3.3 — Tests for <see cref="SuperMajorityVotingStrategy"/>: consensus requires the
/// winning choice to gather at least the configured share of the total vote score
/// (two thirds by default).
/// </summary>
public class SuperMajorityVotingStrategyTests
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

    #region Constructor guards

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(100.1f)]
    public void ShouldThrowArgumentOutOfRangeException_WhenConstructorThresholdOutsideValidRange(float threshold)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SuperMajorityVotingStrategy(threshold));
        Assert.Equal("thresholdPercent", exception.ParamName);
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(50f)]
    [InlineData(100f)]
    public void ShouldInitialize_WhenConstructorThresholdWithinValidRange(float threshold)
    {
        var strategy = new SuperMajorityVotingStrategy(threshold);
        Assert.NotNull(strategy);
    }

    #endregion

    #region Argument guards

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenVotesNull()
    {
        var strategy = new SuperMajorityVotingStrategy();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => strategy.TallyVotesAsync(null!, new VotingOptions(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenOptionsNull()
    {
        var strategy = new SuperMajorityVotingStrategy();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => strategy.TallyVotesAsync([MakeVote("a", "A")], null!, TestContext.Current.CancellationToken));
    }

    #endregion

    #region Default two-thirds threshold

    [Fact]
    public async Task ShouldReachConsensus_WhenExactlyTwoThirdsOfVotesWithDefaultThreshold()
    {
        // 2 votes out of 3 = 66.66…% — the canonical two-thirds super-majority.
        var strategy = new SuperMajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "B")
        };

        var result = await strategy.TallyVotesAsync(votes, new VotingOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(3, result.TotalVotes);
        Assert.Equal(2, result.VotesForWinner);
    }

    [Fact]
    public async Task ShouldNotReachConsensus_WhenSimpleMajorityBelowTwoThirds()
    {
        // 3 votes out of 5 = 60% — a simple majority, but below the super-majority.
        var strategy = new SuperMajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "A"),
            MakeVote("agent4", "B"), MakeVote("agent5", "B")
        };

        var result = await strategy.TallyVotesAsync(votes, new VotingOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice); // The winner is still reported.
        Assert.Equal(60f, result.AgreementScore, 0.1f);
    }

    [Fact]
    public async Task ShouldReachConsensus_WhenAllVotesAgree()
    {
        var strategy = new SuperMajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "A")
        };

        var result = await strategy.TallyVotesAsync(votes, new VotingOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.ConsensusReached);
        Assert.Equal(100f, result.AgreementScore, 0.1f);
    }

    #endregion

    #region Configurable threshold

    [Fact]
    public async Task ShouldReachConsensus_WhenConstructorThresholdLoweredBelowVoteShare()
    {
        // 60% share with an explicit 50% threshold.
        var strategy = new SuperMajorityVotingStrategy(50f);
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "A"),
            MakeVote("agent4", "B"), MakeVote("agent5", "B")
        };

        var result = await strategy.TallyVotesAsync(votes, new VotingOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
    }

    [Fact]
    public async Task ShouldNotReachConsensus_WhenConstructorThresholdRaisedAboveVoteShare()
    {
        // 75% share with an explicit 80% threshold.
        var strategy = new SuperMajorityVotingStrategy(80f);
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "A"),
            MakeVote("agent4", "B")
        };

        var result = await strategy.TallyVotesAsync(votes, new VotingOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.ConsensusReached);
    }

    [Fact]
    public async Task ShouldUseConstructorThreshold_WhenBothConstructorAndOptionsThresholdsProvided()
    {
        // Constructor threshold (80%) takes precedence over options (50%): 60% share fails.
        var strategy = new SuperMajorityVotingStrategy(80f);
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "A"),
            MakeVote("agent4", "B"), MakeVote("agent5", "B")
        };
        var options = new VotingOptions { ConsensusThreshold = 50f };

        var result = await strategy.TallyVotesAsync(votes, options, TestContext.Current.CancellationToken);

        Assert.False(result.ConsensusReached);
    }

    [Fact]
    public async Task ShouldUseOptionsThreshold_WhenNoConstructorThresholdProvided()
    {
        // Options threshold 75%: a 75% share reaches consensus (at-least semantics).
        var strategy = new SuperMajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "A"),
            MakeVote("agent4", "B")
        };
        var options = new VotingOptions { ConsensusThreshold = 75f };

        var result = await strategy.TallyVotesAsync(votes, options, TestContext.Current.CancellationToken);

        Assert.True(result.ConsensusReached);
        Assert.Equal(75f, result.AgreementScore, 0.1f);
    }

    [Fact]
    public async Task ShouldNotReachConsensus_WhenVoteShareBelowOptionsThreshold()
    {
        // Options threshold 75%: a two-thirds share is not enough.
        var strategy = new SuperMajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "B")
        };
        var options = new VotingOptions { ConsensusThreshold = 75f };

        var result = await strategy.TallyVotesAsync(votes, options, TestContext.Current.CancellationToken);

        Assert.False(result.ConsensusReached);
    }

    #endregion

    #region Weighted votes and edge cases

    [Fact]
    public async Task ShouldReachConsensus_WhenWeightedVotesPushWinnerToTwoThirds()
    {
        // Weighted: A scores 2 (weight 2), B scores 1 → A share = 2/3.
        var strategy = new SuperMajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A", weight: 2f),
            MakeVote("agent2", "B", weight: 1f)
        };
        var options = new VotingOptions { UseWeightedVotes = true };

        var result = await strategy.TallyVotesAsync(votes, options, TestContext.Current.CancellationToken);

        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
    }

    [Fact]
    public async Task ShouldNotReachConsensus_WhenNoVotes()
    {
        var strategy = new SuperMajorityVotingStrategy();

        var result = await strategy.TallyVotesAsync([], new VotingOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.ConsensusReached);
        Assert.Null(result.WinningChoice);
        Assert.Equal(0, result.TotalVotes);
    }

    [Fact]
    public async Task ShouldNotReachConsensus_WhenAllVotesAreAbstentions()
    {
        var strategy = new SuperMajorityVotingStrategy();
        var votes = new List<Vote> { MakeVote("agent1", ""), MakeVote("agent2", "") };

        var result = await strategy.TallyVotesAsync(votes, new VotingOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.ConsensusReached);
        Assert.Null(result.WinningChoice);
    }

    #endregion
}
