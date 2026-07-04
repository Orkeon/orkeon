using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.AgentSelection;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Stubs;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// Verifies that <see cref="IAgentSelectionService"/> is resolved according to
/// <see cref="OrkeonApplicationOptions.AgentSelectionStrategy"/> (R3.5):
/// "embedding" -> <see cref="EmbeddingBasedSelectionStrategy"/>,
/// "skill" -> <see cref="SkillMatchingSelectionStrategy"/>,
/// default -> first-fit <see cref="SimpleAgentSelectionService"/> (explicit fallback).
/// </summary>
public class AgentSelectionServiceRegistrationTests
{
    private static IServiceProvider BuildProvider(AgentSelectionStrategyKind? kind)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (kind is not null)
        {
            services.Configure<OrkeonApplicationOptions>(o => o.AgentSelectionStrategy = kind.Value);
        }

        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ShouldResolveEmbeddingStrategy_WhenConfiguredEmbedding()
    {
        var provider = BuildProvider(AgentSelectionStrategyKind.Embedding);

        var service = provider.GetRequiredService<IAgentSelectionService>();

        var strategyService = Assert.IsType<StrategyAgentSelectionService>(service);
        Assert.IsType<EmbeddingBasedSelectionStrategy>(GetStrategy(strategyService));
    }

    [Fact]
    public void ShouldResolveSkillStrategy_WhenConfiguredSkill()
    {
        var provider = BuildProvider(AgentSelectionStrategyKind.Skill);

        var service = provider.GetRequiredService<IAgentSelectionService>();

        var strategyService = Assert.IsType<StrategyAgentSelectionService>(service);
        Assert.IsType<SkillMatchingSelectionStrategy>(GetStrategy(strategyService));
    }

    [Fact]
    public void ShouldNotResolveFirstFitStub_WhenConfiguredEmbedding()
    {
        var provider = BuildProvider(AgentSelectionStrategyKind.Embedding);

        var service = provider.GetRequiredService<IAgentSelectionService>();

        Assert.IsNotType<SimpleAgentSelectionService>(service);
    }

    [Fact]
    public void ShouldResolveFirstFitStub_WhenNotConfigured()
    {
        var provider = BuildProvider(kind: null);

        var service = provider.GetRequiredService<IAgentSelectionService>();

        Assert.IsType<SimpleAgentSelectionService>(service);
    }

    [Fact]
    public void ShouldResolveFirstFitStub_WhenConfiguredFirstFit()
    {
        var provider = BuildProvider(AgentSelectionStrategyKind.FirstFit);

        var service = provider.GetRequiredService<IAgentSelectionService>();

        Assert.IsType<SimpleAgentSelectionService>(service);
    }

    private static IAgentSelectionStrategy GetStrategy(StrategyAgentSelectionService service)
    {
        var field = typeof(StrategyAgentSelectionService)
            .GetField("_strategy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (IAgentSelectionStrategy)field!.GetValue(service)!;
    }
}
