using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Llm;
using Orkeon.Infrastructure.Decision;
using Orkeon.Infrastructure.Decision.SystemOne;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The decision models of a shipped host (ADR-015, ADR-016): TypeSafe is registered when the
/// settings carry an <c>Orkeon:TypeSafe</c> section, the decision profiles always.
/// </summary>
public sealed class RunnerHostDecisionModelsTests
{
    private static readonly Func<string, string?> NoEnvironment = _ => null;

    private static ServiceCollection Register(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        var context = new HostBuilderContext(new Dictionary<object, object>()) { Configuration = configuration };
        RunnerHost.RegisterDecisionModels(context, services, NoEnvironment);
        return services;
    }

    [Fact]
    public void ShouldRegisterTheTypeSafeDecisionModel_WhenTheSettingsHoldATypeSafeSection()
    {
        var services = Register(new Dictionary<string, string?> { ["Orkeon:TypeSafe:Model"] = "test" });

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IDecisionModel));
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Contains(services, d => d.ServiceType == typeof(SystemOneDecisionModel));
    }

    [Fact]
    [Trait("BR", "R-64")]
    public void ShouldRegisterDecisionModel_WhenTypeSafeSectionIsEmpty()
    {
        using var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"Orkeon\":{\"TypeSafe\":{}}}"));
        var configuration = new ConfigurationBuilder().AddJsonStream(json).Build();
        var services = new ServiceCollection();
        var context = new HostBuilderContext(new Dictionary<object, object>()) { Configuration = configuration };

        RunnerHost.RegisterDecisionModels(context, services, NoEnvironment);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IDecisionModel));
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Contains(services, d => d.ServiceType == typeof(SystemOneDecisionModel));
    }

    [Theory]
    [Trait("BR", "BR-01")]
    [Trait("BR", "UC-01")]
    [Trait("BR", "UC-02")]
    [InlineData("typesafe")]
    [InlineData("liquid")]
    [InlineData("kev")]
    public void ShouldRegisterTheActiveProviderAlone_WhenItIsChosen(string key)
    {
        var services = Register(new Dictionary<string, string?> { ["Orkeon:Decision:Provider"] = key });
        services.AddLogging();

        Assert.Single(services, d => d.ServiceType == typeof(IDecisionModel));
        using var provider = services.BuildServiceProvider();
        var model = provider.GetRequiredService<IDecisionModel>();
        Assert.Equal(key, model.Name);
        Assert.Same(model, provider.GetRequiredService<IDecisionModel>());
        Assert.Equal(key, provider.GetRequiredService<DecisionProviderChoice>().Provider!.Key);
        Assert.NotNull(provider.GetRequiredService<DecisionInputGuard>());
        Assert.Single(services, d => d.ServiceType != typeof(IDecisionModel) && d.ImplementationType == typeof(Orkeon.Infrastructure.Tools.SystemOneDecideTool));
        Assert.IsType<Orkeon.Rag.Reranking.DecisionModelReranker>(
            Orkeon.Rag.DependencyInjection.RagRerankingExtensions.AddOrkeonRagReranking(services)
                .BuildServiceProvider().GetRequiredService<Orkeon.Rag.Factories.RerankerFactory>().Create("decision"));
    }

    [Fact]
    [Trait("BR", "BR-03")]
    public void ShouldRegisterNoDecisionModel_WhenNothingIsConfigured()
    {
        var services = Register(new Dictionary<string, string?> { ["Orkeon:Decision:Enabled"] = "true" });

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDecisionModel));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(SystemOneDecisionModel));
        using var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<DecisionProviderChoice>().HasProvider);
    }

    [Theory]
    [Trait("BR", "BR-04")]
    [Trait("BR", "BR-14")]
    [Trait("BR", "BR-17")]
    [InlineData("both")]
    [InlineData("unknown")]
    [InlineData("kev")]
    public void ShouldRefuseTheStart_WhenTheChoiceIsRefused(string scenario)
    {
        var values = scenario switch
        {
            "both" => new Dictionary<string, string?> { ["Orkeon:TypeSafe:Model"] = "a", ["Orkeon:Liquid:Model"] = "b" },
            "unknown" => new Dictionary<string, string?> { ["Orkeon:Decision:Provider"] = "nope" },
            _ => new Dictionary<string, string?>
            {
                ["Orkeon:Decision:Provider"] = "kev",
                ["Orkeon:Kev:ApiKey"] = "k",
                ["Orkeon:Kev:BaseUrl"] = "http://10.0.0.5:8009/v1",
            },
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var context = new HostBuilderContext(new Dictionary<object, object>()) { Configuration = configuration };

        var error = Assert.Throws<RunnerSettingsException>(
            () => RunnerHost.RegisterDecisionModels(context, new ServiceCollection(), NoEnvironment));

        Assert.Contains(scenario switch
        {
            "both" => "Orkeon:Decision:Provider",
            "unknown" => "'nope'",
            _ => "http://10.0.0.5:8009/v1",
        }, error.Message, StringComparison.Ordinal);
    }
}
