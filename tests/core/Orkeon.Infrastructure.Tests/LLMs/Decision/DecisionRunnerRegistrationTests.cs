using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Decision;

public class DecisionRunnerRegistrationTests
{
    [Fact]
    [Trait("BR", "UC-02")]
    [Trait("BR", "BR-05")]
    public void ShouldGiveDecisionRunnerToStrategies_WhenResolvedFromContainer()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<IDecisionModel>(new FakeDecisionModel())
            ;
        services.AddOrkeonInfrastructure();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var dependencies = scope.ServiceProvider.GetRequiredService<CrewStrategyDependencies>();

        Assert.NotNull(dependencies.DecisionRunner);
        Assert.Same(provider.GetRequiredService<DecisionTaskRunner>(), dependencies.DecisionRunner);
    }
}
