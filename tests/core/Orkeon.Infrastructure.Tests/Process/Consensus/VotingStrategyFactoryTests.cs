using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces;
using Orkeon.Infrastructure.Consensus;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Infrastructure.Tests.Process;

/// <summary>
/// R3.3 — Tests for <see cref="VotingStrategyFactory"/> and the configuration-driven
/// voting mechanism selection wired by <c>AddOrkeonConsensus</c> (DoD: configuring
/// BordaCount/WeightedConsensus selects the dedicated strategy, not Majority).
/// </summary>
public class VotingStrategyFactoryTests
{
    private static VotingStrategyFactory CreateFactory(ConsensualProcessOptions? options = null)
        => new(Options.Create(options ?? new ConsensualProcessOptions()));

    #region Constructor guards

    [Fact]
    public void ShouldThrowArgumentNullException_WhenOptionsNull()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new VotingStrategyFactory(null!));
        Assert.Equal("options", exception.ParamName);
    }

    #endregion

    #region Mechanism → strategy mapping

    [Fact]
    public void ShouldReturnMajorityStrategy_WhenMajorityMechanism()
        => Assert.IsType<MajorityVotingStrategy>(CreateFactory().Create(ConsensusType.Majority));

    [Fact]
    public void ShouldReturnSuperMajorityStrategy_WhenSuperMajorityMechanism()
        => Assert.IsType<SuperMajorityVotingStrategy>(CreateFactory().Create(ConsensusType.SuperMajority));

    [Fact]
    public void ShouldReturnUnanimityStrategy_WhenUnanimityMechanism()
        => Assert.IsType<UnanimityVotingStrategy>(CreateFactory().Create(ConsensusType.Unanimity));

    [Fact]
    public void ShouldReturnWeightedConsensusStrategy_WhenWeightedConsensusMechanism()
        => Assert.IsType<WeightedConsensusStrategy>(CreateFactory().Create(ConsensusType.WeightedConsensus));

    [Fact]
    public void ShouldReturnBordaCountStrategy_WhenBordaCountMechanism()
        => Assert.IsType<BordaCountStrategy>(CreateFactory().Create(ConsensusType.BordaCount));

    [Fact]
    public async Task ShouldApplyConfiguredRoleWeights_WhenWeightedConsensusMechanism()
    {
        // The factory must flow ConsensualProcessOptions.RoleWeights into the strategy:
        // a single senior vote (weight 3) beats two worker votes.
        var factory = CreateFactory(new ConsensualProcessOptions
        {
            RoleWeights = { ["senior"] = 3f }
        });
        var strategy = factory.Create(ConsensusType.WeightedConsensus);

        var votes = new List<Vote>
        {
            new() { VoterId = "s1", VoterRole = "senior", Choice = "A", Confidence = 1f, Weight = 1f },
            new() { VoterId = "w1", VoterRole = "worker", Choice = "B", Confidence = 1f, Weight = 1f },
            new() { VoterId = "w2", VoterRole = "worker", Choice = "B", Confidence = 1f, Weight = 1f }
        };

        var result = await strategy.TallyVotesAsync(
            votes,
            new VotingOptions { ConsensusType = ConsensusType.WeightedConsensus, ConsensusThreshold = 50f },
            TestContext.Current.CancellationToken);

        Assert.Equal("A", result.WinningChoice);
        Assert.True(result.ConsensusReached);
    }

    #endregion

    #region DI selection (AddOrkeonConsensus) — DoD R3.3

    private static ServiceProvider BuildProvider(string? configuredMechanism)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configValues = new Dictionary<string, string?>();
        if (configuredMechanism is not null)
        {
            configValues["Orkeon:Consensus:VotingOptions:ConsensusType"] = configuredMechanism;
        }

        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(configValues).Build());

        services.AddOrkeonConsensus();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void ShouldResolveMajorityStrategy_WhenNothingConfigured()
    {
        // Backward-compatible default: the historical MajorityVotingStrategy.
        using var provider = BuildProvider(configuredMechanism: null);

        var strategy = provider.GetRequiredService<IVotingStrategy>();

        Assert.IsType<MajorityVotingStrategy>(strategy);
    }

    [Fact]
    public void ShouldResolveBordaCountStrategy_WhenBordaCountConfigured()
    {
        using var provider = BuildProvider("BordaCount");

        var strategy = provider.GetRequiredService<IVotingStrategy>();

        Assert.IsType<BordaCountStrategy>(strategy);
        Assert.IsNotType<MajorityVotingStrategy>(strategy);
    }

    [Fact]
    public void ShouldResolveWeightedConsensusStrategy_WhenWeightedConsensusConfigured()
    {
        using var provider = BuildProvider("WeightedConsensus");

        var strategy = provider.GetRequiredService<IVotingStrategy>();

        Assert.IsType<WeightedConsensusStrategy>(strategy);
        Assert.IsNotType<MajorityVotingStrategy>(strategy);
    }

    [Fact]
    public void ShouldResolveSuperMajorityStrategy_WhenSuperMajorityConfigured()
    {
        using var provider = BuildProvider("SuperMajority");

        var strategy = provider.GetRequiredService<IVotingStrategy>();

        Assert.IsType<SuperMajorityVotingStrategy>(strategy);
    }

    [Fact]
    public void ShouldResolveUnanimityStrategy_WhenUnanimityConfigured()
    {
        using var provider = BuildProvider("Unanimity");

        var strategy = provider.GetRequiredService<IVotingStrategy>();

        Assert.IsType<UnanimityVotingStrategy>(strategy);
    }

    [Fact]
    public void ShouldRespectHostOverride_WhenIVotingStrategyAlreadyRegistered()
    {
        // TryAdd semantics: a host-registered IVotingStrategy wins over the selector.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IVotingStrategy>(new BordaCountStrategy());

        services.AddOrkeonConsensus();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<BordaCountStrategy>(provider.GetRequiredService<IVotingStrategy>());
    }

    [Fact]
    public void ShouldResolveVotingStrategyFactory_WhenAddOrkeonConsensus()
    {
        using var provider = BuildProvider(configuredMechanism: null);

        var factory = provider.GetRequiredService<IVotingStrategyFactory>();

        Assert.IsType<VotingStrategyFactory>(factory);
    }

    #endregion
}
