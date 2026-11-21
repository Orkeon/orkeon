using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Tests.Doubles;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// The circuit of the run in progress (#433, #431): <see cref="DecisionCircuit.Of(IServiceProvider)"/> reads the run
/// scope's circuit the orchestrator entered, so what is built once per host follows a run whose scope is off; an off
/// circuit always wins, and the container's own circuit stays readable apart.
/// </summary>
public sealed class DecisionCircuitScopeTests
{
    private static ServiceProvider Container(DecisionCircuit? registered = null)
    {
        var services = new ServiceCollection().AddSingleton<IDecisionModel>(new FakeDecisionModel());
        if (registered is not null)
            services.AddSingleton(registered);
        return services.BuildServiceProvider();
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public void ShouldBeOff_WhenTheRunInProgressIsOffAndTheContainerIsOn()
    {
        // Arrange
        using var container = Container(DecisionCircuit.On);
        using var run = DecisionCircuitScope.Begin(DecisionCircuit.Off);

        // Act
        var circuit = DecisionCircuit.Of(container);

        // Assert
        Assert.Equal(DecisionCircuit.Off, circuit);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public void ShouldKeepTheContainersCircuit_WhenAskedForTheHostsOwnDuringAnOffRun()
    {
        // Arrange
        using var container = Container(DecisionCircuit.On);
        using var run = DecisionCircuitScope.Begin(DecisionCircuit.Off);

        // Act
        var circuit = DecisionCircuit.OfHost(container);

        // Assert
        Assert.True(circuit.Enabled);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public void ShouldStayOff_WhenTheScopesCircuitIsOnAndTheContainersIsOff()
    {
        // Arrange
        using var container = Container(DecisionCircuit.OffBy(DecisionCircuitCause.Settings));

        // Act
        var circuit = DecisionCircuit.Of(container, DecisionCircuit.On);

        // Assert
        Assert.Equal(DecisionCircuitCause.Settings, circuit.Cause);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public void ShouldStayOff_WhenAnOnRunIsEnteredInsideAnOffOne()
    {
        // Arrange
        using var container = Container(DecisionCircuit.On);
        using var outer = DecisionCircuitScope.Begin(DecisionCircuit.Off);
        using var inner = DecisionCircuitScope.Begin(DecisionCircuit.On);

        // Act
        var circuit = DecisionCircuit.Of(container);

        // Assert
        Assert.False(circuit.Enabled);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public void ShouldFollowTheContainerAgain_WhenTheOffRunEnds()
    {
        // Arrange
        using var container = Container(DecisionCircuit.On);
        DecisionCircuitScope.Begin(DecisionCircuit.Off).Dispose();

        // Act
        var circuit = DecisionCircuit.Of(container);

        // Assert
        Assert.True(circuit.Enabled);
    }
}
