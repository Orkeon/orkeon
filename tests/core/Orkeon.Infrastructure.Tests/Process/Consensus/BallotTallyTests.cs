using Orkeon.Application.Interfaces;
using Orkeon.Infrastructure.Consensus;

namespace Orkeon.Infrastructure.Tests.Process.Consensus;

/// <summary>
/// GAP-04 — what the tallies do with peer ballots: a voter's own answer is left out of its
/// share, a tie at the top is no consensus, and the quorum and abstention options change the
/// result. Hand-built ballots without <see cref="Vote.OwnChoice"/> keep their meaning.
/// </summary>
public sealed class BallotTallyTests
{
    private static Vote Ballot(string voter, string choice, string? own = null) =>
        new() { VoterId = voter, Choice = choice, OwnChoice = own };

    private static Task<VoteResult> TallyAsync(IVotingStrategy strategy, IReadOnlyList<Vote> votes, VotingOptions options) =>
        strategy.TallyVotesAsync(votes, options, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_share_is_counted_among_the_ballots_that_could_name_the_choice()
    {
        // a, b and c answered A, B and C; b and c rank A first, a ranks B first.
        var votes = new[] { Ballot("a", "A", own: "A") with { Choice = "B" }, Ballot("b", "A", own: "B"), Ballot("c", "A", own: "C") };

        var result = await TallyAsync(new UnanimityVotingStrategy(), votes, new VotingOptions());

        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(100f, result.AgreementScore, 0.01f);
    }

    [Fact]
    public async Task A_tie_at_the_top_is_no_consensus()
    {
        // Two agents can only name each other.
        var votes = new[] { Ballot("a", "B", own: "A"), Ballot("b", "A", own: "B") };

        var result = await TallyAsync(new MajorityVotingStrategy(), votes, new VotingOptions());

        Assert.False(result.ConsensusReached);
        Assert.NotNull(result.WinningChoice);
    }

    [Theory]
    [InlineData(50f, false)]
    [InlineData(25f, true)]
    public async Task The_quorum_is_the_share_of_expressed_ballots(float quorum, bool reached)
    {
        var votes = new[] { Ballot("a", "A"), Ballot("b", ""), Ballot("c", ""), Ballot("d", "") };

        var result = await TallyAsync(new MajorityVotingStrategy(), votes, new VotingOptions { QuorumPercent = quorum });

        Assert.Equal(reached, result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
    }

    [Theory]
    [InlineData(true, true, 100f)]
    [InlineData(false, false, 66.67f)]
    public async Task A_refused_abstention_counts_against_every_choice(bool allowAbstention, bool reached, float share)
    {
        var votes = new[] { Ballot("a", "A"), Ballot("b", "A"), Ballot("c", "") };
        var options = new VotingOptions { ConsensusThreshold = 75f, AllowAbstention = allowAbstention };

        var result = await TallyAsync(new SuperMajorityVotingStrategy(), votes, options);

        Assert.Equal(reached, result.ConsensusReached);
        Assert.Equal(share, result.AgreementScore, 0.01f);
    }

    [Fact]
    public async Task A_refused_abstention_breaks_unanimity()
    {
        var votes = new[] { Ballot("a", "A"), Ballot("b", "A"), Ballot("c", "") };

        var allowed = await TallyAsync(new UnanimityVotingStrategy(), votes, new VotingOptions());
        var refused = await TallyAsync(new UnanimityVotingStrategy(), votes, new VotingOptions { AllowAbstention = false });

        Assert.True(allowed.ConsensusReached);
        Assert.False(refused.ConsensusReached);
    }

    [Fact]
    public async Task Borda_reaches_no_consensus_on_a_tie_at_the_top()
    {
        // Two agents each rank the other's single answer: zero points each.
        var votes = new[] { Ballot("a", "B", own: "A"), Ballot("b", "A", own: "B") };

        var result = await TallyAsync(new BordaCountStrategy(), votes, new VotingOptions { ConsensusType = ConsensusType.BordaCount });

        Assert.False(result.ConsensusReached);
    }

    [Theory]
    [InlineData(50f, true, true)]
    [InlineData(80f, true, false)]
    [InlineData(50f, false, false)]
    public async Task Borda_applies_quorum_and_abstention(float quorum, bool allowAbstention, bool reached)
    {
        var votes = new[] { Ballot("a", "B,C"), Ballot("b", "B,C"), Ballot("c", "") };
        var options = new VotingOptions
        {
            ConsensusType = ConsensusType.BordaCount,
            QuorumPercent = quorum,
            AllowAbstention = allowAbstention,
        };

        var result = await TallyAsync(new BordaCountStrategy(), votes, options);

        Assert.Equal(reached, result.ConsensusReached);
        Assert.Equal("B", result.WinningChoice);
    }
}
