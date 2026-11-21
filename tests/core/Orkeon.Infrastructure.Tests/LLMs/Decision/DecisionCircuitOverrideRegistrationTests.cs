using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Decision;

/// <summary>
/// A scope's <see cref="DecisionCircuitOverride"/> (#431): what a forge trial uses to run one pass with System One
/// and one without it, on the same host. The root's singletons stay the host's own.
/// </summary>
public class DecisionCircuitOverrideRegistrationTests
{
    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<IDecisionModel>(new FakeDecisionModel());
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public void A_scope_whose_override_is_off_gives_the_strategies_a_runner_with_the_circuit_off()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<DecisionCircuitOverride>().Circuit = DecisionCircuit.OffBy(DecisionCircuitCause.Switch);

        var dependencies = scope.ServiceProvider.GetRequiredService<CrewStrategyDependencies>();

        Assert.False(dependencies.DecisionRunner!.Circuit.Enabled);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public void A_scope_without_an_override_keeps_the_hosts_runner()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        var dependencies = scope.ServiceProvider.GetRequiredService<CrewStrategyDependencies>();

        Assert.Same(provider.GetRequiredService<DecisionTaskRunner>(), dependencies.DecisionRunner);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public void A_scope_whose_override_is_on_leaves_the_runner_off_when_the_host_is_off()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<IDecisionModel>(new FakeDecisionModel());
        services.AddOrkeonInfrastructure();
        services.AddSingleton(DecisionCircuit.Off);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<DecisionCircuitOverride>().Circuit = DecisionCircuit.On;

        var dependencies = scope.ServiceProvider.GetRequiredService<CrewStrategyDependencies>();

        Assert.False(dependencies.DecisionRunner!.Circuit.Enabled);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public void A_scope_whose_override_is_off_gives_the_crew_orchestrator_the_circuit_it_enters_for_the_run()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<DecisionCircuitOverride>().Circuit = DecisionCircuit.OffBy(DecisionCircuitCause.Switch);

        var orchestrator = scope.ServiceProvider.GetRequiredService<Orkeon.Infrastructure.Orchestration.SequentialCrewOrchestrator>();

        Assert.Equal(DecisionCircuit.OffBy(DecisionCircuitCause.Switch), orchestrator.RunScopeCircuit);
    }
}
