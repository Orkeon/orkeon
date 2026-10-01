using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Evaluation;
using Orkeon.Infrastructure.Evaluation.LlmJudge;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// GAP-15 — the <c>Evaluation</c> configuration section is read by
/// <c>AddOrkeonInfrastructure(configuration)</c>, and <c>AddOrkeonEvaluation</c> is idempotent:
/// calling it again never registers an evaluator twice.
/// </summary>
public class EvaluationRegistrationTests
{
    private static IConfiguration ConfigurationWith(bool enableLlmJudge) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Evaluation:EnableLlmJudge"] = enableLlmJudge ? "true" : "false",
            })
            .Build();

    private static IEvaluationSuite ResolveSuite(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IChatClient, MockChatClient>();
        register(services);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IEvaluationSuite>();
    }

    [Fact]
    public void ShouldIncludeLlmJudges_WhenInfrastructureConfigurationEnablesThem()
    {
        var configuration = ConfigurationWith(enableLlmJudge: true);

        var suite = ResolveSuite(services => services.AddOrkeonInfrastructure(configuration));

        Assert.Contains(suite.Evaluators, e => e is CoherenceEvaluator);
        Assert.Contains(suite.Evaluators, e => e is FluencyEvaluator);
        Assert.Contains(suite.Evaluators, e => e is GroundednessEvaluator);
    }

    [Fact]
    public void ShouldLeaveLlmJudgesOut_WhenConfigurationDoesNotEnableThem()
    {
        var configuration = ConfigurationWith(enableLlmJudge: false);

        var suite = ResolveSuite(services => services.AddOrkeonInfrastructure(configuration));

        Assert.DoesNotContain(suite.Evaluators, e => e is LlmJudgeEvaluatorBase);
        Assert.NotEmpty(suite.Evaluators);
    }

    [Fact]
    public void ShouldRegisterEachEvaluatorOnce_WhenAddOrkeonEvaluationIsCalledAfterInfrastructure()
    {
        var configuration = ConfigurationWith(enableLlmJudge: true);

        var suite = ResolveSuite(services =>
        {
            services.AddOrkeonInfrastructure();
            services.AddOrkeonEvaluation(configuration);
        });

        var names = suite.Evaluators.Select(e => e.Name).ToList();
        Assert.Equal(names.Distinct().Count(), names.Count);
        Assert.Contains(suite.Evaluators, e => e is CoherenceEvaluator);
    }

    [Fact]
    public void ShouldRegisterEachEvaluatorOnce_WhenInfrastructureConfigurationOverloadIsUsed()
    {
        var configuration = ConfigurationWith(enableLlmJudge: true);

        var suite = ResolveSuite(services => services.AddOrkeonInfrastructure(configuration));

        var names = suite.Evaluators.Select(e => e.Name).ToList();
        Assert.Equal(names.Distinct().Count(), names.Count);
    }
}
