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
    public void A_scope_whose_override_is_off_gives_the_strategies_a_runner_with_the_circuit_off()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<DecisionCircuitOverride>().Circuit = DecisionCircuit.OffBy(DecisionCircuitCause.Switch);

        var dependencies = scope.ServiceProvider.GetRequiredService<CrewStrategyDependencies>();

        Assert.False(dependencies.DecisionRunner!.Circuit.Enabled);
    }

    [Fact]
    public void A_scope_without_an_override_keeps_the_hosts_runner()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        var dependencies = scope.ServiceProvider.GetRequiredService<CrewStrategyDependencies>();

        Assert.Same(provider.GetRequiredService<DecisionTaskRunner>(), dependencies.DecisionRunner);
    }
}
